using System.Collections.Concurrent;

namespace CoffeeShop;

// Producator-consumator: thread-urile clientilor (producatorii) pun comenzi in coada,
// workerii (consumatorii) iau cea mai veche comanda si o pregatesc.
public class Kitchen
{
    readonly BlockingCollection<Order> queue = new(new ConcurrentQueue<Order>());   // FIFO
    int lastId;

    public Kitchen(int workers)
    {
        for (int i = 1; i <= workers; i++)
            new Thread(Work) { IsBackground = true, Name = $"worker-{i}" }.Start();
    }

    // Interlocked: doi clienti care comanda simultan primesc numere diferite.
    public Order NewOrder(ClientConnection client, Dictionary<string, int> items) =>
        new(Interlocked.Increment(ref lastId), client, items);

    public void Enqueue(Order order) => queue.Add(order);

    // Bucla unui worker: asteapta o comanda, o pregateste, apoi ia urmatoarea.
    void Work()
    {
        foreach (Order order in queue.GetConsumingEnumerable())
        {
            try
            {
                Prepare(order);
            }
            catch (Exception e)
            {
                // O eroare la o comanda nu trebuie sa omoare workerul.
                Log.Write(order.Client.Endpoint, $"error in {Thread.CurrentThread.Name}: {e.Message}");
            }
        }
    }

    void Prepare(Order order)
    {
        if (!order.TryStart())
            return;    // anulata cat astepta in coada; produsele sunt deja inapoi in stoc

        Log.Write(order.Client.Endpoint, $"order {order} started by {Thread.CurrentThread.Name}");
        if (!order.WaitPreparation() || !order.TryFinish())
        {
            // Clientul a plecat: ce s-a pregatit se pierde, produsele le-a pus inapoi cine a anulat.
            Log.Write(order.Client.Endpoint, $"order #{order.Id} stopped by {Thread.CurrentThread.Name}, the client is gone");
            return;
        }
        order.Client.Send($"READY {order.Id}");
        Log.Write(order.Client.Endpoint, $"order #{order.Id} ready");
    }
}
