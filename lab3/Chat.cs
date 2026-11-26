using System.Net;
using System.Net.Sockets;

namespace LanChat;

// un peer. thread-ul principal citeste tastatura, restul pe thread-uri separate:
//   2 x ReceiveLoop (cate unul pe socket), HeartbeatLoop (alive la 5 s), TimeoutLoop (cine a disparut)
public class Chat
{
    static readonly TimeSpan JoinWait = TimeSpan.FromSeconds(1);
    static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(5);
    static readonly TimeSpan GoneAfter = TimeSpan.FromSeconds(15);
    // un alive pierdut (5 s) + marja -> "not responding", inainte sa fie scos la 15 s
    static readonly TimeSpan NotRespondingAfter = TimeSpan.FromSeconds(7);
    // programul merge, dar omul n-a scris nimic de atata timp -> "inactive"
    static readonly TimeSpan InactiveAfter = TimeSpan.FromSeconds(60);

    readonly string myName;
    // id random per proces: cu el imi recunosc ecoul si deosebesc 2 procese cu acelasi nume
    readonly string myId = Guid.NewGuid().ToString("N")[..8];
    readonly Log log;
    readonly Network net;
    readonly Crypto crypto;
    readonly Peers peers = new Peers();

    volatile bool running = true;
    volatile bool nameTaken = false;
    DateTime lastTyped = DateTime.Now;   // ultima linie scrisa de mine; o trimit in fiecare mesaj ca "idle"

    public Chat(string name, int port, string passphrase)
    {
        myName = name;
        log = new Log(name);
        net = new Network(port, log);
        crypto = new Crypto(passphrase);
    }

    public int Run()
    {
        // receive porneste inainte de join, ca sa prind raspunsurile la join
        StartThread(() => ReceiveLoop(net.GroupSocket));
        StartThread(() => ReceiveLoop(net.DirectSocket));
        if (!Join()) return 1;

        StartThread(HeartbeatLoop);
        StartThread(TimeoutLoop);
        ReadKeyboard();
        Leave();
        return 0;
    }

    static void StartThread(Action work)
    {
        Thread thread = new Thread(() => work());
        thread.IsBackground = true;   // nu tine procesul in viata dupa /quit
        thread.Start();
    }

    Message NewMessage(string type, string? text = null)
    {
        int idle = (int)(DateTime.Now - lastTyped).TotalSeconds;
        return new Message { Type = type, From = myName, Id = myId, Text = text, Idle = idle };
    }

    // ---------- intrare / iesire ----------

    // anunt pe toti ca am intrat, apoi 1 s strang raspunsurile "here" (sau "taken")
    bool Join()
    {
        net.SendToAll(NewMessage(MessageType.Join));
        Thread.Sleep(JoinWait);

        if (nameTaken)
        {
            Console.WriteLine($"* the name {myName} is already used, choose another one");
            net.SendToAll(NewMessage(MessageType.Bye));
            return false;
        }
        Console.WriteLine($"* you joined the chat as {myName} (ip {net.LocalAddress})");
        ShowUsers();
        return true;
    }

    void Leave()
    {
        running = false;
        net.SendToAll(NewMessage(MessageType.Bye));
        Console.WriteLine("* you left the chat");
    }

    // ---------- tastatura ----------

    void ReadKeyboard()
    {
        Console.WriteLine("* commands: text | /msg name text | /users | /quit");
        while (true)
        {
            string? line = Console.ReadLine();
            if (line == null) return;   // ctrl+d / stdin inchis
            line = line.Trim();
            if (line == "/quit") return;
            if (line != "") lastTyped = DateTime.Now;
            HandleLine(line);
        }
    }

    void HandleLine(string line)
    {
        if (line == "") return;
        if (line == "/users") ShowUsers();
        else if (line.StartsWith("/msg ")) SendPrivate(line.Substring(5));
        else if (line.StartsWith("/")) Console.WriteLine("* unknown command");
        else SendGeneral(line);
    }

    void SendGeneral(string text)
    {
        if (!FitsLength(text)) return;
        // nu il afisez aici: il afisez cand se intoarce din retea (ecoul), asa apare o singura data
        net.SendToAll(NewMessage(MessageType.General, text));
    }

    void SendPrivate(string nameAndText)
    {
        string[] parts = nameAndText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            Console.WriteLine("* usage: /msg name text");
            return;
        }
        string to = parts[0];
        string text = parts[1];
        if (!FitsLength(text)) return;

        EndPoint? endPoint = peers.FindEndPoint(to);
        if (endPoint == null)
        {
            Console.WriteLine($"* {to} is not online");
            return;
        }
        // un singur unicast direct la el, nu la toti cu numele lui inauntru
        net.SendTo(NewPrivate(text), endPoint);
        Console.WriteLine($"[private to {to}] {myName}: {text}");
    }

    Message NewPrivate(string text)
    {
        (string nonce, string data) = crypto.Encrypt(text, myName);
        Message message = NewMessage(MessageType.Private, data);
        message.Nonce = nonce;
        return message;
    }

    static bool FitsLength(string text)
    {
        if (text.Length <= Message.MaxTextLength) return true;
        Console.WriteLine($"* too long: {text.Length} characters, max {Message.MaxTextLength}");
        return false;
    }

    void ShowUsers()
    {
        List<Peer> others = peers.All();
        Console.WriteLine($"* online: {others.Count + 1}");
        Console.WriteLine($"*   {myName} (you)");
        foreach (Peer peer in others)
        {
            Console.WriteLine($"*   {peer.Name} {peer.EndPoint}  {Status(peer)}");
        }
    }

    static string Status(Peer peer)
    {
        if (peer.NotResponding) return $"not responding ({Duration(DateTime.Now - peer.LastSeen)})";
        if (peer.Inactive) return $"inactive ({Duration(DateTime.Now - peer.LastTyped)})";
        return "active";
    }

    static string Duration(TimeSpan time)
    {
        if (time.TotalMinutes < 1) return $"{time.Seconds} s";
        return $"{(int)time.TotalMinutes} min {time.Seconds} s";
    }

    // ---------- primire ----------

    void ReceiveLoop(Socket socket)
    {
        byte[] buffer = new byte[65536];   // cat mai mare datagrama UDP, ca sa nu se taie nimic
        while (running)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int length;
            try
            {
                length = socket.ReceiveFrom(buffer, ref from);
            }
            catch (SocketException)
            {
                // ex pe windows: am trimis un privat cuiva care a plecat -> ICMP "port unreachable"
                // apare aici ca eroare. nu e grav, continui
                continue;
            }
            OnDatagram(buffer, length, from);
        }
    }

    void OnDatagram(byte[] buffer, int length, EndPoint from)
    {
        Message? message = Message.Parse(buffer, length, out string error);
        if (message == null)
        {
            log.Dropped(from, error, length);
            return;
        }
        log.Received(from, message.Type, length);
        Handle(message, from);
    }

    void Handle(Message message, EndPoint from)
    {
        if (message.Id == myId) OnMyEcho(message);
        else if (message.From == myName) OnSameName(message, from);
        else OnOtherPeer(message, from);
    }

    // multicastul se intoarce si la mine (loopback). din ecourile mele afisez doar textul
    void OnMyEcho(Message message)
    {
        if (message.Type == MessageType.General) ShowGeneral(message);
    }

    // alt proces cu numele meu. daca pornesc 2 deodata, fiecare primeste join-ul celuilalt -> ies amandoi
    void OnSameName(Message message, EndPoint from)
    {
        if (message.Type == MessageType.Join) net.SendTo(NewMessage(MessageType.Taken), from);   // el nu intra
        if (message.Type == MessageType.Taken) nameTaken = true;                                 // eu nu intru
    }

    void OnOtherPeer(Message message, EndPoint from)
    {
        if (message.Type != MessageType.Bye && peers.Heard(message.From, message.Id))
            Console.WriteLine($"* {message.From} is responding again");

        switch (message.Type)
        {
            case MessageType.Join: OnJoin(message, from); break;
            case MessageType.Here: peers.Touch(message.From, message.Id, from); break;
            case MessageType.Alive: OnAlive(message, from); break;
            case MessageType.Bye: OnBye(message); break;
            case MessageType.General: ShowGeneral(message); break;
            case MessageType.Private: ShowPrivate(message, from); break;
        }

        // dupa switch, ca un peer nou (join/here) sa fie deja in lista
        if (message.Type != MessageType.Bye && message.Idle != null) UpdateIdle(message);
    }

    void UpdateIdle(Message message)
    {
        bool? change = peers.SetIdle(message.From, message.Id, TimeSpan.FromSeconds(message.Idle!.Value), InactiveAfter);
        if (change == true) Console.WriteLine($"* {message.From} is inactive (nothing typed for {Duration(TimeSpan.FromSeconds(message.Idle.Value))})");
        if (change == false) Console.WriteLine($"* {message.From} is active again");
    }

    // cineva nou: il adaug si ii raspund direct, ca sa afle de mine acum, nu peste 5 s
    void OnJoin(Message message, EndPoint from)
    {
        if (peers.Touch(message.From, message.Id, from)) Console.WriteLine($"* {message.From} joined the chat");
        net.SendTo(NewMessage(MessageType.Here), from);
    }

    // alive de la un necunoscut = i s-a pierdut join-ul, sau revine dupa timeout
    void OnAlive(Message message, EndPoint from)
    {
        if (peers.Touch(message.From, message.Id, from)) Console.WriteLine($"* {message.From} joined the chat");
    }

    void OnBye(Message message)
    {
        if (peers.Remove(message.From, message.Id)) Console.WriteLine($"* {message.From} left the chat");
    }

    static void ShowGeneral(Message message)
    {
        Console.WriteLine($"[general] {message.From}: {message.Text}");
    }

    void ShowPrivate(Message message, EndPoint from)
    {
        string? text = crypto.Decrypt(message.Nonce!, message.Text!, message.From);
        if (text == null || text.Length > Message.MaxTextLength)
        {
            log.Dropped(from, "cannot decrypt", message.Size);
            return;
        }
        Console.WriteLine($"[private] {message.From}: {text}");
    }

    // ---------- timere ----------

    void HeartbeatLoop()
    {
        while (running)
        {
            Thread.Sleep(HeartbeatEvery);
            if (running) net.SendToAll(NewMessage(MessageType.Alive));
        }
    }

    // UDP n-are conexiune, deci nu aflu cand moare un proces. il consider plecat dupa 15 s de liniste
    // (= 3 alive-uri pierdute la rand, ca o singura datagrama pierduta sa nu-l scoata)
    void TimeoutLoop()
    {
        while (running)
        {
            Thread.Sleep(1000);
            foreach (string name in peers.MarkNotResponding(NotRespondingAfter))
            {
                Console.WriteLine($"* {name} is not responding (no news for {NotRespondingAfter.TotalSeconds} s)");
            }
            foreach (string name in peers.RemoveSilentFor(GoneAfter))
            {
                Console.WriteLine($"* {name} left the chat (no news for {GoneAfter.TotalSeconds} s)");
            }
        }
    }
}
