using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanChat;

public static class MessageType
{
    public const string Join = "join";        // multicast: am intrat
    public const string Here = "here";        // unicast, raspuns la join: si eu sunt aici
    public const string Taken = "taken";      // unicast, raspuns la join: numele e deja luat
    public const string Alive = "alive";      // multicast, la fiecare 5 s
    public const string Bye = "bye";          // multicast: am iesit cu /quit
    public const string General = "general";  // multicast: mesaj pt toti
    public const string Private = "private";  // unicast, criptat

    public static readonly string[] All = { Join, Here, Taken, Alive, Bye, General, Private };
}

// o datagrama = un mesaj json, ex: {"type":"general","from":"Ana","id":"3f2a9c1b","text":"salut"}
public class Message
{
    public const int MaxBytes = 4096;
    public const int MaxTextLength = 500;
    public const int MaxNameLength = 20;

    public string Type { get; set; } = "";
    public string From { get; set; } = "";
    public string Id { get; set; } = "";      // random per proces, Chat.myId
    public string? Text { get; set; }         // la private: textul criptat, in base64
    public string? Nonce { get; set; }        // doar la private
    public int? Idle { get; set; }            // de cate secunde autorul n-a mai scris nimic la tastatura

    [JsonIgnore]
    public int Size { get; private set; }     // cati bytes a avut datagrama, pt log

    static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    // null daca datagrama nu e un mesaj valid al chatului, iar error zice de ce
    public static Message? Parse(byte[] buffer, int length, out string error)
    {
        error = "too long";
        if (length > MaxBytes) return null;

        error = "not a chat message";
        Message? message = TryDeserialize(buffer, length);
        if (message == null) return null;

        error = message.FindProblem();
        if (error != "") return null;

        message.Size = length;
        return message;
    }

    static Message? TryDeserialize(byte[] buffer, int length)
    {
        try
        {
            return JsonSerializer.Deserialize<Message>(buffer.AsSpan(0, length), JsonOptions);
        }
        catch (JsonException)
        {
            return null;   // bytes random, alt program, json stricat
        }
    }

    // json corect nu inseamna mesaj corect: verific campurile de care am nevoie
    string FindProblem()
    {
        if (!MessageType.All.Contains(Type)) return "unknown type";
        if (string.IsNullOrEmpty(From) || From.Length > MaxNameLength) return "bad name";
        if (string.IsNullOrEmpty(Id)) return "no id";
        if (Type == MessageType.General && (Text == null || Text.Length > MaxTextLength)) return "bad text";
        if (Type == MessageType.Private && (Text == null || Nonce == null)) return "bad private";
        if (Idle < 0) return "bad idle";
        return "";
    }
}
