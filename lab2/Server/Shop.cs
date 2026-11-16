namespace CoffeeShop;

// Stocul magazinului. Orice citire sau schimbare a lui se face sub stockLock,
// ca "verific daca ajunge" si "scad din stoc" sa fie un singur pas,
// si ca o livrare si o comanda simultane sa nu piarda nicio modificare.
public class Shop
{
    // Cand un produs scade sub aceasta cantitate, providerul primeste alerta.
    public const int LowStock = 5;

    public static readonly Dictionary<string, int> PrepSeconds = new()
    {
        ["coffee"] = 4,
        ["tea"] = 3,
        ["croissant"] = 2,
    };

    readonly Dictionary<string, int> stock = new();
    readonly object stockLock = new();

    public Shop()
    {
        foreach (string product in PrepSeconds.Keys)
            stock[product] = Random.Shared.Next(10, 51);
    }

    public string Menu()
    {
        lock (stockLock)
            return string.Join(" ", stock.Select(p => $"{p.Key}={p.Value}"));
    }

    // Ia toate produsele sau niciunul.
    // Daca un produs nu ajunge: false, cu produsul si cate au ramas din el.
    // nowLow = produsele care tocmai au scazut sub LowStock (pentru alerta).
    public bool TryTake(Dictionary<string, int> items, out string shortProduct, out int left,
                        out Dictionary<string, int> nowLow)
    {
        shortProduct = "";
        left = 0;
        nowLow = new Dictionary<string, int>();
        lock (stockLock)
        {
            foreach (var (product, qty) in items)
            {
                if (stock[product] < qty)
                {
                    shortProduct = product;
                    left = stock[product];
                    return false;
                }
            }
            foreach (var (product, qty) in items)
            {
                bool wasEnough = stock[product] >= LowStock;
                stock[product] -= qty;
                if (wasEnough && stock[product] < LowStock)
                    nowLow[product] = stock[product];
            }
        }
        return true;
    }

    // Pune produse in stoc: livrarea providerului sau produsele unei comenzi anulate.
    public void Add(Dictionary<string, int> items)
    {
        lock (stockLock)
            foreach (var (product, qty) in items)
                stock[product] += qty;
    }

    // Produsele care sunt acum sub LowStock (trimise providerului cand se conecteaza).
    public Dictionary<string, int> LowProducts()
    {
        lock (stockLock)
            return stock.Where(p => p.Value < LowStock).ToDictionary(p => p.Key, p => p.Value);
    }
}
