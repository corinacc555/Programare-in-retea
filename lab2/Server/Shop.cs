namespace CoffeeShop;

// Stocul magazinului. Il folosesc toate thread-urile, deci orice citire si orice
// schimbare a lui se face in lock (stockLock). Asa, "ajunge?" si "iau produsele"
// sunt un singur pas pe care niciun alt thread nu il poate taia in doua.
public static class Shop
{
    public static readonly string[] Products = { "coffee", "tea", "croissant" };
    public const int LowStock = 5;       // sub aceasta cantitate, providerul primeste alerta
    public const int MaxQuantity = 1000; // cea mai mare cantitate dintr-un produs la ORDER sau DELIVER

    const string StockFile = "stock.txt"; // bonus: stocul ramane si dupa repornirea serverului

    static readonly Dictionary<string, int> stock = new Dictionary<string, int>();
    static readonly object stockLock = new object();

    public static int PrepSeconds(string product)
    {
        if (product == "coffee") return 4;
        if (product == "tea") return 3;
        if (product == "croissant") return 2;
        return 0;
    }

    public static bool IsProduct(string product)
    {
        return Products.Contains(product);
    }

    // Citeste stocul salvat la rularea trecuta. Daca nu exista, fiecare produs
    // primeste o cantitate aleatoare de la 10 la 50.
    public static void LoadStock()
    {
        lock (stockLock)
        {
            if (!ReadStockFile())
            {
                foreach (string product in Products)
                {
                    stock[product] = Random.Shared.Next(10, 51);
                }
            }
            SaveStock();
        }
    }

    // Intoarce "coffee=17 tea=30 croissant=13".
    public static string Menu()
    {
        lock (stockLock)
        {
            return StockText();
        }
    }

    public static int Count(string product)
    {
        lock (stockLock)
        {
            return stock[product];
        }
    }

    // Ia toate produsele unei comenzi, sau niciunul.
    // Intoarce null daca a reusit, sau motivul refuzului.
    public static string? TakeProducts(Dictionary<string, int> items)
    {
        lock (stockLock)
        {
            // Intai verificam tot...
            foreach (var item in items)
            {
                if (stock[item.Key] < item.Value)
                {
                    return "not enough " + item.Key + ", only " + stock[item.Key] + " left";
                }
            }
            // ...apoi luam tot. Intre timp nimeni nu poate schimba stocul.
            foreach (var item in items)
            {
                stock[item.Key] -= item.Value;
            }
            SaveStock();
            return null;
        }
    }

    // Folosit la o livrare, si cand o comanda anulata isi pune produsele inapoi.
    public static void AddProducts(Dictionary<string, int> items)
    {
        lock (stockLock)
        {
            foreach (var item in items)
            {
                stock[item.Key] += item.Value;
            }
            SaveStock();
        }
    }

    // Citeste "<produs> <cantitate> <produs> <cantitate> ..." din parts[1], parts[2], ...
    // (parts[0] e comanda: ORDER sau DELIVER). Acelasi produs de doua ori se aduna.
    // Intoarce null si un mesaj de eroare daca ceva e gresit.
    public static Dictionary<string, int>? ParseItems(string[] parts, out string error)
    {
        error = "";
        if (parts.Length < 3 || parts.Length % 2 == 0)
        {
            error = "usage: " + parts[0].ToUpperInvariant() + " <product> <quantity> [<product> <quantity> ...]";
            return null;
        }

        Dictionary<string, int> items = new Dictionary<string, int>();
        for (int i = 1; i < parts.Length; i += 2)
        {
            string product = parts[i].ToLowerInvariant();
            if (!IsProduct(product))
            {
                error = "unknown product '" + parts[i] + "'";
                return null;
            }
            // Limita de sus tine numerele mici, ca adunarea lor sa nu poata depasi int.
            // Fara ea, "tea 2000000000 tea 2000000000" ar da un numar negativ.
            if (!int.TryParse(parts[i + 1], out int quantity) || quantity <= 0 || quantity > MaxQuantity)
            {
                error = "quantity '" + parts[i + 1] + "' must be a number from 1 to " + MaxQuantity;
                return null;
            }
            if (items.ContainsKey(product))
            {
                items[product] += quantity;
            }
            else
            {
                items[product] = quantity;
            }
        }
        return items;
    }

    // ---- functii ajutatoare, apelate mereu cu stockLock deja luat ----

    static string StockText()
    {
        List<string> parts = new List<string>();
        foreach (string product in Products)
        {
            parts.Add(product + "=" + stock[product]);
        }
        return string.Join(" ", parts);
    }

    static void SaveStock()
    {
        try
        {
            File.WriteAllText(StockFile, StockText());
        }
        catch (IOException e)
        {
            Log.Write("server", "cannot save the stock: " + e.Message);
        }
    }

    // Fisierul are o singura linie, de forma "coffee=17 tea=30 croissant=13".
    static bool ReadStockFile()
    {
        if (!File.Exists(StockFile))
        {
            return false;
        }
        Dictionary<string, int> loaded = new Dictionary<string, int>();
        foreach (string part in File.ReadAllText(StockFile).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = part.Split('=');
            if (pair.Length == 2 && IsProduct(pair[0]) && int.TryParse(pair[1], out int amount) && amount >= 0)
            {
                loaded[pair[0]] = amount;
            }
        }
        if (loaded.Count != Products.Length)
        {
            return false;   // fisierul e stricat: pornim cu stoc aleator
        }
        foreach (var item in loaded)
        {
            stock[item.Key] = item.Value;
        }
        Log.Write("server", "stock loaded from " + StockFile);
        return true;
    }
}
