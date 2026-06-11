using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PalladiumWallet.Core.Net;

/// <summary>
/// Pinning TLS "trust on first use" (blueprint §9): al primo contatto la
/// fingerprint SHA-256 del certificato del server viene salvata; ai successivi
/// viene confrontata. Se cambia, la connessione va rifiutata e l'utente può
/// sbloccare con un reset esplicito (caso tipico: server self-signed rinnovato).
/// </summary>
public sealed class CertificatePinStore(string filePath)
{
    private readonly object _lock = new();

    public static string Fingerprint(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).ToLowerInvariant();

    /// <summary>
    /// Verifica TOFU: true se il certificato è quello già visto (o se è il primo
    /// contatto, nel qual caso viene salvato). False = certificato cambiato.
    /// </summary>
    public bool VerifyOrPin(string host, int port, X509Certificate certificate)
    {
        var key = $"{host}:{port}";
        var fingerprint = Fingerprint(certificate);
        lock (_lock)
        {
            var pins = Load();
            if (pins.TryGetValue(key, out var pinned))
                return pinned == fingerprint;
            pins[key] = fingerprint;
            Save(pins);
            return true;
        }
    }

    /// <summary>Reset del certificato salvato per un server ("reset certificati SSL", §9).</summary>
    public bool Reset(string host, int port)
    {
        lock (_lock)
        {
            var pins = Load();
            if (!pins.Remove($"{host}:{port}"))
                return false;
            Save(pins);
            return true;
        }
    }

    public void ResetAll()
    {
        lock (_lock)
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    private Dictionary<string, string> Load() =>
        File.Exists(filePath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(filePath)) ?? []
            : [];

    private void Save(Dictionary<string, string> pins)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(pins,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
