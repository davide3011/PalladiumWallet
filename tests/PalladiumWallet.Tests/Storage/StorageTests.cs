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
        // Corrompe un byte del ciphertext mantenendo base64 e JSON validi.
        var node = System.Text.Json.Nodes.JsonNode.Parse(cipher)!;
        var data = Convert.FromBase64String(node["Data"]!.GetValue<string>());
        data[0] ^= 0xff;
        node["Data"] = Convert.ToBase64String(data);
        Assert.Throws<WrongPasswordException>(() => EncryptedFile.Decrypt(node.ToJsonString(), "pass"));
    }

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
        var doc = SampleDoc();
        var json = doc.ToJson().Replace("\"Version\": 1", "\"Version\": 99");
        Assert.Throws<InvalidDataException>(() => WalletDocument.FromJson(json));
    }

    [Fact]
    public void Il_wallet_store_salva_e_riapre_con_e_senza_password()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plm-test-{Guid.NewGuid()}.wallet.json");
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
    public void Il_documento_senza_mnemonica_e_watch_only()
    {
        var doc = SampleDoc();
        doc.Mnemonic = null;
        Assert.True(WalletDocument.FromJson(doc.ToJson()).IsWatchOnly);
    }
}
