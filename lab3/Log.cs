using System.Net;

namespace LanChat;

// fisierul chat-<name>.log: o linie pt fiecare datagrama trimisa, primita sau aruncata
public class Log
{
    readonly string path;
    readonly object fileLock = new object();   // scriu din mai multe thread-uri

    public Log(string name)
    {
        path = "chat-" + name + ".log";
    }

    public void Sent(EndPoint to, string kind, int size) => Write("sent", to, kind, size);

    public void Received(EndPoint from, string kind, int size) => Write("received", from, kind, size);

    public void Dropped(EndPoint from, string reason, int size) => Write("dropped", from, reason, size);

    void Write(string action, EndPoint endPoint, string kind, int size)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {action,-8} {endPoint,-21} {kind,-20} {size} bytes";
        lock (fileLock)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
