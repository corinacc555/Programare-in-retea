using System.Net.Sockets;
using System.Text;

namespace CoffeeShop;

// Protocolul e text: fiecare mesaj e o linie UTF-8, terminata cu '\n'.
// TCP e un flux de octeti, deci un Send nu inseamna un Receive: un mesaj poate veni
// in mai multe bucati, sau mai multe mesaje intr-o singura bucata. LineReader pastreaza
// octetii primiti pana vede '\n' si abia atunci da mai departe o linie intreaga.
public class LineReader
{
    public const int MaxLineBytes = 256;

    readonly Socket socket;
    readonly byte[] buffer = new byte[1024];
    readonly List<byte> current = new();      // octetii liniei care nu e inca completa
    readonly Queue<string?> lines = new();     // linii complete; null = linie prea lunga
    bool tooLong;

    public LineReader(Socket socket) => this.socket = socket;

    // Intoarce false cand cealalta parte a inchis conexiunea (Receive a intors 0).
    // line e null cand linia primita a fost mai lunga de MaxLineBytes.
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
            tooLong = true;                    // aruncam restul liniei, pana la '\n'
        else
            current.Add(b);
    }
}

public static class LineWriter
{
    // Send poate trimite doar o parte din octeti, deci il apelam pana se trimit toti.
    public static void SendLine(Socket socket, string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        int sent = 0;
        while (sent < bytes.Length)
            sent += socket.Send(bytes, sent, bytes.Length - sent, SocketFlags.None);
    }
}
