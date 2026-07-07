using System;
using System.IO;
using System.Text.Json.Nodes;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.Tests.Storage;

public class StorageTests
{
    private static WalletDocument SampleDoc() => new()
    {
        Network = "regtest",
        ScriptKind = "NativeSegwit",
        Mnemonic = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
        AccountPath = "84'/1'/0'",
        AccountXpub = "vpub-fittizia-per-test",
        Labels = { ["txid123"] = "caffè" },
    };

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"plm-test-{Guid.NewGuid()}.wallet.json");

    // ---- AES-GCM encryption ----

    [Fact]
    public void La_cifratura_fa_roundtrip_con_la_password_giusta()
    {
        var cipher = EncryptedFile.Encrypt("contenuto segreto", "pass-forte");
        Assert.True(EncryptedFile.IsEncrypted(cipher));
        Assert.DoesNotContain("segreto", cipher);
        Assert.Equal("contenuto segreto", EncryptedFile.Decrypt(cipher, "pass-forte"));
    }

    [Fact]
    public void La_password_errata_viene_rifiutata()
    {
        var cipher = EncryptedFile.Encrypt("contenuto", "giusta");
        Assert.Throws<WrongPasswordException>(() => EncryptedFile.Decrypt(cipher, "sbagliata"));
    }

    [Fact]
    public void Un_file_manomesso_viene_rifiutato()
    {
        var cipher = EncryptedFile.Encrypt("contenuto", "pass");
        var node = JsonNode.Parse(cipher)!;
        var data = Convert.FromBase64String(node["Data"]!.GetValue<string>());
        data[0] ^= 0xff;
        node["Data"] = Convert.ToBase64String(data);
        Assert.Throws<WrongPasswordException>(() => EncryptedFile.Decrypt(node.ToJsonString(), "pass"));
    }

    [Fact]
    public void Ogni_encrypt_produce_nonce_diverso()
    {
        var c1 = JsonNode.Parse(EncryptedFile.Encrypt("x", "p"))!["Nonce"]!.GetValue<string>();
        var c2 = JsonNode.Parse(EncryptedFile.Encrypt("x", "p"))!["Nonce"]!.GetValue<string>();
        Assert.NotEqual(c1, c2);
    }

    [Theory]
    [InlineData("not json at all")]                                     // broken JSON
    [InlineData("null")]                                                // JSON null
    [InlineData("""{"Format":"plm-wallet-aesgcm-v1"}""")]               // missing fields → null strings
    [InlineData("""{"Format":"plm-wallet-aesgcm-v1","Iterations":600000,"Salt":"$$$","Nonce":"AAAAAAAAAAAAAAAA","Tag":"AAAAAAAAAAAAAAAAAAAAAA==","Data":""}""")] // bad base64
    [InlineData("""{"Format":"plm-wallet-aesgcm-v1","Iterations":600000,"Salt":"AA==","Nonce":"AA==","Tag":"AAAAAAAAAAAAAAAAAAAAAA==","Data":""}""")] // wrong nonce size
    [InlineData("""{"Format":"plm-wallet-aesgcm-v1","Iterations":0,"Salt":"AA==","Nonce":"AAAAAAAAAAAAAAAA","Tag":"AAAAAAAAAAAAAAAAAAAAAA==","Data":""}""")] // iterations 0
    [InlineData("""{"Format":"plm-wallet-aesgcm-v1","Iterations":2147483647,"Salt":"AA==","Nonce":"AAAAAAAAAAAAAAAA","Tag":"AAAAAAAAAAAAAAAAAAAAAA==","Data":""}""")] // PBKDF2 DoS
    public void Un_contenitore_malformato_produce_sempre_InvalidDataException(string container)
    {
        // A tampered/corrupted file must map to the two typed exceptions, never to a
        // raw JsonException/FormatException/ArgumentNullException (found by fuzzing);
        // the absurd iteration count would otherwise hang the wallet at open.
        Assert.Throws<InvalidDataException>(() => EncryptedFile.Decrypt(container, "pass"));
    }

    [Fact]
    public void Ogni_encrypt_produce_salt_diverso()
    {
        var s1 = JsonNode.Parse(EncryptedFile.Encrypt("x", "p"))!["Salt"]!.GetValue<string>();
        var s2 = JsonNode.Parse(EncryptedFile.Encrypt("x", "p"))!["Salt"]!.GetValue<string>();
        Assert.NotEqual(s1, s2);
    }

    [Fact]
    public void IsEncrypted_restituisce_false_per_json_non_cifrato()
    {
        Assert.False(EncryptedFile.IsEncrypted("{\"Version\": 1}"));
    }

    [Fact]
    public void IsEncrypted_restituisce_false_per_testo_non_json()
    {
        Assert.False(EncryptedFile.IsEncrypted("non è json"));
    }

    // Regression: valid JSON whose root is not an object (found by the
    // property test) must not throw from TryGetProperty.
    [Theory]
    [InlineData("5")]
    [InlineData("true")]
    [InlineData("\"stringa\"")]
    [InlineData("[1, 2]")]
    public void IsEncrypted_restituisce_false_per_json_con_radice_non_oggetto(string content)
    {
        Assert.False(EncryptedFile.IsEncrypted(content));
    }

    [Fact]
    public void IsEncrypted_restituisce_false_per_utf16_invalido()
    {
        // Lone surrogate: cannot be transcoded to UTF-8 for JSON parsing.
        Assert.False(EncryptedFile.IsEncrypted("\ud800"));
    }

    [Fact]
    public void IsEncrypted_restituisce_false_se_Format_non_e_una_stringa()
    {
        Assert.False(EncryptedFile.IsEncrypted("{\"Format\": 42}"));
    }

    // ---- WalletDocument JSON ----

    [Fact]
    public void Il_documento_wallet_fa_roundtrip_json()
    {
        var doc = SampleDoc();
        var restored = WalletDocument.FromJson(doc.ToJson());

        Assert.Equal(doc.Network, restored.Network);
        Assert.Equal(doc.Mnemonic, restored.Mnemonic);
        Assert.Equal(doc.AccountXpub, restored.AccountXpub);
        Assert.Equal("caffè", restored.Labels["txid123"]);
        Assert.False(restored.IsWatchOnly);
    }

    [Fact]
    public void Una_versione_futura_del_file_viene_rifiutata()
    {
        var json = SampleDoc().ToJson().Replace("\"Version\": 1", "\"Version\": 99");
        Assert.Throws<InvalidDataException>(() => WalletDocument.FromJson(json));
    }

    [Fact]
    public void Il_documento_senza_mnemonica_e_watch_only()
    {
        var doc = SampleDoc();
        doc.Mnemonic = null;
        Assert.True(WalletDocument.FromJson(doc.ToJson()).IsWatchOnly);
    }

    [Fact]
    public void Json_corrotto_lancia_eccezione()
    {
        Assert.ThrowsAny<Exception>(() => WalletDocument.FromJson("{non è json valido}"));
    }

    [Fact]
    public void Json_con_campi_mancanti_lancia_eccezione()
    {
        Assert.ThrowsAny<Exception>(() => WalletDocument.FromJson("{}"));
    }

    // ---- WalletStore ----

    [Fact]
    public void Il_wallet_store_salva_e_riapre_con_e_senza_password()
    {
        var path = TempPath();
        try
        {
            WalletStore.Save(SampleDoc(), path);
            Assert.False(WalletStore.RequiresPassword(path));
            Assert.Equal("regtest", WalletStore.Load(path).Network);

            WalletStore.Save(SampleDoc(), path, "pwd");
            Assert.True(WalletStore.RequiresPassword(path));
            Assert.Throws<WrongPasswordException>(() => WalletStore.Load(path));
            Assert.Throws<WrongPasswordException>(() => WalletStore.Load(path, "altra"));
            Assert.Equal("regtest", WalletStore.Load(path, "pwd").Network);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Scrittura_atomica_non_lascia_file_tmp()
    {
        var path = TempPath();
        try
        {
            WalletStore.Save(SampleDoc(), path);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_da_path_inesistente_lancia_eccezione()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plm-noexist-{Guid.NewGuid()}.wallet.json");
        Assert.Throws<FileNotFoundException>(() => WalletStore.Load(path));
    }

    [Fact]
    public void Exists_restituisce_false_per_path_inesistente()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plm-noexist-{Guid.NewGuid()}.wallet.json");
        Assert.False(WalletStore.Exists(path));
    }

    [Fact]
    public void Due_save_successivi_producono_nonce_diversi()
    {
        var path = TempPath();
        try
        {
            WalletStore.Save(SampleDoc(), path, "password");
            var n1 = JsonNode.Parse(File.ReadAllText(path))!["Nonce"]!.GetValue<string>();
            WalletStore.Save(SampleDoc(), path, "password");
            var n2 = JsonNode.Parse(File.ReadAllText(path))!["Nonce"]!.GetValue<string>();
            Assert.NotEqual(n1, n2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- WalletLock ----

    [Fact]
    public void WalletLock_acquisisce_e_rilascia()
    {
        var path = TempPath();
        try
        {
            using var lock1 = WalletLock.TryAcquire(path);
            Assert.NotNull(lock1);
        }
        finally
        {
            File.Delete(path + ".lock");
        }
    }

    [Fact]
    public void WalletLock_seconda_istanza_restituisce_null()
    {
        var path = TempPath();
        try
        {
            using var lock1 = WalletLock.TryAcquire(path);
            Assert.NotNull(lock1);
            Assert.Null(WalletLock.TryAcquire(path));
        }
        finally
        {
            File.Delete(path + ".lock");
        }
    }

    [Fact]
    public void WalletLock_riacquisibile_dopo_rilascio()
    {
        var path = TempPath();
        try
        {
            var lock1 = WalletLock.TryAcquire(path);
            Assert.NotNull(lock1);
            lock1!.Dispose();

            using var lock2 = WalletLock.TryAcquire(path);
            Assert.NotNull(lock2);
        }
        finally
        {
            File.Delete(path + ".lock");
        }
    }

    [Fact]
    public void WalletLock_dispose_rimuove_il_file_lock()
    {
        var path = TempPath();
        var lockPath = path + ".lock";
        var lock1 = WalletLock.TryAcquire(path);
        Assert.NotNull(lock1);
        lock1!.Dispose();
        Assert.False(File.Exists(lockPath));
    }

    [Fact]
    public void WalletLock_file_lock_preesistente_ma_non_bloccato_viene_acquisito()
    {
        // A stale .lock left from a previous crash (file exists but nobody holds it)
        // must not block wallet opening.
        var path = TempPath();
        var lockPath = path + ".lock";
        try
        {
            File.WriteAllText(lockPath, "stale");
            using var lock1 = WalletLock.TryAcquire(path);
            Assert.NotNull(lock1);
        }
        finally
        {
            File.Delete(lockPath);
        }
    }
}
