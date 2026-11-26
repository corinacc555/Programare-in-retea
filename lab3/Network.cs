using System.Net;
using System.Net.NetworkInformation;
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

    // ip-ul placii de retea pe care trimit multicastul (wifi / cablu)
    public readonly IPAddress LocalAddress;

    public Network(int port, Log log)
    {
        this.log = log;
        groupEndPoint = new IPEndPoint(Group, port);
        LocalAddress = FindLocalAddress();
        GroupSocket = CreateGroupSocket(port);
        DirectSocket = CreateDirectSocket(LocalAddress);
    }

    // pe windows sunt des mai multe placi de retea: wifi + vEthernet (WSL/Hyper-V), VirtualBox, VPN.
    // cu IPAddress.Any sistemul alege una singura pt multicast, si poate fi una virtuala -> nu ne aude nimeni.
    // aleg placa pe care iese ruta implicita (cea spre router)
    static IPAddress FindLocalAddress()
    {
        try
        {
            using Socket probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // la UDP, Connect nu trimite nimic pe retea, doar alege ruta si ip-ul local
            probe.Connect(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
            return ((IPEndPoint)probe.LocalEndPoint!).Address;
        }
        catch (SocketException)
        {
            return IPAddress.Any;   // fara ruta implicita (ex: cablu direct intre 2 laptopuri): las sistemul sa aleaga
        }
    }

    static Socket CreateGroupSocket(int port)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        // fara asta al 2-lea peer pe acelasi pc ia "address already in use". trebuie INAINTE de bind
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        IgnoreConnectionReset(socket);
        // 0.0.0.0 = primesc pe toate placile de retea (wifi, cablu, loopback), nu doar pe un ip
        socket.Bind(new IPEndPoint(IPAddress.Any, port));
        // intru in grupul multicast, altfel kernelul nu-mi da datagramele trimise la 239.255.42.42.
        // intru pe FIECARE placa de retea, ca sa primesc oricare ar fi placa folosita de ceilalti
        bool joined = false;
        foreach (IPAddress address in LocalIPv4Addresses())
        {
            try
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, address));
                joined = true;
            }
            catch (SocketException) { }   // placa care nu suporta multicast: o sar
        }
        if (!joined) socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, IPAddress.Any));
        return socket;
    }

    static Socket CreateDirectSocket(IPAddress localAddress)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        IgnoreConnectionReset(socket);
        // port 0 = sistemul alege unul liber. e unic, deci un privat trimis aici ajunge doar la mine
        // (pe portul chatului, comun, unicastul ar ajunge doar la UNUL din peers de pe pc)
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);   // nu iese din reteaua locala
        // multicastul trimis iese pe placa reala, nu pe una virtuala
        if (!localAddress.Equals(IPAddress.Any))
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localAddress.GetAddressBytes());
        return socket;
    }

    // ip-urile IPv4 ale placilor de retea pornite, fara loopback
    static IEnumerable<IPAddress> LocalIPv4Addresses()
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (!nic.SupportsMulticast) continue;
            foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily == AddressFamily.InterNetwork) yield return info.Address;
            }
        }
    }

    // pe windows: daca trimit unui peer care a plecat, vine ICMP "port unreachable" si urmatorul
    // ReceiveFrom arunca ConnectionReset. SIO_UDP_CONNRESET = false opreste comportamentul asta
    static void IgnoreConnectionReset(Socket socket)
    {
        if (!OperatingSystem.IsWindows()) return;
        const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
        socket.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
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
