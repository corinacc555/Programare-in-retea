namespace CoffeeShop;

// Tine minte care conexiune e providerul (cel mult unul o data).
// Thread-ul oricarui client poate trimite o alerta, deci accesul e sub slotLock.
public class ProviderSlot
{
    readonly object slotLock = new();
    ClientConnection? provider;

    // False daca alt provider e deja conectat.
    public bool TryRegister(ClientConnection connection)
    {
        lock (slotLock)
        {
            if (provider != null)
                return false;
            provider = connection;
            return true;
        }
    }

    public void Unregister(ClientConnection connection)
    {
        lock (slotLock)
            if (provider == connection)
                provider = null;
        Log.Write(connection.Endpoint, "provider left, the shop goes on with its stock");
    }

    // Trimite alerta providerului, daca e conectat. Daca nu e, o primeste
    // cand se conecteaza: atunci i se trimit toate produsele care sunt inca sub limita.
    public void Alert(string product, int left)
    {
        ClientConnection? current;
        lock (slotLock)
            current = provider;
        // Trimitem in afara lock-ului, ca un provider lent sa nu blocheze ceilalti clienti.
        if (current == null)
        {
            Log.Write("server", $"low stock: {product} {left} left, no provider connected");
            return;
        }
        current.Send($"ALERT {product} only {left} left");
        Log.Write(current.Endpoint, $"alert sent: {product} {left} left");
    }
}
