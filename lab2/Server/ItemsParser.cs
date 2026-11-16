namespace CoffeeShop;

// Transforma textul "<produs> <cantitate> <produs> <cantitate> ..." in dictionar si invers.
// Folosit si de ORDER, si de DELIVER.
public static class ItemsParser
{
    // Limita impiedica depasirea lui int: fara ea, 2147483647 + 1 devine un numar negativ
    // si o comanda ar putea adauga produse in stoc in loc sa le scada.
    public const int MaxQuantity = 1000;

    // parts[0] e comanda (ORDER sau DELIVER); produsele incep de la parts[1].
    public static bool TryParse(string[] parts, out Dictionary<string, int> items, out string error)
    {
        items = new Dictionary<string, int>();
        error = "";
        if (parts.Length < 3 || parts.Length % 2 == 0)
        {
            error = $"usage: {parts[0].ToUpperInvariant()} <product> <quantity> [<product> <quantity> ...]";
            return false;
        }
        for (int i = 1; i < parts.Length; i += 2)
        {
            string product = parts[i].ToLowerInvariant();
            if (!Shop.PrepSeconds.ContainsKey(product))
            {
                error = $"unknown product '{parts[i]}'";
                return false;
            }
            if (!int.TryParse(parts[i + 1], out int qty) || qty <= 0 || qty > MaxQuantity)
            {
                error = $"quantity '{parts[i + 1]}' is not a number from 1 to {MaxQuantity}";
                return false;
            }
            items[product] = items.GetValueOrDefault(product) + qty;
        }
        return true;
    }

    // Ex: "2 croissant, 1 tea" (pentru log).
    public static string ToText(Dictionary<string, int> items) =>
        string.Join(", ", items.Select(p => $"{p.Value} {p.Key}"));
}
