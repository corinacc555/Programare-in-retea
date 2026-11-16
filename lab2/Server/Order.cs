namespace CoffeeShop;

public enum OrderState { Waiting, Preparing, Done, Cancelled }

// O comanda si starea ei. Starea se schimba doar sub stateLock, ca workerul
// si thread-ul clientului sa nu se contrazica (ex: "gata" si "anulata" in acelasi timp).
// Asa, produsele unei comenzi anulate se pun inapoi in stoc o singura data.
public class Order
{
    readonly object stateLock = new();
    // Semnal pentru worker: "opreste pregatirea, clientul a plecat".
    readonly ManualResetEventSlim cancelSignal = new();
    OrderState state = OrderState.Waiting;

    public int Id { get; }
    public ClientConnection Client { get; }
    public Dictionary<string, int> Items { get; }
    public int Seconds { get; }

    public Order(int id, ClientConnection client, Dictionary<string, int> items)
    {
        Id = id;
        Client = client;
        Items = items;
        Seconds = items.Sum(p => Shop.PrepSeconds[p.Key] * p.Value);
    }

    // Workerul incepe comanda. False daca a fost anulata cat astepta in coada.
    public bool TryStart() => TryMove(OrderState.Waiting, OrderState.Preparing);

    // Workerul termina comanda. False daca a fost anulata in timpul pregatirii.
    public bool TryFinish() => TryMove(OrderState.Preparing, OrderState.Done);

    // Pregatirea e simulata prin asteptare. Wait se trezeste mai devreme
    // daca vine semnalul de anulare. Intoarce false daca a fost anulata.
    public bool WaitPreparation() => !cancelSignal.Wait(Seconds * 1000);

    // Anuleaza comanda daca nu e gata inca.
    // True = cel care a anulat trebuie sa puna produsele inapoi in stoc.
    public bool TryCancel()
    {
        lock (stateLock)
        {
            if (state is OrderState.Done or OrderState.Cancelled)
                return false;
            state = OrderState.Cancelled;
        }
        cancelSignal.Set();    // trezeste workerul, daca o pregateste acum
        return true;
    }

    bool TryMove(OrderState from, OrderState to)
    {
        lock (stateLock)
        {
            if (state != from)
                return false;
            state = to;
            return true;
        }
    }

    public override string ToString() => $"#{Id} ({ItemsParser.ToText(Items)}, {Seconds}s)";
}
