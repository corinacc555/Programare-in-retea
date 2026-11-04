using System.Collections.Concurrent;

namespace CoffeeShop;

public class Order
{
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

    public override string ToString() =>
        $"#{Id} ({string.Join(", ", Items.Select(p => $"{p.Value} {p.Key}"))}, {Seconds}s)";
}

// Producer-consumer: the client threads put orders into the queue,
// the workers take the oldest one and prepare it.
public class Kitchen
{
    readonly BlockingCollection<Order> queue = new(new ConcurrentQueue<Order>());   // FIFO
    int lastId;

    public Kitchen(int workers)
    {
        for (int i = 1; i <= workers; i++)
            new Thread(Work) { IsBackground = true, Name = $"worker-{i}" }.Start();
    }

    public Order NewOrder(ClientConnection client, Dictionary<string, int> items) =>
        new(Interlocked.Increment(ref lastId), client, items);

    public void Enqueue(Order order) => queue.Add(order);

    void Work()
    {
        foreach (Order order in queue.GetConsumingEnumerable())
        {
            Log.Write(order.Client.Endpoint, $"order {order} started by {Thread.CurrentThread.Name}");
            Thread.Sleep(order.Seconds * 1000);    // the preparation is simulated by waiting
            order.Client.Send($"READY {order.Id}");
            Log.Write(order.Client.Endpoint, $"order #{order.Id} ready");
        }
    }
}
