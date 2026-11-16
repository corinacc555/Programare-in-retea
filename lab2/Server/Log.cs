namespace CoffeeShop;

// Scrie o linie de log: ora, endpoint-ul clientului si mesajul.
public static class Log
{
    public static void Write(string endpoint, string message) =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} [{endpoint}] {message}");
}
