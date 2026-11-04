using System.Net.Sockets;
using System.Text;

namespace CoffeeShop;

// The protocol is text: every message is one line of UTF-8, ended by '\n'.
// TCP is a stream of bytes, so one Send is not one Receive: a message can arrive
// in several pieces, or several messages in one piece. LineReader keeps the bytes
// it has received until it sees '\n', and only then gives out a whole line.
public class LineReader
{
    public const int MaxLineBytes = 256;

    readonly Socket socket;
    readonly byte[] buffer = new byte[1024];
    readonly List<byte> current = new();      // bytes of the line that is not complete yet
    readonly Queue<string?> lines = new();     // complete lines; null = a line that was too long
    bool tooLong;

    public LineReader(Socket socket) => this.socket = socket;

    // Returns false when the other side closed the connection (Receive returned 0).
    // line is null when the received line was longer than MaxLineBytes.
    public bool TryReadLine(out string? line)
    {
        while (lines.Count == 0)
        {
            int n = socket.Receive(buffer);
            if (n == 0)
            {
                line = null;
                return false;
            }
            for (int i = 0; i < n; i++)
                Add(buffer[i]);
        }
        line = lines.Dequeue();
        return true;
    }

    void Add(byte b)
    {
        if (b == (byte)'\n')
        {
            lines.Enqueue(tooLong ? null : Encoding.UTF8.GetString(current.ToArray()).TrimEnd('\r'));
            current.Clear();
            tooLong = false;
        }
        else if (current.Count >= MaxLineBytes)
            tooLong = true;                    // drop the rest of the line, up to its '\n'
        else
            current.Add(b);
    }
}

public static class LineWriter
{
    // Send may send only part of the bytes, so it is called until all of them are sent.
    public static void SendLine(Socket socket, string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        int sent = 0;
        while (sent < bytes.Length)
            sent += socket.Send(bytes, sent, bytes.Length - sent, SocketFlags.None);
    }
}
