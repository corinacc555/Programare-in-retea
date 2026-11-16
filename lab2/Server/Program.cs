using System.Net;
using System.Net.Sockets;

namespace CoffeeShop;

public static class Program
{
    public static void Main(string[] args)
    {
        if (!TryParsePort(args, out int port))
        {
            Console.WriteLine("Usage: Server <port>");
            return;
        }

        var shop = new Shop();
        var kitchen = new Kitchen(Environment.ProcessorCount);
        var providers = new ProviderSlot();
        Log.Write("server", $"stock: {shop.Menu()}");
        Log.Write("server", $"{Environment.ProcessorCount} workers started");

        Socket listener = Listen(port);
        AcceptClients(listener, shop, kitchen, providers);
    }

    static bool TryParsePort(string[] args, out int port)
    {
        port = 0;
        return args.Length == 1 && int.TryParse(args[0], out port) && port is >= 1 and <= 65535;
    }

    // Socket-ul care asculta: doar accepta conexiuni, nu trimite si nu primeste date.
    static Socket Listen(int port)
    {
        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Any, port));
        listener.Listen(16);
        Log.Write("server", $"listening on port {port}");
        return listener;
    }

    static void AcceptClients(Socket listener, Shop shop, Kitchen kitchen, ProviderSlot providers)
    {
        while (true)
        {
            try
            {
                Socket socket = listener.Accept();
                // Fiecare client are thread-ul lui, ca acesta sa se intoarca imediat la Accept.
                var client = new ClientConnection(socket, shop, kitchen, providers);
                new Thread(client.Run) { IsBackground = true, Name = $"client-{client.Endpoint}" }.Start();
            }
            catch (SocketException e)
            {
                // Un client care pica in timpul Accept nu trebuie sa opreasca serverul.
                Log.Write("server", $"accept error: {e.Message}");
            }
        }
    }
}
