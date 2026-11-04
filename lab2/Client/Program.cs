using System.Net.Sockets;

namespace CoffeeShop;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out int port) || port is < 1 or > 65535)
        {
            Console.WriteLine("Usage: Client <host> <port>");
            return;
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Connect(args[0], port);
        }
        catch (SocketException e)
        {
            Console.WriteLine($"Cannot connect to {args[0]}:{port}: {e.Message}");
            return;
        }

        // The server can send at any moment (READY comes later than ACCEPTED),
        // so one thread receives while the main thread reads the keyboard.
        var receiver = new Thread(() => Receive(socket)) { IsBackground = true };
        receiver.Start();

        try
        {
            while (Console.ReadLine() is string input)
                LineWriter.SendLine(socket, input);
            socket.Shutdown(SocketShutdown.Send);   // end of input: tell the server we are done
            receiver.Join();
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            Console.WriteLine("Connection lost.");
        }
    }

    static void Receive(Socket socket)
    {
        var reader = new LineReader(socket);
        try
        {
            while (reader.TryReadLine(out string? line))
                Console.WriteLine(line ?? "(message too long)");
            Console.WriteLine("Disconnected from the server.");
        }
        catch (SocketException e)
        {
            Console.WriteLine($"Connection lost: {e.Message}");
        }
        socket.Close();
        Environment.Exit(0);
    }
}
