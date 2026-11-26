using System.Net;

namespace LanChat;

public class Peer
{
    public string Name = "";
    public string Id = "";
    public EndPoint EndPoint = new IPEndPoint(IPAddress.Any, 0);   // socket-ul lui direct, unic
    public DateTime LastSeen = DateTime.Now;    // ultima datagrama primita de la el
    public DateTime LastTyped = DateTime.Now;   // cand a scris ultima oara la tastatura (din ce ne spune el)
    public bool Inactive;                       // n-a scris nimic de mult, dar programul merge
    public bool NotResponding;                  // nu mai vin alive-uri de la el

    public Peer Copy() => (Peer)MemberwiseClone();
}

// cine e online. il folosesc thread-urile de receive, de timeout si tastatura -> lock
public class Peers
{
    readonly Dictionary<string, Peer> byName = new Dictionary<string, Peer>();
    readonly object sync = new object();

    // adauga sau reimprospateaza; true doar daca e nou
    public bool Touch(string name, string id, EndPoint endPoint)
    {
        lock (sync)
        {
            if (byName.TryGetValue(name, out Peer? peer))
            {
                // acelasi nume dar alt proces -> nu il bag, ramane cel care era primul
                if (peer.Id == id) peer.LastSeen = DateTime.Now;
                return false;
            }
            byName[name] = new Peer { Name = name, Id = id, EndPoint = endPoint };
            return true;
        }
    }

    // sterge doar daca e chiar procesul pe care il stiu (acelasi id)
    public bool Remove(string name, string id)
    {
        lock (sync)
        {
            if (!byName.TryGetValue(name, out Peer? peer) || peer.Id != id) return false;
            return byName.Remove(name);
        }
    }

    public EndPoint? FindEndPoint(string name)
    {
        lock (sync)
        {
            return byName.TryGetValue(name, out Peer? peer) ? peer.EndPoint : null;
        }
    }

    public List<Peer> All()
    {
        lock (sync)
        {
            // copii, ca afisarea sa nu citeasca campuri pe care alt thread le schimba
            return byName.Values.OrderBy(p => p.Name).Select(p => p.Copy()).ToList();
        }
    }

    // a venit ceva de la el. true daca nu mai raspundea si acum raspunde iar
    public bool Heard(string name, string id)
    {
        lock (sync)
        {
            if (!byName.TryGetValue(name, out Peer? peer) || peer.Id != id) return false;
            peer.LastSeen = DateTime.Now;
            if (!peer.NotResponding) return false;
            peer.NotResponding = false;
            return true;
        }
    }

    // el zice de cate secunde n-a scris. intoarce true = a devenit inactiv, false = a redevenit activ,
    // null = nicio schimbare
    public bool? SetIdle(string name, string id, TimeSpan idle, TimeSpan limit)
    {
        lock (sync)
        {
            if (!byName.TryGetValue(name, out Peer? peer) || peer.Id != id) return null;
            peer.LastTyped = DateTime.Now - idle;
            bool inactive = idle >= limit;
            if (inactive == peer.Inactive) return null;
            peer.Inactive = inactive;
            return inactive;
        }
    }

    // cei tacuti de peste limit, marcati acum (doar o data) ca "not responding"
    public List<string> MarkNotResponding(TimeSpan limit)
    {
        lock (sync)
        {
            List<Peer> silent = byName.Values
                .Where(p => !p.NotResponding && DateTime.Now - p.LastSeen > limit)
                .ToList();
            foreach (Peer peer in silent) peer.NotResponding = true;
            return silent.Select(p => p.Name).ToList();
        }
    }

    // cei de la care n-a venit nimic de prea mult timp: ii scot si intorc numele lor
    public List<string> RemoveSilentFor(TimeSpan limit)
    {
        lock (sync)
        {
            List<string> gone = byName.Values
                .Where(p => DateTime.Now - p.LastSeen > limit)
                .Select(p => p.Name)
                .ToList();
            foreach (string name in gone) byName.Remove(name);
            return gone;
        }
    }
}
