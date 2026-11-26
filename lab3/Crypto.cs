using System.Security.Cryptography;
using System.Text;

namespace LanChat;

// task 4: cheie din parola (PBKDF2) + criptare AES-GCM pt mesajele private
public class Crypto
{
    // salt fix: toti trebuie sa ajunga la aceeasi cheie fara sa-si trimita nimic intre ei
    static readonly byte[] Salt = Encoding.UTF8.GetBytes("pr-lab3-lan-chat");
    const int Iterations = 200_000;   // lent intentionat, ca sa fie greu de ghicit parola prin incercari
    const int KeySize = 32;           // AES-256
    const int NonceSize = 12;
    const int TagSize = 16;

    readonly byte[] key;

    public Crypto(string passphrase)
    {
        key = Rfc2898DeriveBytes.Pbkdf2(passphrase, Salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }

    // intoarce nonce-ul si (ciphertext + tag), ambele in base64 ca sa intre in json
    public (string nonce, string data) Encrypt(string text, string author)
    {
        // nonce random, nou la fiecare mesaj. acelasi nonce de 2 ori cu aceeasi cheie = gcm e spart
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] plain = Encoding.UTF8.GetBytes(text);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];

        using AesGcm aes = new AesGcm(key, TagSize);
        // autorul intra ca "associated data": nu e criptat, dar daca il schimba cineva, tag-ul nu mai bate
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(author));

        return (Convert.ToBase64String(nonce), Convert.ToBase64String(cipher.Concat(tag).ToArray()));
    }

    // null = alta parola sau mesajul a fost modificat pe drum
    public string? Decrypt(string nonce, string data, string author)
    {
        try
        {
            byte[] nonceBytes = Convert.FromBase64String(nonce);
            byte[] all = Convert.FromBase64String(data);
            if (nonceBytes.Length != NonceSize || all.Length < TagSize) return null;

            byte[] cipher = all[..^TagSize];
            byte[] tag = all[^TagSize..];
            byte[] plain = new byte[cipher.Length];

            using AesGcm aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonceBytes, cipher, tag, plain, Encoding.UTF8.GetBytes(author));
            return Encoding.UTF8.GetString(plain);
        }
        catch (FormatException)
        {
            return null;   // nu era base64
        }
        catch (CryptographicException)
        {
            return null;   // tag-ul nu bate
        }
    }
}
