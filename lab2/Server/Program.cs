using System.Net;
using System.Net.Sockets;

namespace CoffeeShop;

public static class Log
{
    public static void Write(string endpoint, string message) =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} [{endpoint}] {message}");
}

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out int port) || port is < 1 or > 65535)
        {
            Console.WriteLine("Usage: Server <port>");
            return;
        }

        var shop = new Shop();
        var kitchen = new Kitchen(Environment.ProcessorCount);
        Log.Write("server", $"stock: {shop.Menu()}");
        Log.Write("server", $"{Environment.ProcessorCount} workers started");

        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Any, port));
        listener.Listen(16);
        Log.Write("server", $"listening on port {port}");

        while (true)
        {
            Socket socket = listener.Accept();
            // Every client gets its own thread, so this one can go back to Accept at once.
            var client = new ClientConnection(socket, shop, kitchen);
            new Thread(client.Run) { IsBackground = true, Name = $"client-{client.Endpoint}" }.Start();
        }
    }
}
