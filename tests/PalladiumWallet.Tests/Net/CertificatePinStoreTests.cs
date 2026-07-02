using System.Security.Cryptography.X509Certificates;
using PalladiumWallet.Core.Net;

namespace PalladiumWallet.Tests.Net;

/// <summary>
/// Tests for TLS trust-on-first-use pinning (§9): first contact pins, matching
/// certificate passes, changed certificate is rejected until an explicit reset.
/// </summary>
public class CertificatePinStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"plm-pins-{Guid.NewGuid()}", "server-certs.json");

    private static X509Certificate2 Cert(string cn) =>
        FakeElectrumServer.CreateSelfSignedCertificate(cn);

    private static void Cleanup(string path)
    {
        if (Directory.Exists(Path.GetDirectoryName(path)))
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void Il_primo_contatto_salva_il_pin_e_lo_stesso_certificato_ripassa()
    {
        var path = TempPath();
        try
        {
            using var cert = Cert("server");
            var store = new CertificatePinStore(path);

            Assert.True(store.VerifyOrPin("host", 50002, cert)); // first use: pinned
            Assert.True(File.Exists(path));
            Assert.True(store.VerifyOrPin("host", 50002, cert)); // same cert: ok

            // A fresh instance reads the same file (persistence).
            Assert.True(new CertificatePinStore(path).VerifyOrPin("host", 50002, cert));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Un_certificato_cambiato_viene_rifiutato()
    {
        var path = TempPath();
        try
        {
            using var original = Cert("originale");
            using var changed = Cert("cambiato");
            var store = new CertificatePinStore(path);

            Assert.True(store.VerifyOrPin("host", 50002, original));
            Assert.False(store.VerifyOrPin("host", 50002, changed));
            // The rejection must not overwrite the pin.
            Assert.True(store.VerifyOrPin("host", 50002, original));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Host_e_porte_diverse_hanno_pin_indipendenti()
    {
        var path = TempPath();
        try
        {
            using var cert1 = Cert("uno");
            using var cert2 = Cert("due");
            var store = new CertificatePinStore(path);

            Assert.True(store.VerifyOrPin("host-a", 50002, cert1));
            Assert.True(store.VerifyOrPin("host-b", 50002, cert2)); // different host
            Assert.True(store.VerifyOrPin("host-a", 50012, cert2)); // different port
            Assert.False(store.VerifyOrPin("host-a", 50002, cert2));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Il_reset_di_un_server_permette_di_ripinnare_il_nuovo_certificato()
    {
        var path = TempPath();
        try
        {
            using var original = Cert("originale");
            using var renewed = Cert("rinnovato");
            var store = new CertificatePinStore(path);

            Assert.True(store.VerifyOrPin("host", 50002, original));
            Assert.False(store.VerifyOrPin("host", 50002, renewed));

            Assert.True(store.Reset("host", 50002));
            Assert.True(store.VerifyOrPin("host", 50002, renewed));  // re-pinned
            Assert.False(store.VerifyOrPin("host", 50002, original)); // old one now rejected
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Il_reset_di_un_server_mai_visto_restituisce_false()
    {
        var path = TempPath();
        try
        {
            Assert.False(new CertificatePinStore(path).Reset("mai-visto", 50002));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void ResetAll_cancella_il_file_e_tutti_i_pin()
    {
        var path = TempPath();
        try
        {
            using var cert1 = Cert("uno");
            using var cert2 = Cert("due");
            var store = new CertificatePinStore(path);
            store.VerifyOrPin("host-a", 50002, cert1);
            store.VerifyOrPin("host-b", 50002, cert1);

            store.ResetAll();

            Assert.False(File.Exists(path));
            Assert.True(store.VerifyOrPin("host-a", 50002, cert2)); // TOFU restarts
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Un_file_pin_corrotto_non_blocca_le_connessioni()
    {
        var path = TempPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ non-json ");

            using var cert = Cert("server");
            var store = new CertificatePinStore(path);
            Assert.True(store.VerifyOrPin("host", 50002, cert)); // falls back to first contact
            Assert.True(store.VerifyOrPin("host", 50002, cert)); // and re-persists correctly
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void La_fingerprint_e_sha256_del_certificato_in_hex_minuscolo()
    {
        using var cert = Cert("server");
        var fingerprint = CertificatePinStore.Fingerprint(cert);

        Assert.Equal(64, fingerprint.Length);
        Assert.Equal(fingerprint.ToLowerInvariant(), fingerprint);
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cert.RawData))
                .ToLowerInvariant(),
            fingerprint);
    }
}
