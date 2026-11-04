using System.Net.Sockets;

namespace CoffeeShop;

// One connected client: reads its commands and answers them.
public class ClientConnection
{
    readonly Socket socket;
    readonly Shop shop;
    readonly Kitchen kitchen;
    // The client thread and the workers both send on this socket;
    // the lock keeps their messages from mixing.
    readonly object sendLock = new();

    public string Endpoint { get; }

    public ClientConnection(Socket socket, Shop shop, Kitchen kitchen)
    {
        this.socket = socket;
        this.shop = shop;
        this.kitchen = kitchen;
        Endpoint = socket.RemoteEndPoint?.ToString() ?? "unknown";
    }

    public void Run()
    {
        Log.Write(Endpoint, "connected");
        var reader = new LineReader(socket);
        try
        {
            Send("WELCOME to the coffee shop. Commands: MENU | ORDER <product> <qty> ... | QUIT");
            while (reader.TryReadLine(out string? line))
            {
                if (line == null)
                {
                    Send($"ERROR message longer than {LineReader.MaxLineBytes} bytes");
                    Log.Write(Endpoint, "error: message too long");
                }
                else if (!Handle(line))
                    break;
            }
            Log.Write(Endpoint, "disconnected");
        }
        catch (SocketException e)
        {
            Log.Write(Endpoint, $"error: {e.Message}");
        }
        finally
        {
            socket.Close();
        }
    }

    // Returns false when the client asked to leave.
    bool Handle(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            Send("ERROR empty command");
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
            case "QUIT":
                Send("BYE");
                return false;
            default:
                Send($"ERROR unknown command '{parts[0]}'");
                Log.Write(Endpoint, $"error: unknown command '{parts[0]}'");
                break;
        }
        return true;
    }

    void HandleOrder(string[] parts)
    {
        if (!Shop.TryParseItems(parts, out var items, out string error))
        {
            Send("ERROR " + error);
            Log.Write(Endpoint, $"order error: {error}");
            return;
        }
        if (!shop.TryTake(items, out string shortProduct, out int left))
        {
            Send($"REFUSED not enough {shortProduct}, only {left} left");
            Log.Write(Endpoint, $"order refused: not enough {shortProduct} ({left} left)");
            return;
        }
        Order order = kitchen.NewOrder(this, items);
        // Answer at once; READY comes later, from the worker that prepares the order.
        Send($"ACCEPTED {order.Id} {order.Seconds}");
        Log.Write(Endpoint, $"order {order} accepted");
        kitchen.Enqueue(order);
    }

    // Called by the client thread and by workers. Returns false when the client is gone.
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
