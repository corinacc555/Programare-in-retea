using System.Net.Sockets;

namespace CoffeeShop;

// O conexiune: citeste comenzile clientului (sau ale providerului) si raspunde la ele.
public class ClientConnection
{
    readonly Socket socket;
    readonly Shop shop;
    readonly Kitchen kitchen;
    readonly ProviderSlot providers;
    // Pe acest socket trimit si thread-ul clientului, si workerii, si alertele;
    // lock-ul impiedica amestecarea mesajelor lor.
    readonly object sendLock = new();
    // Comenzile acestui client. Le foloseste doar thread-ul lui, deci nu au nevoie de lock.
    readonly List<Order> orders = new();
    bool isProvider;

    public string Endpoint { get; }

    public ClientConnection(Socket socket, Shop shop, Kitchen kitchen, ProviderSlot providers)
    {
        this.socket = socket;
        this.shop = shop;
        this.kitchen = kitchen;
        this.providers = providers;
        Endpoint = socket.RemoteEndPoint?.ToString() ?? "unknown";
    }

    public void Run()
    {
        Log.Write(Endpoint, "connected");
        try
        {
            EnableKeepAlive();
            Send("WELCOME to the coffee shop. Commands: MENU | ORDER <product> <qty> ... | PROVIDER | DELIVER <product> <qty> ... | QUIT");
            ReadCommands();
            Log.Write(Endpoint, "disconnected");
        }
        catch (SocketException e)
        {
            // Ex: procesul clientului a fost omorat si conexiunea a fost resetata.
            Log.Write(Endpoint, $"connection lost: {e.Message}");
        }
        catch (Exception e)
        {
            // Orice alta eroare opreste doar acest client, nu tot serverul.
            Log.Write(Endpoint, $"error: {e.Message}");
        }
        finally
        {
            Cleanup();
        }
    }

    // Fara keepalive, un client a carui retea a disparut nu e observat niciodata:
    // Receive asteapta la nesfarsit. Cu keepalive, sistemul de operare verifica
    // conexiunea dupa 10 s de liniste si o inchide daca nu raspunde nimeni.
    void EnableKeepAlive()
    {
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10);     // secunde de liniste
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);  // secunde intre verificari
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
    }

    void ReadCommands()
    {
        var reader = new LineReader(socket);
        while (reader.TryReadLine(out string? line))
        {
            if (line == null)
                SendError($"message longer than {LineReader.MaxLineBytes} bytes");
            else if (!Handle(line))
                return;
        }
    }

    // Se apeleaza oricum s-ar termina conexiunea (QUIT, inchidere, eroare).
    void Cleanup()
    {
        CancelOrders();
        if (isProvider)
            providers.Unregister(this);
        socket.Close();
    }

    // Clientul a plecat: comenzile lui care nu sunt gata se anuleaza,
    // iar produsele lor revin in stoc.
    void CancelOrders()
    {
        foreach (Order order in orders)
        {
            if (order.TryCancel())
            {
                shop.Add(order.Items);
                Log.Write(Endpoint, $"order #{order.Id} cancelled, products back in stock");
            }
        }
    }

    // Intoarce false cand clientul vrea sa plece (QUIT).
    bool Handle(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            SendError("empty command");
            return true;
        }
        switch (parts[0].ToUpperInvariant())
        {
            case "MENU":
                Send("MENU " + shop.Menu());
                break;
            case "ORDER":
                HandleOrder(parts);
                break;
            case "PROVIDER":
                HandleProvider();
                break;
            case "DELIVER":
                HandleDeliver(parts);
                break;
            case "QUIT":
                Send("BYE");
                return false;
            default:
                SendError($"unknown command '{parts[0]}'");
                break;
        }
        return true;
    }

    void HandleOrder(string[] parts)
    {
        if (!ItemsParser.TryParse(parts, out var items, out string error))
        {
            SendError(error);
            return;
        }
        if (!shop.TryTake(items, out string shortProduct, out int left, out var nowLow))
        {
            Send($"REFUSED not enough {shortProduct}, only {left} left");
            Log.Write(Endpoint, $"order refused: not enough {shortProduct} ({left} left)");
            return;
        }
        Order order = kitchen.NewOrder(this, items);
        orders.Add(order);
        // Raspundem imediat; READY vine mai tarziu, de la workerul care pregateste comanda.
        Send($"ACCEPTED {order.Id} {order.Seconds}");
        Log.Write(Endpoint, $"order {order} accepted");
        kitchen.Enqueue(order);

        // Alerta o trimite chiar thread-ul clientului care a scazut stocul, imediat.
        foreach (var (product, n) in nowLow)
            providers.Alert(product, n);
    }

    // Conexiunea se anunta ca provider. Serverul o tine minte in ProviderSlot.
    void HandleProvider()
    {
        if (isProvider)
        {
            SendError("you are already the provider");
            return;
        }
        if (!providers.TryRegister(this))
        {
            SendError("another provider is already connected");
            return;
        }
        isProvider = true;
        Send("OK you are the provider");
        Log.Write(Endpoint, "is the provider");

        // Alertele pierdute cat timp providerul nu era conectat.
        foreach (var (product, left) in shop.LowProducts())
            providers.Alert(product, left);
    }

    void HandleDeliver(string[] parts)
    {
        if (!isProvider)
        {
            SendError("only the provider can deliver");
            return;
        }
        if (!ItemsParser.TryParse(parts, out var items, out string error))
        {
            SendError(error);
            return;
        }
        shop.Add(items);
        Send("DELIVERED " + shop.Menu());
        Log.Write(Endpoint, $"delivery: {ItemsParser.ToText(items)}");
    }

    // Trimite eroarea clientului si o scrie in log, ca nicio eroare sa nu lipseasca din log.
    void SendError(string message)
    {
        Send("ERROR " + message);
        Log.Write(Endpoint, "error: " + message);
    }

    // Apelata de thread-ul clientului, de workeri si de alerte. False cand clientul a plecat.
    public bool Send(string line)
    {
        try
        {
            lock (sendLock)
                LineWriter.SendLine(socket, line);
            return true;
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            return false;
        }
    }
}
