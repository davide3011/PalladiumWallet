using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PalladiumWallet.Core.Storage;

/// <summary>
/// Cifratura del file wallet (blueprint §8/§17): AES-256-GCM con chiave derivata
/// dalla password via PBKDF2-HMAC-SHA512. Il contenitore è JSON autodescrittivo
/// (kdf, parametri, salt, nonce) per consentire upgrade futuri dei parametri.
/// </summary>
public static class EncryptedFile
{
    private const string Format = "plm-wallet-aesgcm-v1";
    private const int DefaultIterations = 600_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private sealed record Container(
        string Format, int Iterations, string Salt, string Nonce, string Tag, string Data);

    public static bool IsEncrypted(string fileContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(fileContent);
            return doc.RootElement.TryGetProperty("Format", out var f)
                && f.GetString() == Format;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string Encrypt(string plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(password, salt, DefaultIterations);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        CryptographicOperations.ZeroMemory(key);

        return JsonSerializer.Serialize(new Container(
            Format, DefaultIterations,
            Convert.ToBase64String(salt), Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag), Convert.ToBase64String(cipher)));
    }

    /// <summary>Lancia <see cref="WrongPasswordException"/> se la password è errata o il file è manomesso.</summary>
    public static string Decrypt(string fileContent, string password)
    {
        var container = JsonSerializer.Deserialize<Container>(fileContent)
            ?? throw new InvalidDataException("Contenitore cifrato non valido.");
        if (container.Format != Format)
            throw new InvalidDataException($"Formato sconosciuto: {container.Format}");

        var key = DeriveKey(password, Convert.FromBase64String(container.Salt), container.Iterations);
        var cipher = Convert.FromBase64String(container.Data);
        var plain = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                Convert.FromBase64String(container.Nonce), cipher,
                Convert.FromBase64String(container.Tag), plain);
        }
        catch (AuthenticationTagMismatchException)
        {
            // Il tag GCM autentica: password errata e manomissione sono indistinguibili.
            throw new WrongPasswordException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, KeySize);
}

/// <summary>Password errata (o file wallet manomesso: il tag GCM non distingue i due casi).</summary>
public sealed class WrongPasswordException : Exception
{
    public WrongPasswordException() : base("Password errata o file wallet danneggiato.") { }
}
