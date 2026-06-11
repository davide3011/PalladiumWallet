using PalladiumWallet.Core.Crypto;

namespace PalladiumWallet.Tests.Crypto;

public class Bip39Tests
{
    // Vettori ufficiali Trezor (python-mnemonic/vectors.json), passphrase "TREZOR".
    [Theory]
    [InlineData(
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
        "c55257c360c07c72029aebc1b53c05ed0362ada38ead3e3e9efa3708e53495531f09a6987599d18264c1e1c92f2cf141630c7a3c4ab7c81b2f001698e7463b04")]
    [InlineData(
        "legal winner thank year wave sausage worth useful legal winner thank yellow",
        "2e8905819b8723fe2c1d161860e5ee1830318dbf49a83bd451cfb8440c28bd6fa457fe1296106559a3c80937a1c1069be3a3a5bd381ee6260e8d9739fce1f607")]
    [InlineData(
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon art",
        "bda85446c68413707090a52022edd26a1c9462295029f2e60cd7c4f2bbd3097170af7a4d73245cafa9c3cca8d561a7c3de6f5d4a10be8ed2a5e608d68f92fcc8")]
    [InlineData(
        "zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo zoo vote",
        "dd48c104698c30cfe2b6142103248622fb7bb0ff692eebb00089b32d22484e1613912f0a5b694407be899ffd31ed3992c456cdf60f5d4564b8ba3f05a69890ad")]
    [InlineData(
        "void come effort suffer camp survey warrior heavy shoot primary clutch crush open amazing screen patrol group space point ten exist slush involve unfold",
        "01f5bced59dec48e362f2c45b5de68b9fd6c92c6634f44d6d40aab69056506f0e35524a518034ddc1192e1dacd32c1ed3eaa3c3b131c88ed8e7e54c49a5d0998")]
    public void I_vettori_trezor_producono_il_seed_atteso(string words, string expectedSeedHex)
    {
        Assert.True(Bip39.TryParse(words, out var mnemonic));
        Assert.Equal(expectedSeedHex, Convert.ToHexString(Bip39.ToSeed(mnemonic!, "TREZOR")).ToLowerInvariant());
    }

    [Fact]
    public void Il_vettore_giapponese_copre_la_normalizzazione_nfkd()
    {
        // Vettore ufficiale bip32JP (test_JP_BIP39.json[0]): mnemonica con spazi
        // ideografici U+3000 e passphrase con caratteri da normalizzare NFKD.
        const string words =
            "あいこくしん　あいこくしん　あいこくしん　あいこくしん　あいこくしん　あいこくしん　あいこくしん　あいこくしん　あいこくしん　あいこくしん　あいこくしん　あおぞら";
        const string passphrase = "㍍ガバヴァぱばぐゞちぢ十人十色";
        const string expectedSeed =
            "a262d6fb6122ecf45be09c50492b31f92e9beb7d9a845987a02cefda57a15f9c467a17872029a9e92299b5cbdf306e3a0ee620245cbd508959b6cb7ca637bd55";

        Assert.True(Bip39.TryParse(words, out var mnemonic, MnemonicLanguage.Japanese));
        Assert.Equal(expectedSeed, Convert.ToHexString(Bip39.ToSeed(mnemonic!, passphrase)).ToLowerInvariant());
    }

    [Theory]
    [InlineData(MnemonicLength.Twelve)]
    [InlineData(MnemonicLength.TwentyFour)]
    public void Generate_produce_il_numero_di_parole_richiesto_con_checksum_valido(MnemonicLength length)
    {
        var mnemonic = Bip39.Generate(length);
        Assert.Equal((int)length, mnemonic.Words.Length);
        Assert.True(Bip39.TryParse(mnemonic.ToString(), out _));
    }

    [Fact]
    public void Checksum_invalido_viene_rifiutato()
    {
        // 12 × "abandon": parole valide ma checksum errato — il caso che il
        // costruttore NBitcoin non controlla da solo.
        var words = string.Join(' ', Enumerable.Repeat("abandon", 12));
        Assert.False(Bip39.TryParse(words, out _));
    }

    [Fact]
    public void Numero_di_parole_errato_viene_rifiutato()
    {
        var words = string.Join(' ', Enumerable.Repeat("abandon", 12)) + " about";
        Assert.False(Bip39.TryParse(words, out _));
    }

    [Fact]
    public void La_lingua_viene_riconosciuta_automaticamente()
    {
        var spanish = Bip39.Generate(MnemonicLength.Twelve, MnemonicLanguage.Spanish);
        Assert.True(Bip39.TryParse(spanish.ToString(), out var parsed));
        Assert.Equal(spanish.ToString(), parsed!.ToString());
    }

    [Fact]
    public void Passphrase_diverse_producono_seed_diversi()
    {
        Assert.True(Bip39.TryParse(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            out var mnemonic));

        var noPass = Bip39.ToSeed(mnemonic!);
        var withPass = Bip39.ToSeed(mnemonic!, "TREZOR");
        var otherPass = Bip39.ToSeed(mnemonic!, "trezor"); // case-sensitive (§4.1)

        Assert.NotEqual(Convert.ToHexString(noPass), Convert.ToHexString(withPass));
        Assert.NotEqual(Convert.ToHexString(withPass), Convert.ToHexString(otherPass));
    }
}
