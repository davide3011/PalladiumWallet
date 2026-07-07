using NBitcoin;

namespace PalladiumWallet.Core.Crypto;

/// <summary>
/// Supported BIP39 wordlist languages (blueprint §4.1). The lists are embedded
/// in NBitcoin: no network or filesystem access required.
/// </summary>
public enum MnemonicLanguage
{
    English,
    Spanish,
    Japanese,
    PortugueseBrazil,
    ChineseSimplified,
    ChineseTraditional,
    French,
    Czech,
}

/// <summary>Supported word counts for mnemonic creation (blueprint §4.1: 12 or 24).</summary>
public enum MnemonicLength
{
    Twelve = 12,
    TwentyFour = 24,
}

/// <summary>
/// BIP39 facade over NBitcoin.Mnemonic (blueprint §4.1). NBitcoin already handles
/// entropy→words, checksum, NFKD normalisation, and PBKDF2-HMAC-SHA512 with 2048
/// rounds using salt "mnemonic"+passphrase. This class narrows the API to the
/// blueprint use-cases and centralises the language map. When the native versioned
/// seed arrives (§4.1 point 1), multi-scheme recognition will be a chain of
/// TryParse calls per scheme.
/// </summary>
public static class Bip39
{
    /// <summary>Generates a new mnemonic using entropy from the system CSPRNG.</summary>
    public static Mnemonic Generate(MnemonicLength length, MnemonicLanguage language = MnemonicLanguage.English)
    {
        var wordCount = length == MnemonicLength.Twelve ? WordCount.Twelve : WordCount.TwentyFour;
        return new Mnemonic(ToWordlist(language), wordCount);
    }

    /// <summary>
    /// Recognises and validates a BIP39 mnemonic: word count, wordlist membership,
    /// and checksum. If <paramref name="language"/> is specified it is tried first;
    /// otherwise the language is auto-detected (words shared between lists can make
    /// autodetect ambiguous — the user can override it on import).
    /// </summary>
    public static bool TryParse(string text, out Mnemonic? mnemonic, MnemonicLanguage? language = null)
    {
        mnemonic = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Outer trim only: NBitcoin handles NFKD normalisation and Japanese
        // ideographic spaces — the text must not be altered further (§4.1).
        text = text.Trim();

        Wordlist wordlist;
        if (language is not null)
        {
            wordlist = ToWordlist(language.Value);
        }
        else
        {
            try
            {
                wordlist = Wordlist.AutoDetect(text);
            }
            catch (NotSupportedException)
            {
                // AutoDetect lands on NBitcoin's internal "Unknown" language for
                // text resembling no wordlist and throws instead of returning a
                // default (found by fuzzing) — not a valid mnemonic, plain and simple.
                return false;
            }
        }

        try
        {
            var parsed = new Mnemonic(text, wordlist);
            // The constructor does NOT verify the checksum — explicit check is mandatory.
            if (parsed.IsValidChecksum && parsed.Words.Length is 12 or 15 or 18 or 21 or 24)
            {
                mnemonic = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // Words outside the wordlist or invalid count.
        }
        catch (NotSupportedException)
        {
            // Defensive: same NBitcoin failure mode surfacing from the constructor.
        }

        return false;
    }

    /// <summary>
    /// Mnemonic → 64-byte seed (PBKDF2-HMAC-SHA512, 2048 rounds, salt
    /// "mnemonic"+passphrase). The passphrase completely changes the derived
    /// wallet — mandatory UI warnings required (§4.1).
    /// </summary>
    public static byte[] ToSeed(Mnemonic mnemonic, string? passphrase = null) =>
        mnemonic.DeriveSeed(passphrase);

    internal static Wordlist ToWordlist(MnemonicLanguage language) => language switch
    {
        MnemonicLanguage.English => Wordlist.English,
        MnemonicLanguage.Spanish => Wordlist.Spanish,
        MnemonicLanguage.Japanese => Wordlist.Japanese,
        MnemonicLanguage.PortugueseBrazil => Wordlist.PortugueseBrazil,
        MnemonicLanguage.ChineseSimplified => Wordlist.ChineseSimplified,
        MnemonicLanguage.ChineseTraditional => Wordlist.ChineseTraditional,
        MnemonicLanguage.French => Wordlist.French,
        MnemonicLanguage.Czech => Wordlist.Czech,
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };
}
