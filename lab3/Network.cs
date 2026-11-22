using System.Net;
using System.Net.Sockets;

namespace LanChat;

// cele 2 socket-uri ale unui peer
//   GroupSocket:  0.0.0.0:<port chat>, comun pt toti peers -> primeste canalul general (multicast)
//   DirectSocket: 0.0.0.0:<port random>, doar al meu      -> primeste private + raspunsuri, si trimite TOT
public class Network
{
    public static readonly IPAddress Group = IPAddress.Parse("239.255.42.42");

    public readonly Socket GroupSocket;
    public readonly Socket DirectSocket;

    readonly IPEndPoint groupEndPoint;
    readonly Log log;

    public Network(int port, Log log)
    {
        this.log = log;
        groupEndPoint = new IPEndPoint(Group, port);
        GroupSocket = CreateGroupSocket(port);
        DirectSocket = CreateDirectSocket();
    }

    static Socket CreateGroupSocket(int port)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // fara asta al 2-lea peer pe acelasi pc ia "address already in use". trebuie INAINTE de bind
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        // 0.0.0.0 = primesc pe toate placile de retea (wifi, cablu, loopback), nu doar pe un ip
        socket.Bind(new IPEndPoint(IPAddress.Any, port));
        // intru in grupul multicast, altfel kernelul nu-mi da datagramele trimise la 239.255.42.42
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, IPAddress.Any));
        return socket;
    }

    static Socket CreateDirectSocket()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // port 0 = sistemul alege unul liber. e unic, deci un privat trimis aici ajunge doar la mine
        // (pe portul chatului, comun, unicastul ar ajunge doar la UNUL din peers de pe pc)
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);   // nu iese din reteaua locala
        return socket;
    }

    public void SendToAll(Message message) => SendTo(message, groupEndPoint);

    // trimit mereu de pe DirectSocket: asa cine primeste vede in recvfrom endpoint-ul meu unic
    public void SendTo(Message message, EndPoint to)
    {
        byte[] data = message.ToBytes();
        try
        {
            DirectSocket.SendTo(data, to);
            log.Sent(to, message.Type, data.Length);
        }
        catch (SocketException e)
        {
            Console.WriteLine("* could not send: " + e.Message);
        }
    }

    // nu am Close(): pe macOS Close se blocheaza cat timp alt thread sta in ReceiveFrom.
    // la iesire procesul se termina si sistemul inchide singur socket-urile
}
