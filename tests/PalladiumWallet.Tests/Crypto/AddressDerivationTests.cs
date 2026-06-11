using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;

namespace PalladiumWallet.Tests.Crypto;

public class AddressDerivationTests
{
    private static byte[] AbandonAboutSeed()
    {
        Assert.True(Bip39.TryParse(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            out var mnemonic));
        return Bip39.ToSeed(mnemonic!);
    }

    [Fact]
    public void Il_vettore_bip44_produce_lo_stesso_hash_su_bitcoin_e_plm()
    {
        // Indirizzo noto di abandon-about su m/44'/0'/0'/0/0 (riferimento pubblico).
        var account = HdAccount.FromSeed(AbandonAboutSeed(), ScriptKind.Legacy,
            ChainProfiles.Mainnet, new KeyPath("44'/0'/0'"));
        var pubKey = account.GetPublicKey(isChange: false, 0);

        var bitcoinAddr = pubKey.GetAddress(ScriptPubKeyType.Legacy, Network.Main);
        Assert.Equal("1LqBGSKuX5yYUonjxT5qGfpUsXKYYWeabA", bitcoinAddr.ToString());

        var plmAddr = account.GetReceiveAddress(0);
        Assert.StartsWith("P", plmAddr.ToString());
        // Stesso hash160 sotto i due prefissi: la derivazione è identica,
        // cambia solo la veste di rete.
        Assert.Equal(pubKey.Hash, ((BitcoinPubKeyAddress)plmAddr).Hash);
    }

    [Fact]
    public void Il_vettore_bip49_produce_lo_stesso_script_hash_su_bitcoin_e_plm()
    {
        var account = HdAccount.FromSeed(AbandonAboutSeed(), ScriptKind.WrappedSegwit,
            ChainProfiles.Mainnet, new KeyPath("49'/0'/0'"));
        var pubKey = account.GetPublicKey(isChange: false, 0);

        var bitcoinAddr = pubKey.GetAddress(ScriptPubKeyType.SegwitP2SH, Network.Main);
        Assert.Equal("37VucYSaXLCAsxYyAPfbSi9eh4iEcbShgf", bitcoinAddr.ToString());

        var plmAddr = account.GetReceiveAddress(0);
        Assert.StartsWith("3", plmAddr.ToString());
        Assert.Equal(((BitcoinScriptAddress)bitcoinAddr).Hash, ((BitcoinScriptAddress)plmAddr).Hash);
    }

    [Theory]
    [InlineData(ScriptKind.Legacy, NetKind.Mainnet)]
    [InlineData(ScriptKind.WrappedSegwit, NetKind.Mainnet)]
    [InlineData(ScriptKind.NativeSegwit, NetKind.Mainnet)]
    [InlineData(ScriptKind.Legacy, NetKind.Testnet)]
    [InlineData(ScriptKind.WrappedSegwit, NetKind.Testnet)]
    [InlineData(ScriptKind.NativeSegwit, NetKind.Testnet)]
    [InlineData(ScriptKind.NativeSegwit, NetKind.Regtest)]
    public void Ogni_indirizzo_derivato_fa_roundtrip_sulla_propria_rete(ScriptKind kind, NetKind net)
    {
        var profile = ChainProfiles.For(net);
        var account = HdAccount.FromSeed(AbandonAboutSeed(), kind, profile);

        var addr = account.GetReceiveAddress(0);
        var parsed = BitcoinAddress.Create(addr.ToString(), PalladiumNetworks.For(net));

        Assert.Equal(addr.ScriptPubKey, parsed.ScriptPubKey);
    }

    [Theory]
    [InlineData(NetKind.Mainnet, "tplm1")]
    [InlineData(NetKind.Mainnet, "rplm1")]
    public void Gli_indirizzi_segwit_mainnet_non_hanno_prefissi_di_altre_reti(NetKind net, string wrongPrefix)
    {
        var account = HdAccount.FromSeed(AbandonAboutSeed(), ScriptKind.NativeSegwit, ChainProfiles.For(net));
        Assert.DoesNotContain(wrongPrefix, account.GetReceiveAddress(0).ToString());
    }

    [Fact]
    public void Il_path_di_account_usa_il_coin_type_del_profilo()
    {
        Assert.Equal("84'/746'/0'",
            DerivationPaths.AccountPath(ScriptKind.NativeSegwit, ChainProfiles.Mainnet).ToString());
        Assert.Equal("84'/1'/0'",
            DerivationPaths.AccountPath(ScriptKind.NativeSegwit, ChainProfiles.Testnet).ToString());
        Assert.Equal("44'/746'/2'",
            DerivationPaths.AccountPath(ScriptKind.Legacy, ChainProfiles.Mainnet, account: 2).ToString());
    }

    [Fact]
    public void Receiving_e_change_derivano_indirizzi_diversi()
    {
        var account = HdAccount.FromSeed(AbandonAboutSeed(), ScriptKind.NativeSegwit, ChainProfiles.Mainnet);
        Assert.NotEqual(account.GetReceiveAddress(0).ToString(), account.GetChangeAddress(0).ToString());
        Assert.NotEqual(account.GetReceiveAddress(0).ToString(), account.GetReceiveAddress(1).ToString());
    }
}
