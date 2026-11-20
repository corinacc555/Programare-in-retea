using System.Collections.Concurrent;

namespace CoffeeShop;

// Producator-consumator: thread-urile clientilor pun comenzi in coada (producatorii),
// workerii iau cea mai veche comanda si o pregatesc (consumatorii).
public static class Kitchen
{
    // BlockingCollection peste un ConcurrentQueue = o coada FIFO sigura pentru thread-uri.
    // Cand coada e goala, workerul asteapta.
    static readonly BlockingCollection<Order> queue = new BlockingCollection<Order>(new ConcurrentQueue<Order>());
    static int lastId = 0;

    public static void StartWorkers(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            Thread worker = new Thread(WorkerLoop);
            worker.Name = "worker-" + i;
            worker.IsBackground = true;
            worker.Start();
        }
    }

    public static Order CreateOrder(ClientConnection client, Dictionary<string, int> items)
    {
        int id = Interlocked.Increment(ref lastId);   // doi clienti in acelasi timp primesc numere diferite
        return new Order(id, client, items);
    }

    public static void AddToQueue(Order order)
    {
        queue.Add(order);
    }

    // Apelata de thread-ul clientului cand clientul a plecat.
    // Intoarce true cand comanda a fost anulata acum, deci produsele ei trebuie puse inapoi in stoc.
    // Intoarce false cand era deja gata (sau deja anulata).
    public static bool CancelOrder(Order order)
    {
        lock (order)
        {
            if (order.State == "ready" || order.State == "canceled")
            {
                return false;
            }
            order.State = "canceled";
        }
        order.CancelSignal.Set();   // daca un worker o pregateste acum, se trezeste imediat
        return true;
    }

    // Ce face fiecare worker, la nesfarsit: ia cea mai veche comanda si o pregateste.
    static void WorkerLoop()
    {
        foreach (Order order in queue.GetConsumingEnumerable())
        {
            try
            {
                PrepareOrder(order);
            }
            catch (Exception e)
            {
                // O eroare la o comanda nu trebuie sa omoare workerul.
                Log.Write(order.Client.Endpoint, "error in " + Thread.CurrentThread.Name + ": " + e.Message);
            }
        }
    }

    static void PrepareOrder(Order order)
    {
        string worker = Thread.CurrentThread.Name ?? "worker";

        if (!ChangeState(order, "waiting", "preparing"))
        {
            return;   // a fost anulata cat astepta in coada: o sarim
        }
        Log.Write(order.Client.Endpoint, "order " + order + " started by " + worker);

        // Pregatirea e simulata prin asteptare. Wait intoarce true daca
        // comanda e anulata in timpul asteptarii, si atunci workerul se opreste imediat.
        bool canceled = order.CancelSignal.Wait(order.Seconds * 1000);
        if (canceled || !ChangeState(order, "preparing", "ready"))
        {
            // Ce s-a pregatit se pierde; produsele le-a pus inapoi cel care a anulat.
            Log.Write(order.Client.Endpoint, "order #" + order.Id + " canceled, " + worker + " stops");
            return;
        }

        if (order.Client.Send("READY " + order.Id))
        {
            Log.Write(order.Client.Endpoint, "order #" + order.Id + " ready");
        }
        else
        {
            Log.Write(order.Client.Endpoint, "order #" + order.Id + " ready, but the client is gone");
        }
    }

    // Schimba starea doar daca este inca "from". Altfel intoarce false.
    static bool ChangeState(Order order, string from, string to)
    {
        lock (order)
        {
            if (order.State != from)
            {
                return false;
            }
            order.State = to;
            return true;
        }
    }
}
