using System;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Storage;
using PalladiumWallet.Core.Wallet;

namespace PalladiumWallet.Tests.Wallet;

public class WalletLoaderTests
{
    private const string ValidMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private const string ValidMnemonic24 =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon " +
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon art";

    // ---- NewFromMnemonic ----

    [Fact]
    public void NewFromMnemonic_crea_documento_con_rete_e_tipo_corretti()
    {
        var profile = ChainProfiles.Mainnet;
        var (doc, account) = WalletLoader.NewFromMnemonic(
            ValidMnemonic, passphrase: null, ScriptKind.NativeSegwit, profile);

        Assert.Equal("mainnet", doc.Network);
        Assert.Equal("NativeSegwit", doc.ScriptKind);
        Assert.Equal(ValidMnemonic, doc.Mnemonic);
        Assert.Null(doc.Passphrase);
        Assert.NotEmpty(doc.AccountXpub);
        Assert.NotEmpty(doc.MasterFingerprint);
        Assert.False(doc.IsWatchOnly);
    }

    [Fact]
    public void NewFromMnemonic_stessa_mnemonica_produce_stesso_xpub()
    {
        var profile = ChainProfiles.Mainnet;
        var (doc1, _) = WalletLoader.NewFromMnemonic(ValidMnemonic, null, ScriptKind.NativeSegwit, profile);
        var (doc2, _) = WalletLoader.NewFromMnemonic(ValidMnemonic, null, ScriptKind.NativeSegwit, profile);

        Assert.Equal(doc1.AccountXpub, doc2.AccountXpub);
    }

    [Fact]
    public void NewFromMnemonic_passphrase_diversa_produce_xpub_diverso()
    {
        var profile = ChainProfiles.Mainnet;
        var (doc1, _) = WalletLoader.NewFromMnemonic(ValidMnemonic, null,       ScriptKind.NativeSegwit, profile);
        var (doc2, _) = WalletLoader.NewFromMnemonic(ValidMnemonic, "passphrase", ScriptKind.NativeSegwit, profile);

        Assert.NotEqual(doc1.AccountXpub, doc2.AccountXpub);
    }

    [Fact]
    public void NewFromMnemonic_mnemonica_invalida_lancia_eccezione()
    {
        Assert.Throws<InvalidDataException>(() =>
            WalletLoader.NewFromMnemonic("parole non valide foo bar", null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet));
    }

    [Fact]
    public void NewFromMnemonic_mnemonica_24_parole_funziona()
    {
        var (doc, _) = WalletLoader.NewFromMnemonic(
            ValidMnemonic24, null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        Assert.Equal("mainnet", doc.Network);
        Assert.NotEmpty(doc.AccountXpub);
    }

    [Fact]
    public void NewFromMnemonic_tipi_script_producono_xpub_diversi()
    {
        var profile = ChainProfiles.Mainnet;
        var (docNative, _) = WalletLoader.NewFromMnemonic(ValidMnemonic, null, ScriptKind.NativeSegwit, profile);
        var (docWrapped, _) = WalletLoader.NewFromMnemonic(ValidMnemonic, null, ScriptKind.WrappedSegwit, profile);
        var (docLegacy, _) = WalletLoader.NewFromMnemonic(ValidMnemonic, null, ScriptKind.Legacy, profile);

        Assert.NotEqual(docNative.AccountXpub, docWrapped.AccountXpub);
        Assert.NotEqual(docNative.AccountXpub, docLegacy.AccountXpub);
    }

    [Fact]
    public void NewFromMnemonic_reti_diverse_producono_xpub_diverse()
    {
        var (docMain, _) = WalletLoader.NewFromMnemonic(ValidMnemonic, null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        var (docTest, _) = WalletLoader.NewFromMnemonic(ValidMnemonic, null, ScriptKind.NativeSegwit, ChainProfiles.Testnet);

        Assert.NotEqual(docMain.AccountXpub, docTest.AccountXpub);
    }

    // ---- ToAccount ----

    [Fact]
    public void ToAccount_da_mnemonica_deriva_gli_stessi_indirizzi_ad_ogni_caricamento()
    {
        var (doc, _) = WalletLoader.NewFromMnemonic(
            ValidMnemonic, null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        var account1 = WalletLoader.ToAccount(doc);
        var account2 = WalletLoader.ToAccount(doc);

        Assert.Equal(
            account1.GetReceiveAddress(0).ToString(),
            account2.GetReceiveAddress(0).ToString());
    }

    [Fact]
    public void ToAccount_da_mnemonica_non_e_watch_only()
    {
        var (doc, _) = WalletLoader.NewFromMnemonic(
            ValidMnemonic, null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        var account = WalletLoader.ToAccount(doc);
        Assert.False(account.IsWatchOnly);
    }

    [Fact]
    public void ToAccount_da_xpub_e_watch_only_e_produce_stessi_indirizzi()
    {
        var (doc, accountSeed) = WalletLoader.NewFromMnemonic(
            ValidMnemonic, null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);

        // Crea documento watch-only rimuovendo la mnemonica
        var docWo = new WalletDocument
        {
            Network = doc.Network,
            ScriptKind = doc.ScriptKind,
            AccountPath = doc.AccountPath,
            AccountXpub = doc.AccountXpub,
        };

        var accountWo = WalletLoader.ToAccount(docWo);

        Assert.True(accountWo.IsWatchOnly);
        Assert.Equal(
            accountSeed.GetReceiveAddress(0).ToString(),
            accountWo.GetReceiveAddress(0).ToString());
        Assert.Equal(
            accountSeed.GetReceiveAddress(9).ToString(),
            accountWo.GetReceiveAddress(9).ToString());
    }

    [Fact]
    public void ToAccount_watch_only_non_espone_chiavi_private()
    {
        var (doc, _) = WalletLoader.NewFromMnemonic(
            ValidMnemonic, null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        var docWo = new WalletDocument
        {
            Network = doc.Network,
            ScriptKind = doc.ScriptKind,
            AccountPath = doc.AccountPath,
            AccountXpub = doc.AccountXpub,
        };
        var account = WalletLoader.ToAccount(docWo);
        Assert.True(account.IsWatchOnly);
        Assert.Null(account.GetPrivateKey(false, 0));
    }

    [Fact]
    public void ToAccount_rete_sconosciuta_lancia_eccezione()
    {
        var (doc, _) = WalletLoader.NewFromMnemonic(
            ValidMnemonic, null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        var docBad = new WalletDocument
        {
            Network = "fantanet",
            ScriptKind = doc.ScriptKind,
            AccountPath = doc.AccountPath,
            AccountXpub = doc.AccountXpub,
            Mnemonic = doc.Mnemonic,
        };
        Assert.ThrowsAny<Exception>(() => WalletLoader.ToAccount(docBad));
    }

    // ---- ProfileOf ----

    [Theory]
    [InlineData("mainnet",  NetKind.Mainnet)]
    [InlineData("testnet",  NetKind.Testnet)]
    [InlineData("regtest",  NetKind.Regtest)]
    [InlineData("Mainnet",  NetKind.Mainnet)]
    public void ProfileOf_riconosce_le_reti_note(string network, NetKind expected)
    {
        var doc = new WalletDocument { Network = network, ScriptKind = "NativeSegwit",
            AccountPath = "84'/0'/0'", AccountXpub = "xpub" };
        Assert.Equal(expected, WalletLoader.ProfileOf(doc).Kind);
    }
}
