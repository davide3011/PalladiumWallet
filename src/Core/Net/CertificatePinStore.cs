using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PalladiumWallet.Core.Net;

/// <summary>
/// TLS "trust on first use" pinning (blueprint §9): on the first contact the
/// server certificate's SHA-256 fingerprint is saved; on subsequent ones it is
/// compared. If it changes, the connection must be rejected and the user can
/// unlock with an explicit reset (typical case: renewed self-signed server).
/// </summary>
public sealed class CertificatePinStore(string filePath)
{
    private readonly object _lock = new();

    public static string Fingerprint(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).ToLowerInvariant();

    /// <summary>
    /// TOFU check: true if the certificate is the one already seen (or if it is the
    /// first contact, in which case it is saved). False = certificate changed.
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

    /// <summary>Reset the saved certificate for a server ("reset SSL certificates", §9).</summary>
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
