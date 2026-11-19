using System.Net;
using System.Net.Sockets;

namespace CoffeeShop;

public static class Program
{
    public static void Main(string[] args)
    {
        int port = ReadPort(args);
        if (port == 0)
        {
            Console.WriteLine("Usage: Server <port>");
            return;
        }

        Shop.LoadStock();
        Log.Write("server", "stock: " + Shop.Menu());

        int workers = Environment.ProcessorCount;   // un worker pentru fiecare procesor logic
        Kitchen.StartWorkers(workers);
        Log.Write("server", workers + " workers started");

        Socket? listener = StartListening(port);
        if (listener == null)
        {
            return;
        }
        AcceptClients(listener);
    }

    // Intoarce portul din linia de comanda, sau 0 daca lipseste ori e gresit.
    static int ReadPort(string[] args)
    {
        if (args.Length != 1)
        {
            return 0;
        }
        if (!int.TryParse(args[0], out int port) || port < 1 || port > 65535)
        {
            return 0;
        }
        return port;
    }

    // socket + bind + listen. Intoarce null daca portul nu poate fi folosit.
    // Socket-ul care asculta doar accepta conexiuni; el nu trimite si nu primeste date.
    static Socket? StartListening(int port)
    {
        Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            // Any = toate placile de retea, ca sa se poata conecta si alte calculatoare.
            listener.Bind(new IPEndPoint(IPAddress.Any, port));
            listener.Listen(16);
        }
        catch (SocketException e)
        {
            Console.WriteLine("Cannot listen on port " + port + ": " + e.Message);
            return null;
        }
        Log.Write("server", "listening on port " + port);
        return listener;
    }

    // Thread-ul principal doar accepta conexiuni. Fiecare conexiune primeste thread-ul ei,
    // ca thread-ul principal sa se poata intoarce imediat la Accept.
    static void AcceptClients(Socket listener)
    {
        while (true)
        {
            Socket socket;
            try
            {
                socket = listener.Accept();
            }
            catch (SocketException e)
            {
                // Un client care pica in timpul Accept nu trebuie sa opreasca serverul.
                Log.Write("server", "accept failed: " + e.Message);
                continue;
            }

            ClientConnection client = new ClientConnection(socket);
            Thread thread = new Thread(client.Run);
            thread.IsBackground = true;
            thread.Start();
        }
    }
}
