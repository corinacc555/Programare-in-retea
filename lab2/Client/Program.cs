using System.Net.Sockets;

namespace CoffeeShop;

// Acelasi program e client sau provider, dupa al treilea argument.
public static class Program
{
    public static void Main(string[] args)
    {
        if (!TryParseArgs(args, out string host, out int port, out bool provider))
        {
            Console.WriteLine("Usage: Client <host> <port> [provider]");
            return;
        }

        Socket? socket = Connect(host, port);
        if (socket == null)
            return;

        // Serverul poate trimite oricand (READY, ALERT), deci un thread primeste
        // mesajele, iar thread-ul principal citeste tastatura.
        var receiver = new Thread(() => Receive(socket)) { IsBackground = true };
        receiver.Start();

        SendKeyboard(socket, provider);
        receiver.Join();
    }

    static bool TryParseArgs(string[] args, out string host, out int port, out bool provider)
    {
        host = args.Length > 0 ? args[0] : "";
        port = 0;
        provider = args.Length == 3 && args[2].Equals("provider", StringComparison.OrdinalIgnoreCase);
        bool roleOk = args.Length == 2 || provider;
        return roleOk && int.TryParse(args[1], out port) && port is >= 1 and <= 65535;
    }

    static Socket? Connect(string host, int port)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Connect(host, port);
            return socket;
        }
        catch (SocketException e)
        {
            Console.WriteLine($"Cannot connect to {host}:{port}: {e.Message}");
            socket.Close();
            return null;
        }
    }

    // Trimite serverului fiecare linie scrisa de utilizator.
    static void SendKeyboard(Socket socket, bool provider)
    {
        try
        {
            if (provider)
                LineWriter.SendLine(socket, "PROVIDER");    // se anunta ca provider
            while (Console.ReadLine() is string input)
                LineWriter.SendLine(socket, input);
            socket.Shutdown(SocketShutdown.Send);   // sfarsitul tastaturii: anuntam serverul ca am terminat
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            Console.WriteLine("Connection lost.");
        }
    }

    // Afiseaza fiecare mesaj de la server imediat ce soseste.
    // Cand serverul dispare, inchide programul curat.
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
