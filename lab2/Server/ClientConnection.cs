using System.Net.Sockets;

namespace CoffeeShop;

// O conexiune (un client sau providerul). Thread-ul ei ruleaza Run():
// citeste comenzile una cate una si raspunde la ele.
public class ClientConnection
{
    readonly Socket socket;

    // Pe acest socket trimit thread-ul clientului, workerii (READY) si alti clienti (ALERT).
    // Lock-ul are grija ca doua mesaje sa nu se amestece niciodata.
    readonly object sendLock = new object();

    // Comenzile acestei conexiuni, ca sa poata fi anulate cand ea dispare.
    // Lista e folosita doar de thread-ul acestei conexiuni, deci nu are nevoie de lock.
    readonly List<Order> myOrders = new List<Order>();

    public string Endpoint;

    public ClientConnection(Socket socket)
    {
        this.socket = socket;
        Endpoint = socket.RemoteEndPoint?.ToString() ?? "unknown";
        SetupSocket();
    }

    void SetupSocket()
    {
        // Un client care nu citeste niciodata ar bloca Send la nesfarsit (si workerul odata cu el).
        // Dupa 5 secunde Send da eroare si consideram ca clientul a plecat.
        socket.SendTimeout = 5000;
        try
        {
            // Keep-alive: daca nu vine nimic 10 s, TCP verifica daca cealalta parte mai exista.
            // Asa aflam de clientii a caror retea a disparut (fara FIN, fara RST).
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 3);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }
        catch (SocketException)
        {
            // Nu e suportat pe acest sistem: aflam doar cand un Send da eroare.
        }
    }

    public void Run()
    {
        Log.Write(Endpoint, "connected");
        try
        {
            Send("WELCOME to the coffee shop. Commands: MENU | ORDER <product> <qty> ... | PROVIDER | DELIVER <product> <qty> ... | QUIT");
            ReadCommands();
        }
        catch (SocketException e)
        {
            Log.Write(Endpoint, "connection lost: " + e.Message);   // ex: reset, procesul a fost omorat
        }
        catch (ObjectDisposedException)
        {
            Log.Write(Endpoint, "connection closed");
        }
        catch (Exception e)
        {
            // Orice alta eroare nu trebuie sa opreasca tot serverul: se termina doar aceasta conexiune.
            Log.Write(Endpoint, "error: " + e.Message);
        }
        finally
        {
            Disconnect();
        }
    }

    void ReadCommands()
    {
        LineReader reader = new LineReader(socket);
        while (true)
        {
            string? line = reader.ReadLine();
            if (line == null)
            {
                Log.Write(Endpoint, "closed the connection");
                return;
            }
            if (line == LineReader.TooLong)
            {
                SendError("message longer than " + Protocol.MaxLineBytes + " bytes");
                continue;
            }
            bool keepGoing = HandleCommand(line);
            if (!keepGoing)
            {
                return;
            }
        }
    }

    // Intoarce false cand conexiunea trebuie sa se termine (QUIT).
    bool HandleCommand(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            SendError("empty command");
            return true;
        }

        string command = parts[0].ToUpperInvariant();
        if (command == "MENU")
        {
            HandleMenu();
        }
        else if (command == "ORDER")
        {
            HandleOrder(parts);
        }
        else if (command == "PROVIDER")
        {
            HandleProvider();
        }
        else if (command == "DELIVER")
        {
            HandleDeliver(parts);
        }
        else if (command == "QUIT")
        {
            Send("BYE");
            Log.Write(Endpoint, "quit");
            return false;
        }
        else
        {
            SendError("unknown command '" + parts[0] + "'");
        }
        return true;
    }

    void HandleMenu()
    {
        Send("MENU " + Shop.Menu());
    }

    void HandleOrder(string[] parts)
    {
        Dictionary<string, int>? items = Shop.ParseItems(parts, out string error);
        if (items == null)
        {
            SendError(error);
            return;
        }

        string? refusal = Shop.TakeProducts(items);
        if (refusal != null)
        {
            Send("REFUSED " + refusal);
            Log.Write(Endpoint, "order refused: " + refusal);
            return;
        }

        Order order = Kitchen.CreateOrder(this, items);
        myOrders.Add(order);
        // Raspundem imediat. READY vine mai tarziu, de la workerul care pregateste comanda.
        Send("ACCEPTED " + order.Id + " " + order.Seconds);
        Log.Write(Endpoint, "order " + order + " accepted");
        Kitchen.AddToQueue(order);

        // Daca un produs din aceasta comanda a scazut sub 5, providerul afla imediat.
        // Alerta o trimite chiar thread-ul acestui client.
        Provider.SendAlerts(items.Keys);
    }

    void HandleProvider()
    {
        if (Provider.IsProvider(this))
        {
            SendError("you are already the provider");
            return;
        }
        if (!Provider.TryRegister(this))
        {
            SendError("there is already a provider");
            return;
        }
        // TryRegister a trimis deja alertele care asteptau; OK doar confirma rolul.
        Send("OK you are the provider");
        Log.Write(Endpoint, "is the provider");
    }

    void HandleDeliver(string[] parts)
    {
        if (!Provider.IsProvider(this))
        {
            SendError("only the provider can deliver");
            return;
        }
        Dictionary<string, int>? items = Shop.ParseItems(parts, out string error);
        if (items == null)
        {
            SendError(error);
            return;
        }
        Shop.AddProducts(items);
        string stock = Shop.Menu();
        Send("DELIVERED " + stock);
        Log.Write(Endpoint, "delivery " + ItemsText(items) + ", stock now: " + stock);
    }

    // Conexiunea s-a terminat (QUIT, inchisa, reset sau eroare): anulam toate comenzile ei.
    void Disconnect()
    {
        foreach (Order order in myOrders)
        {
            if (Kitchen.CancelOrder(order))
            {
                Shop.AddProducts(order.Items);   // produsele revin in stoc
                Log.Write(Endpoint, "order #" + order.Id + " canceled, products back in stock");
            }
        }
        Provider.Remove(this);   // daca era providerul, magazinul merge mai departe fara el
        socket.Close();
        Log.Write(Endpoint, "disconnected");
    }

    // Trimite eroarea clientului si o scrie in log, ca nicio eroare sa nu lipseasca din log.
    void SendError(string message)
    {
        Send("ERROR " + message);
        Log.Write(Endpoint, "error: " + message);
    }

    // Apelata de thread-ul acestei conexiuni, de workeri si de alti clienti (alerte).
    // Intoarce false cand clientul a plecat.
    public bool Send(string message)
    {
        try
        {
            lock (sendLock)
            {
                Protocol.SendLine(socket, message);
            }
            return true;
        }
        catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
        {
            // Clientul a plecat sau nu citeste. Shutdown trezeste thread-ul lui
            // (blocat in Receive), care apoi ii anuleaza comenzile si inchide socket-ul.
            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch (Exception)
            {
                // deja inchis
            }
            return false;
        }
    }

    static string ItemsText(Dictionary<string, int> items)
    {
        List<string> parts = new List<string>();
        foreach (var item in items)
        {
            parts.Add(item.Value + " " + item.Key);
        }
        return string.Join(", ", parts);
    }
}
