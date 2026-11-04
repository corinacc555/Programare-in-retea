namespace CoffeeShop;

// The stock of the shop. Every read or change of it happens under stockLock,
// so that checking "is there enough?" and taking the products is one step.
public class Shop
{
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

    // Takes all the items, or none of them. When one product is short,
    // returns false with that product and how many of it are left.
    public bool TryTake(Dictionary<string, int> items, out string shortProduct, out int left)
    {
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
                stock[product] -= qty;
        }
        shortProduct = "";
        left = 0;
        return true;
    }

    // Parses "<product> <qty> <product> <qty> ..." from parts[1..].
    public static bool TryParseItems(string[] parts, out Dictionary<string, int> items, out string error)
    {
        items = new Dictionary<string, int>();
        error = "";
        if (parts.Length < 3 || parts.Length % 2 == 0)
        {
            error = "usage: ORDER <product> <quantity> [<product> <quantity> ...]";
            return false;
        }
        for (int i = 1; i < parts.Length; i += 2)
        {
            string product = parts[i].ToLowerInvariant();
            if (!PrepSeconds.ContainsKey(product))
            {
                error = $"unknown product '{parts[i]}'";
                return false;
            }
            if (!int.TryParse(parts[i + 1], out int qty) || qty <= 0)
            {
                error = $"quantity '{parts[i + 1]}' is not a positive number";
                return false;
            }
            items[product] = items.GetValueOrDefault(product) + qty;
        }
        return true;
    }
}
