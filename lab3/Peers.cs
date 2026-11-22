using System.Net;

namespace LanChat;

public class Peer
{
    public string Name = "";
    public string Id = "";
    public EndPoint EndPoint = new IPEndPoint(IPAddress.Any, 0);   // socket-ul lui direct, unic
    public DateTime LastSeen = DateTime.Now;
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
            return byName.Values.OrderBy(p => p.Name).ToList();
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
