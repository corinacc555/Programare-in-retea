namespace CoffeeShop;

// O comanda: ce s-a comandat, de cine, cat dureaza si in ce stare e.
public class Order
{
    public int Id;
    public ClientConnection Client;
    public Dictionary<string, int> Items;
    public int Seconds;

    // "waiting" -> "preparing" -> "ready", sau "canceled" oricand inainte de "ready".
    // O pot schimba si thread-ul clientului, si un worker, deci se schimba doar in lock (this).
    public string State = "waiting";

    // Se aprinde cand comanda e anulata; trezeste workerul care o pregateste.
    public ManualResetEventSlim CancelSignal = new ManualResetEventSlim(false);

    public Order(int id, ClientConnection client, Dictionary<string, int> items)
    {
        Id = id;
        Client = client;
        Items = items;
        Seconds = 0;
        foreach (var item in items)
        {
            Seconds += Shop.PrepSeconds(item.Key) * item.Value;   // 2 croissant + 1 tea = 2*2 + 3 = 7 s
        }
    }

    public override string ToString()
    {
        List<string> parts = new List<string>();
        foreach (var item in Items)
        {
            parts.Add(item.Value + " " + item.Key);
        }
        return "#" + Id + " (" + string.Join(", ", parts) + ", " + Seconds + "s)";
    }
}
