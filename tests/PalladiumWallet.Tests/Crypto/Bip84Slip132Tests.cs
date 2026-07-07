using NBitcoin;
using NBitcoin.DataEncoders;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;

namespace PalladiumWallet.Tests.Crypto;

/// <summary>
/// BIP84 specification test vector (abandon-about mnemonic, without
/// passphrase). The SLIP-132 headers of the PLM mainnet coincide with the
/// Bitcoin ones, so zprv/zpub are compared directly; addresses are
/// compared on the witness program (chain-independent) + PLM prefix.
/// The path m/84'/0'/0' is deliberately "customized" (coin type 0, not 746):
/// it also exercises the import with a custom path (§4.2).
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
        // Valid Bitcoin xpub but with Legacy header: it is not a zpub.
        var account = Account();
        var asXpub = account.AccountXpub.ToString(Network.Main);

        Assert.True(Slip132.TryDecodePublic(asXpub, ChainProfiles.Mainnet, out _, out var kind));
        Assert.Equal(ScriptKind.Legacy, kind); // xpub header → recognized as Legacy
        Assert.False(Slip132.TryDecodePublic("non-base58!!!", ChainProfiles.Mainnet, out _, out _));
        Assert.False(Slip132.TryDecodePrivate(asXpub, ChainProfiles.Mainnet, out _, out _)); // pub ≠ priv
    }

    [Fact]
    public void Una_xkey_base58_valida_ma_di_lunghezza_sbagliata_viene_rifiutata()
    {
        // Well-formed Base58Check, but the decoded payload is not 78 bytes.
        var tooShort = Encoders.Base58Check.EncodeData([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        Assert.False(Slip132.TryDecodePublic(tooShort, ChainProfiles.Mainnet, out _, out _));
        Assert.False(Slip132.TryDecodePrivate(tooShort, ChainProfiles.Mainnet, out _, out _));
    }

    [Fact]
    public void Una_zpub_con_payload_corrotto_viene_rifiutata_senza_eccezioni()
    {
        // Correct zpub header, but the 33-byte pubkey has an invalid point prefix:
        // the ExtPubKey constructor must fail and TryDecodePublic must return false.
        var data = Encoders.Base58Check.DecodeData(Account().ToSlip132());
        data[45] = 0xFF; // pubkey prefix (offset 4 header + 41) — not 0x02/0x03
        var corrupted = Encoders.Base58Check.EncodeData(data);

        Assert.False(Slip132.TryDecodePublic(corrupted, ChainProfiles.Mainnet, out _, out _));
    }

    [Fact]
    public void Una_zprv_con_payload_corrotto_viene_rifiutata_senza_eccezioni()
    {
        // Correct zprv header, but the byte before the 32-byte key must be 0x00:
        // ExtKey.CreateFromBytes must fail and TryDecodePrivate must return false.
        var data = Encoders.Base58Check.DecodeData(Account().ToSlip132Private());
        data[45] = 0xFF;
        var corrupted = Encoders.Base58Check.EncodeData(data);

        Assert.False(Slip132.TryDecodePrivate(corrupted, ChainProfiles.Mainnet, out _, out _));
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
        var pubKey = account.GetPublicKey(isChange, index)!;

        if (expectedPubKeyHex is not null)
            Assert.Equal(expectedPubKeyHex, pubKey.ToHex());

        // The witness program (hash160 of the pubkey) is chain-independent: it must
        // coincide with that of the bc1 address from the official vector.
        var bitcoinExpected = (BitcoinWitPubKeyAddress)BitcoinAddress.Create(bitcoinAddress, Network.Main);
        Assert.Equal(bitcoinExpected.Hash, pubKey.WitHash);

        var plmAddress = account.GetAddress(isChange, index);
        Assert.StartsWith("plm1q", plmAddress.ToString());
        Assert.Equal(pubKey.WitHash, ((BitcoinWitPubKeyAddress)plmAddress).Hash);
    }
}
