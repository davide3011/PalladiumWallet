using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;

namespace PalladiumWallet.Tests.Crypto;

/// <summary>
/// Vettore di test della specifica BIP84 (mnemonica abandon-about, senza
/// passphrase). Gli header SLIP-132 della mainnet PLM coincidono con quelli
/// Bitcoin, quindi zprv/zpub si confrontano direttamente; gli indirizzi si
/// confrontano sul witness program (chain-independent) + prefisso PLM.
/// Il path m/84'/0'/0' è volutamente "personalizzato" (coin type 0, non 746):
/// esercita anche l'import con path custom (§4.2).
/// </summary>
public class Bip84Slip132Tests
{
    private static HdAccount Account()
    {
        Assert.True(Bip39.TryParse(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            out var mnemonic));
        return HdAccount.FromSeed(
            Bip39.ToSeed(mnemonic!),
            ScriptKind.NativeSegwit,
            ChainProfiles.Mainnet,
            new KeyPath("84'/0'/0'"));
    }

    [Fact]
    public void L_account_serializza_la_zprv_e_la_zpub_del_vettore()
    {
        var account = Account();
        Assert.Equal(
            "zprvAdG4iTXWBoARxkkzNpNh8r6Qag3irQB8PzEMkAFeTRXxHpbF9z4QgEvBRmfvqWvGp42t42nvgGpNgYSJA9iefm1yYNZKEm7z6qUWCroSQnE",
            account.ToSlip132Private());
        Assert.Equal(
            "zpub6rFR7y4Q2AijBEqTUquhVz398htDFrtymD9xYYfG1m4wAcvPhXNfE3EfH1r1ADqtfSdVCToUG868RvUUkgDKf31mGDtKsAYz2oz2AGutZYs",
            account.ToSlip132());
    }

    [Fact]
    public void La_zpub_fa_roundtrip_e_viene_riconosciuta_come_native_segwit()
    {
        var account = Account();
        var encoded = account.ToSlip132();

        Assert.True(Slip132.TryDecodePublic(encoded, ChainProfiles.Mainnet, out var decoded, out var kind));
        Assert.Equal(ScriptKind.NativeSegwit, kind);
        Assert.Equal(account.AccountXpub.ToString(PalladiumNetworks.Mainnet),
            decoded!.ToString(PalladiumNetworks.Mainnet));
    }

    [Fact]
    public void La_zprv_fa_roundtrip_con_riconoscimento_del_tipo()
    {
        var account = Account();
        Assert.True(Slip132.TryDecodePrivate(account.ToSlip132Private(), ChainProfiles.Mainnet,
            out var decoded, out var kind));
        Assert.Equal(ScriptKind.NativeSegwit, kind);
        Assert.Equal(account.ToSlip132Private(),
            Slip132.Encode(decoded!, kind, ChainProfiles.Mainnet));
    }

    [Fact]
    public void Una_xkey_con_header_sconosciuto_viene_rifiutata()
    {
        // xpub Bitcoin valida ma con header Legacy: non è una zpub.
        var account = Account();
        var asXpub = account.AccountXpub.ToString(Network.Main);

        Assert.True(Slip132.TryDecodePublic(asXpub, ChainProfiles.Mainnet, out _, out var kind));
        Assert.Equal(ScriptKind.Legacy, kind); // header xpub → riconosciuto come Legacy
        Assert.False(Slip132.TryDecodePublic("non-base58!!!", ChainProfiles.Mainnet, out _, out _));
        Assert.False(Slip132.TryDecodePrivate(asXpub, ChainProfiles.Mainnet, out _, out _)); // pub ≠ priv
    }

    [Theory]
    [InlineData(false, 0, "0330d54fd0dd420a6e5f8d3624f5f3482cae350f79d5f0753bf5beef9c2d91af3c",
        "bc1qcr8te4kr609gcawutmrza0j4xv80jy8z306fyu")]
    [InlineData(false, 1, null, "bc1qnjg0jd8228aq7egyzacy8cys3knf9xvrerkf9g")]
    [InlineData(true, 0, null, "bc1q8c6fshw2dlwun7ekn9qwf37cu2rn755upcp6el")]
    public void Gli_indirizzi_del_vettore_hanno_lo_stesso_witness_program_su_plm(
        bool isChange, int index, string? expectedPubKeyHex, string bitcoinAddress)
    {
        var account = Account();
        var pubKey = account.GetPublicKey(isChange, index);

        if (expectedPubKeyHex is not null)
            Assert.Equal(expectedPubKeyHex, pubKey.ToHex());

        // Il witness program (hash160 della pubkey) è chain-independent: deve
        // coincidere con quello dell'indirizzo bc1 del vettore ufficiale.
        var bitcoinExpected = (BitcoinWitPubKeyAddress)BitcoinAddress.Create(bitcoinAddress, Network.Main);
        Assert.Equal(bitcoinExpected.Hash, pubKey.WitHash);

        var plmAddress = account.GetAddress(isChange, index);
        Assert.StartsWith("plm1q", plmAddress.ToString());
        Assert.Equal(pubKey.WitHash, ((BitcoinWitPubKeyAddress)plmAddress).Hash);
    }
}
