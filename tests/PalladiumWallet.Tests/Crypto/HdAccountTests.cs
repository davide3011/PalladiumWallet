using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;

namespace PalladiumWallet.Tests.Crypto;

public class HdAccountTests
{
    private static Mnemonic AbandonAbout()
    {
        Assert.True(Bip39.TryParse(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            out var mnemonic));
        return mnemonic!;
    }

    [Fact]
    public void Il_watch_only_da_xpub_deriva_gli_stessi_indirizzi_del_seed()
    {
        var full = HdAccount.FromMnemonic(AbandonAbout(), null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);

        Assert.True(Slip132.TryDecodePublic(full.ToSlip132(), ChainProfiles.Mainnet, out var xpub, out var kind));
        var watchOnly = HdAccount.FromAccountXpub(xpub!, kind, ChainProfiles.Mainnet);

        Assert.True(watchOnly.IsWatchOnly);
        Assert.False(full.IsWatchOnly);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(full.GetReceiveAddress(i).ToString(), watchOnly.GetReceiveAddress(i).ToString());
            Assert.Equal(full.GetChangeAddress(i).ToString(), watchOnly.GetChangeAddress(i).ToString());
        }
    }

    [Fact]
    public void Il_watch_only_non_espone_chiavi_private()
    {
        var full = HdAccount.FromMnemonic(AbandonAbout(), null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        Assert.True(Slip132.TryDecodePublic(full.ToSlip132(), ChainProfiles.Mainnet, out var xpub, out _));
        var watchOnly = HdAccount.FromAccountXpub(xpub!, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);

        Assert.Throws<InvalidOperationException>(() => watchOnly.GetExtPrivateKey(false, 0));
        Assert.Throws<InvalidOperationException>(() => watchOnly.ToSlip132Private());
    }

    [Fact]
    public void La_master_fingerprint_di_abandon_about_e_quella_nota()
    {
        // 73c5da0a: fingerprint usata nei vettori PSBT di mezzo ecosistema.
        var account = HdAccount.FromMnemonic(AbandonAbout(), null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        Assert.Equal("73c5da0a", Convert.ToHexString(account.MasterFingerprint.ToBytes()).ToLowerInvariant());
    }

    [Fact]
    public void La_chiave_privata_derivata_corrisponde_all_indirizzo()
    {
        var account = HdAccount.FromMnemonic(AbandonAbout(), null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        var priv = account.GetExtPrivateKey(isChange: false, 0);

        Assert.Equal(account.GetPublicKey(false, 0), priv.PrivateKey.PubKey);
    }

    [Fact]
    public void La_passphrase_cambia_completamente_l_account()
    {
        var without = HdAccount.FromMnemonic(AbandonAbout(), null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        var with = HdAccount.FromMnemonic(AbandonAbout(), "extension", ScriptKind.NativeSegwit, ChainProfiles.Mainnet);

        Assert.NotEqual(without.GetReceiveAddress(0).ToString(), with.GetReceiveAddress(0).ToString());
        Assert.NotEqual(without.MasterFingerprint, with.MasterFingerprint);
    }

    [Theory]
    [InlineData(ScriptKind.WrappedSegwitMultisig)]
    [InlineData(ScriptKind.NativeSegwitMultisig)]
    public void I_tipi_multisig_non_sono_ancora_supportati(ScriptKind kind)
    {
        Assert.Throws<NotSupportedException>(() =>
            HdAccount.FromMnemonic(AbandonAbout(), null, kind, ChainProfiles.Mainnet));
        Assert.Throws<NotSupportedException>(() => DerivationPaths.ScriptPubKeyTypeFor(kind));
    }

    [Fact]
    public void L_account_di_default_usa_il_path_standard_del_profilo()
    {
        var account = HdAccount.FromMnemonic(AbandonAbout(), null, ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        Assert.Equal("84'/746'/0'", account.AccountPath.ToString());
    }
}
