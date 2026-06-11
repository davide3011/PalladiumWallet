using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;

namespace PalladiumWallet.Tests.Crypto;

/// <summary>
/// Vettore di test 1 della specifica BIP32 (seed 000102...0e0f). Gli header
/// Legacy della mainnet PLM coincidono con quelli Bitcoin, quindi il confronto
/// con le stringhe della specifica è diretto sulla rete PLM.
/// </summary>
public class Bip32Tests
{
    private static ExtKey Root => ExtKey.CreateFromSeed(
        Convert.FromHexString("000102030405060708090a0b0c0d0e0f"));

    [Fact]
    public void La_root_del_vettore_1_serializza_le_stringhe_attese()
    {
        Assert.Equal(
            "xprv9s21ZrQH143K3QTDL4LXw2F7HEK3wJUD2nW2nRk4stbPy6cq3jPPqjiChkVvvNKmPGJxWUtg6LnF5kejMRNNU3TGtRBeJgk33yuGBxrMPHi",
            Root.ToString(PalladiumNetworks.Mainnet));
        Assert.Equal(
            "xpub661MyMwAqRbcFtXgS5sYJABqqG9YLmC4Q1Rdap9gSE8NqtwybGhePY2gZ29ESFjqJoCu1Rupje8YtGqsefD265TMg7usUDFdp6W1EGMcet8",
            Root.Neuter().ToString(PalladiumNetworks.Mainnet));
    }

    [Fact]
    public void La_derivazione_hardened_m_0h_produce_le_chiavi_attese()
    {
        var derived = Root.Derive(new KeyPath("0'"));
        Assert.Equal(
            "xprv9uHRZZhk6KAJC1avXpDAp4MDc3sQKNxDiPvvkX8Br5ngLNv1TxvUxt4cV1rGL5hj6KCesnDYUhd7oWgT11eZG7XnxHrnYeSvkzY7d2bhkJ7",
            derived.ToString(PalladiumNetworks.Mainnet));
        Assert.Equal(
            "xpub68Gmy5EdvgibQVfPdqkBBCHxA5htiqg55crXYuXoQRKfDBFA1WEjWgP6LHhwBZeNK1VTsfTFUHCdrfp1bgwQ9xv5ski8PX9rL2dZXvgGDnw",
            derived.Neuter().ToString(PalladiumNetworks.Mainnet));
    }

    [Fact]
    public void I_path_hardened_non_sono_derivabili_da_una_xpub()
    {
        // Garanzia §17: da sole chiavi pubbliche niente derivazione hardened.
        Assert.ThrowsAny<InvalidOperationException>(() =>
            Root.Neuter().Derive(new KeyPath("0'")));
    }

    [Theory]
    [InlineData("m/84'/746'/0'", true)]
    [InlineData("84h/746h/0h", true)]
    [InlineData("0/5", true)]
    [InlineData("m/84'/abc", false)]
    [InlineData("", false)]
    public void TryParse_accetta_path_validi_e_rifiuta_malformati(string path, bool expected)
    {
        Assert.Equal(expected, DerivationPaths.TryParse(path, out var parsed));
        if (expected)
            Assert.NotNull(parsed);
    }

    [Fact]
    public void Gli_hardened_marker_apostrofo_e_h_sono_equivalenti()
    {
        Assert.True(DerivationPaths.TryParse("m/84'/746'/0'", out var a));
        Assert.True(DerivationPaths.TryParse("84h/746h/0h", out var b));
        Assert.Equal(a!.ToString(), b!.ToString());
    }
}
