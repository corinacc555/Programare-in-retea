namespace CoffeeShop;

// O linie de log: ora, endpoint-ul conexiunii, ce s-a intamplat.
// Console.WriteLine poate fi apelat din mai multe thread-uri deodata fara probleme.
public static class Log
{
    public static void Write(string endpoint, string message)
    {
        Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " [" + endpoint + "] " + message);
    }
}
