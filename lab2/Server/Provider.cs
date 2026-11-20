namespace CoffeeShop;

// Tine minte care conexiune e providerul si ii trimite alertele.
// Providerul e conexiunea care a trimis comanda PROVIDER. Exista cel mult unul.
public static class Provider
{
    static ClientConnection? current = null;
    static readonly object providerLock = new object();

    // Intoarce false daca alta conexiune este deja provider.
    public static bool TryRegister(ClientConnection connection)
    {
        lock (providerLock)
        {
            if (current != null)
            {
                return false;
            }
            current = connection;
        }
        // Alertele de cand providerul nu era conectat: le primeste acum.
        SendAlerts(Shop.Products);
        return true;
    }

    public static void Remove(ClientConnection connection)
    {
        lock (providerLock)
        {
            if (current == connection)
            {
                current = null;
                Log.Write(connection.Endpoint, "provider left, the shop goes on with its stock");
            }
        }
    }

    public static bool IsProvider(ClientConnection connection)
    {
        lock (providerLock)
        {
            return current == connection;
        }
    }

    // Trimite "ALERT <produs> <n> left" pentru fiecare din aceste produse care e sub LowStock.
    // Apelata de thread-ul clientului imediat dupa ce o comanda a luat produse,
    // si cand providerul se conecteaza.
    public static void SendAlerts(IEnumerable<string> products)
    {
        lock (providerLock)
        {
            if (current == null)
            {
                return;   // nu are cine afla acum; providerul o primeste cand se conecteaza
            }
            foreach (string product in products)
            {
                int left = Shop.Count(product);
                if (left < Shop.LowStock)
                {
                    current.Send("ALERT " + product + " " + left + " left");
                    Log.Write(current.Endpoint, "alert: " + product + " " + left + " left");
                }
            }
        }
    }
}
