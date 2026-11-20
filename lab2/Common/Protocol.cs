using System.Net.Sockets;
using System.Text;

namespace CoffeeShop;

// Cod folosit de ambele programe (server si client) ca sa vorbeasca prin TCP.
// Protocolul e text: fiecare mesaj e o linie UTF-8 care se termina cu '\n'.
public static class Protocol
{
    public const int MaxLineBytes = 256;

    // Trimite un mesaj si pune '\n' la sfarsit.
    // Send poate trimite doar o parte din octeti, deci il apelam pana se trimit toti.
    public static void SendLine(Socket socket, string message)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message + "\n");
        int sent = 0;
        while (sent < bytes.Length)
        {
            sent += socket.Send(bytes, sent, bytes.Length - sent, SocketFlags.None);
        }
    }
}

// TCP e un flux de octeti, deci un Send nu inseamna un Receive:
// un mesaj poate veni in doua bucati, sau doua mesaje pot veni intr-o singura bucata.
// LineReader pastreaza octetii primiti pana vede '\n' si abia atunci da linia mai departe.
public class LineReader
{
    // Intors de ReadLine pentru o linie mai lunga de MaxLineBytes.
    // O linie adevarata nu contine niciodata '\n', deci nu se poate confunda cu una.
    public const string TooLong = "\n";

    private readonly Socket socket;
    private readonly byte[] buffer = new byte[1024];
    private readonly List<byte> currentLine = new List<byte>();      // octetii liniei care nu e gata inca
    private readonly Queue<string> readyLines = new Queue<string>(); // liniile deja complete
    private bool skippingLongLine = false;                           // true cat timp aruncam restul unei linii prea lungi

    public LineReader(Socket socket)
    {
        this.socket = socket;
    }

    // Asteapta urmatoarea linie intreaga.
    // Intoarce null cand cealalta parte a inchis conexiunea (Receive a intors 0).
    // Intoarce TooLong cand linia a fost mai lunga de MaxLineBytes.
    public string? ReadLine()
    {
        while (readyLines.Count == 0)
        {
            int count = socket.Receive(buffer);
            if (count == 0)
            {
                return null;
            }
            for (int i = 0; i < count; i++)
            {
                AddByte(buffer[i]);
            }
        }
        return readyLines.Dequeue();
    }

    private void AddByte(byte b)
    {
        if (b == '\n')
        {
            // Sfarsitul unei linii: o dam mai departe, daca nu era o linie lunga pe care o aruncam.
            // Decodam UTF-8 abia acum, ca o litera din 2 octeti venita in doua bucati sa nu se strice.
            if (!skippingLongLine)
            {
                string line = Encoding.UTF8.GetString(currentLine.ToArray());
                readyLines.Enqueue(line.TrimEnd('\r'));
            }
            currentLine.Clear();
            skippingLongLine = false;
        }
        else if (skippingLongLine)
        {
            // Inca suntem in linia prea lunga: aruncam octetul.
        }
        else if (currentLine.Count == Protocol.MaxLineBytes)
        {
            // Linia e prea lunga. O anuntam imediat (nu abia cand vine '\n')
            // si aruncam restul ei.
            readyLines.Enqueue(TooLong);
            currentLine.Clear();
            skippingLongLine = true;
        }
        else
        {
            currentLine.Add(b);
        }
    }
}
