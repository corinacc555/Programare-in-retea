using System.Text.RegularExpressions;

namespace LanChat;

// dotnet run -- <name> <port> <passphrase>
public static class Program
{
    public static int Main(string[] args)
    {
        if (!ReadArguments(args, out string name, out int port, out string passphrase))
        {
            Console.WriteLine("Usage: dotnet run -- <name> <port> <passphrase>");
            Console.WriteLine("  name: 1-20 letters, digits, _ or -   port: 1024-65535");
            return 1;
        }
        return new Chat(name, port, passphrase).Run();
    }

    static bool ReadArguments(string[] args, out string name, out int port, out string passphrase)
    {
        name = args.Length > 0 ? args[0] : "";
        port = 0;
        passphrase = args.Length > 2 ? args[2] : "";
        return args.Length == 3
            && IsValidName(name)
            && int.TryParse(args[1], out port) && port >= 1024 && port <= 65535
            && passphrase.Length > 0;
    }

    // numele ajunge si in numele fisierului de log, deci doar caractere sigure
    static bool IsValidName(string name) => Regex.IsMatch(name, "^[A-Za-z0-9_-]{1,20}$");
}
