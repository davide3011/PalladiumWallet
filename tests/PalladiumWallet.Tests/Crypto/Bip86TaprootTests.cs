using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;

namespace PalladiumWallet.Tests.Crypto;

/// <summary>
/// Vettori di test BIP86 (mnemonica abandon-about, senza passphrase).
/// La chiave pubblica tweakizzata (output key, 32 byte x-only) è chain-independent:
/// viene verificata contro i vettori ufficiali Bitcoin, poi si controlla che
/// l'indirizzo PLM abbia il prefisso plm1p (witness v1, bech32m).
/// Il path m/86'/0'/0' usa coin_type=0 (non 746) per aderire ai vettori BIP86.
/// </summary>
public class Bip86TaprootTests
{
    private static HdAccount Account()
    {
        Assert.True(Bip39.TryParse(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            out var mnemonic));
        return HdAccount.FromSeed(
            Bip39.ToSeed(mnemonic!),
            ScriptKind.Taproot,
            ChainProfiles.Mainnet,
            new KeyPath("86'/0'/0'"));
    }

    [Fact]
    public void Il_derivation_path_di_un_account_taproot_usa_purpose_86()
    {
        var path = DerivationPaths.AccountPath(ScriptKind.Taproot, ChainProfiles.Mainnet, 0);
        Assert.Equal($"86'/{ChainProfiles.Mainnet.Bip44CoinType}'/0'", path.ToString());
    }

    [Fact]
    public void Gli_indirizzi_plm_taproot_iniziano_con_plm1p()
    {
        var account = Account();
        var addr0 = account.GetReceiveAddress(0).ToString();
        var addr1 = account.GetReceiveAddress(1).ToString();
        var change0 = account.GetChangeAddress(0).ToString();

        Assert.StartsWith("plm1p", addr0);
        Assert.StartsWith("plm1p", addr1);
        Assert.StartsWith("plm1p", change0);
    }

    /// <summary>
    /// L'output key (chiave tweakizzata x-only, 32 byte) è identica al vettore BIP86
    /// indipendentemente dalla rete: la rete cambia solo HRP e checksum, non il programma.
    /// Indirizzi Bitcoin da https://github.com/bitcoin/bips/blob/master/bip-0086.mediawiki
    /// </summary>
    [Theory]
    [InlineData(false, 0, "bc1p5cyxnuxmeuwuvkwfem96lqzszd02n6xdcjrs20cac6yqjjwudpxqkedrcr")]
    [InlineData(false, 1, "bc1p4qhjn9zdvkux4e44uhx8tc55attvtyu358kutcqkudyccelu0was9fqzwh")]
    [InlineData(true,  0, "bc1p3qkhfews2uk44qtvauqyr2ttdsw7svhkl9nkm9s9c3x4ax5h60wqwruhk7")]
    public void L_output_key_coincide_col_vettore_bip86(
        bool isChange, int index, string bitcoinAddress)
    {
        var account = Account();
        var plmAddr = account.GetAddress(isChange, index);

        // Il witness program (output key tweakizzata) è chain-independent
        var btcAddr = (TaprootAddress)BitcoinAddress.Create(bitcoinAddress, Network.Main);
        var plmTaproot = (TaprootAddress)plmAddr;
        Assert.Equal(btcAddr.PubKey, plmTaproot.PubKey);
    }

    [Fact]
    public void Il_wallet_taproot_e_watch_only_se_creato_da_xpub()
    {
        var full = Account();
        var watchOnly = HdAccount.FromAccountXpub(
            full.AccountXpub, ScriptKind.Taproot, ChainProfiles.Mainnet);
        Assert.True(watchOnly.IsWatchOnly);
        Assert.Equal(
            full.GetReceiveAddress(0).ToString(),
            watchOnly.GetReceiveAddress(0).ToString());
    }

    [Fact]
    public void I_tipi_multisig_lanciano_not_supported()
    {
        Assert.Throws<NotSupportedException>(
            () => DerivationPaths.ScriptPubKeyTypeFor(ScriptKind.WrappedSegwitMultisig));
        Assert.Throws<NotSupportedException>(
            () => DerivationPaths.ScriptPubKeyTypeFor(ScriptKind.NativeSegwitMultisig));
    }
}
