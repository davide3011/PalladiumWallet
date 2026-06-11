using NBitcoin;

namespace PalladiumWallet.Core.Crypto;

/// <summary>
/// Lingue wordlist BIP39 supportate (blueprint §4.1). Le liste sono incorporate
/// in NBitcoin: nessun accesso a rete o filesystem.
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

/// <summary>Numero di parole supportato in creazione (blueprint §4.1: 12 o 24).</summary>
public enum MnemonicLength
{
    Twelve = 12,
    TwentyFour = 24,
}

/// <summary>
/// Facciata BIP39 su NBitcoin.Mnemonic (blueprint §4.1). NBitcoin copre già
/// entropia→parole, checksum, normalizzazione NFKD e PBKDF2-HMAC-SHA512 a 2048
/// round con salt "mnemonic"+passphrase: qui si restringe l'API ai casi del
/// blueprint e si centralizza la mappa delle lingue. Quando arriverà il seed
/// nativo versionato (§4.1 punto 1), il riconoscimento multi-schema sarà una
/// catena di TryParse per schema.
/// </summary>
public static class Bip39
{
    /// <summary>Genera una nuova mnemonica con entropia dal CSPRNG di sistema.</summary>
    public static Mnemonic Generate(MnemonicLength length, MnemonicLanguage language = MnemonicLanguage.English)
    {
        var wordCount = length == MnemonicLength.Twelve ? WordCount.Twelve : WordCount.TwentyFour;
        return new Mnemonic(ToWordlist(language), wordCount);
    }

    /// <summary>
    /// Riconosce e valida una mnemonica BIP39: numero di parole, appartenenza alla
    /// wordlist e checksum. Se <paramref name="language"/> è indicata viene provata
    /// per prima; altrimenti la lingua è auto-rilevata (parole condivise tra liste
    /// possono rendere ambiguo l'autodetect: in import l'utente può forzarla).
    /// </summary>
    public static bool TryParse(string text, out Mnemonic? mnemonic, MnemonicLanguage? language = null)
    {
        mnemonic = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Solo trim esterno: la normalizzazione NFKD e lo spazio ideografico
        // giapponese li gestisce NBitcoin, il testo non va alterato (§4.1).
        text = text.Trim();

        IEnumerable<Wordlist> candidates = language is not null
            ? [ToWordlist(language.Value)]
            : [Wordlist.AutoDetect(text)];

        foreach (var wordlist in candidates)
        {
            try
            {
                var parsed = new Mnemonic(text, wordlist);
                // Il costruttore NON verifica il checksum: controllo esplicito obbligatorio.
                if (parsed.IsValidChecksum && parsed.Words.Length is 12 or 15 or 18 or 21 or 24)
                {
                    mnemonic = parsed;
                    return true;
                }
            }
            catch (FormatException)
            {
                // Parole fuori wordlist o conteggio non valido: si prova oltre.
            }
        }

        return false;
    }

    /// <summary>
    /// Mnemonica → seed di 64 byte (PBKDF2-HMAC-SHA512, 2048 round, salt
    /// "mnemonic"+passphrase). La passphrase cambia completamente il wallet
    /// derivato: avvisi UI obbligatori (§4.1).
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
