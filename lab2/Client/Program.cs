using System.Net.Sockets;

namespace CoffeeShop;

// Un singur program pentru ambele roluri: client si provider.
//   Client <host> <port>            -> client
//   Client <host> <port> provider   -> provider
public static class Program
{
    public static void Main(string[] args)
    {
        if (!ReadArguments(args, out string host, out int port, out bool isProvider))
        {
            Console.WriteLine("Usage: Client <host> <port> [provider]");
            return;
        }

        Socket? socket = Connect(host, port);
        if (socket == null)
        {
            return;
        }

        // Serverul poate trimite oricand (READY, ALERT), chiar cand utilizatorul scrie.
        // Deci un thread primeste mesajele, iar thread-ul principal citeste tastatura.
        Thread receiver = new Thread(() => ReceiveLoop(socket));
        receiver.IsBackground = true;
        receiver.Start();

        if (isProvider)
        {
            Protocol.SendLine(socket, "PROVIDER");   // ii spunem serverului cine suntem
            Console.WriteLine("You are the provider. Example: DELIVER coffee 20 croissant 10");
        }
        else
        {
            Console.WriteLine("Example: MENU, ORDER croissant 2 tea 1, QUIT");
        }

        KeyboardLoop(socket);
        receiver.Join();   // asteptam ca serverul sa inchida conexiunea
    }

    static bool ReadArguments(string[] args, out string host, out int port, out bool isProvider)
    {
        host = "";
        port = 0;
        isProvider = false;
        if (args.Length < 2 || args.Length > 3)
        {
            return false;
        }
        host = args[0];
        if (!int.TryParse(args[1], out port) || port < 1 || port > 65535)
        {
            return false;
        }
        if (args.Length == 3)
        {
            if (args[2].ToLowerInvariant() != "provider")
            {
                return false;
            }
            isProvider = true;
        }
        return true;
    }

    // socket + connect. Intoarce null daca serverul nu poate fi gasit.
    static Socket? Connect(string host, int port)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Connect(host, port);
            return socket;
        }
        catch (SocketException e)
        {
            Console.WriteLine("Cannot connect to " + host + ":" + port + ": " + e.Message);
            socket.Close();
            return null;
        }
    }

    // Trimite fiecare linie scrisa de utilizator.
    static void KeyboardLoop(Socket socket)
    {
        try
        {
            while (true)
            {
                string? input = Console.ReadLine();
                if (input == null)
                {
                    break;   // sfarsitul tastaturii (Ctrl+D / Ctrl+Z)
                }
                Protocol.SendLine(socket, input);
            }
            socket.Shutdown(SocketShutdown.Send);   // ii spunem serverului ca am terminat
        }
        catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
        {
            Console.WriteLine("Connection lost.");
            Environment.Exit(0);
        }
    }

    // Afiseaza fiecare mesaj de la server imediat ce soseste.
    static void ReceiveLoop(Socket socket)
    {
        LineReader reader = new LineReader(socket);
        try
        {
            while (true)
            {
                string? line = reader.ReadLine();
                if (line == null)
                {
                    break;
                }
                if (line == LineReader.TooLong)
                {
                    Console.WriteLine("(message from the server too long)");
                }
                else
                {
                    Console.WriteLine(line);
                }
            }
            Console.WriteLine("Disconnected from the server.");
        }
        catch (SocketException e)
        {
            Console.WriteLine("Connection lost: " + e.Message);
        }
        // Serverul a disparut: inchidem tot programul, chiar daca thread-ul principal e in ReadLine.
        socket.Close();
        Environment.Exit(0);
    }
}
