using NBitcoin;
using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Tests.Chain;

public class PalladiumNetworksTests
{
    // Fixed private key for deterministic tests (test-only, never use it for real).
    private static Key TestKey => new(Convert.FromHexString(
        "0000000000000000000000000000000000000000000000000000000000000001"));

    [Theory]
    [InlineData(NetKind.Mainnet)]
    [InlineData(NetKind.Testnet)]
    [InlineData(NetKind.Regtest)]
    public void La_genesi_della_rete_corrisponde_al_profilo(NetKind kind)
    {
        var network = PalladiumNetworks.For(kind);
        var profile = ChainProfiles.For(kind);

        Assert.Equal(profile.GenesisHash, network.GetGenesis().GetHash().ToString());
    }

    [Fact]
    public void Indirizzo_p2pkh_mainnet_inizia_con_P()
    {
        var addr = TestKey.PubKey.GetAddress(ScriptPubKeyType.Legacy, PalladiumNetworks.Mainnet);
        Assert.StartsWith("P", addr.ToString());
    }

    [Fact]
    public void Indirizzo_native_segwit_mainnet_inizia_con_plm1()
    {
        var addr = TestKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, PalladiumNetworks.Mainnet);
        Assert.StartsWith("plm1", addr.ToString());
    }

    [Fact]
    public void Indirizzo_segwit_wrapped_mainnet_inizia_con_3()
    {
        var addr = TestKey.PubKey.GetAddress(ScriptPubKeyType.SegwitP2SH, PalladiumNetworks.Mainnet);
        Assert.StartsWith("3", addr.ToString());
    }

    [Theory]
    [InlineData(NetKind.Testnet, "tplm1")]
    [InlineData(NetKind.Regtest, "rplm1")]
    public void Indirizzi_segwit_test_e_regtest_usano_l_hrp_giusto(NetKind kind, string prefix)
    {
        var addr = TestKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, PalladiumNetworks.For(kind));
        Assert.StartsWith(prefix, addr.ToString());
    }

    [Fact]
    public void Wif_mainnet_fa_roundtrip_con_il_prefisso_del_profilo()
    {
        var wif = TestKey.GetWif(PalladiumNetworks.Mainnet);
        var decoded = Key.Parse(wif.ToString(), PalladiumNetworks.Mainnet);

        Assert.Equal(TestKey.ToHex(), decoded.ToHex());
    }

    [Fact]
    public void Chiavi_estese_mainnet_serializzano_come_xprv_xpub()
    {
        var ext = new ExtKey();
        Assert.StartsWith("xprv", ext.ToString(PalladiumNetworks.Mainnet));
        Assert.StartsWith("xpub", ext.Neuter().ToString(PalladiumNetworks.Mainnet));
    }

    [Fact]
    public void Le_tre_reti_sono_distinte_e_riutilizzate()
    {
        Assert.NotSame(PalladiumNetworks.Mainnet, PalladiumNetworks.Testnet);
        Assert.NotSame(PalladiumNetworks.Testnet, PalladiumNetworks.Regtest);
        Assert.Same(PalladiumNetworks.Mainnet, PalladiumNetworks.For(NetKind.Mainnet));
    }
}
