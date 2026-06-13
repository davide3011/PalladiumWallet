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

    // ---- cifratura AES-GCM ----

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
        // Un .lock rimasto da un crash precedente (file esiste ma nessuno lo tiene)
        // non deve bloccare l'apertura del wallet.
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
