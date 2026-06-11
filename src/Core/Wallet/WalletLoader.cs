using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.Core.Wallet;

/// <summary>
/// Ponte documento wallet ↔ dominio (blueprint §4.5): dal file ricostruisce
/// l'HdAccount giusto (da seed o watch-only da xpub). È l'embrione della
/// factory dei tipi di wallet; crescerà con multisig e importati.
/// </summary>
public static class WalletLoader
{
    public static ChainProfile ProfileOf(WalletDocument doc) =>
        ChainProfiles.For(Enum.Parse<NetKind>(doc.Network, ignoreCase: true));

    public static HdAccount ToAccount(WalletDocument doc)
    {
        var profile = ProfileOf(doc);
        var kind = Enum.Parse<ScriptKind>(doc.ScriptKind);
        var path = KeyPath.Parse(doc.AccountPath);

        if (doc.Mnemonic is { } words)
        {
            if (!Bip39.TryParse(words, out var mnemonic))
                throw new InvalidDataException("Mnemonica del file wallet non valida.");
            return HdAccount.FromSeed(Bip39.ToSeed(mnemonic!, doc.Passphrase), kind, profile, path);
        }

        if (!Slip132.TryDecodePublic(doc.AccountXpub, profile, out var xpub, out _))
            throw new InvalidDataException("Xpub del file wallet non valida per questa rete.");
        return HdAccount.FromAccountXpub(xpub!, kind, profile, path);
    }

    /// <summary>Crea il documento per un nuovo wallet da seed.</summary>
    public static (WalletDocument Doc, HdAccount Account) NewFromMnemonic(
        string words, string? passphrase, ScriptKind kind, ChainProfile profile, KeyPath? customPath = null)
    {
        if (!Bip39.TryParse(words, out var mnemonic))
            throw new InvalidDataException("Mnemonica non valida (parole o checksum errati).");

        var account = customPath is null
            ? HdAccount.FromMnemonic(mnemonic!, passphrase, kind, profile)
            : HdAccount.FromSeed(Bip39.ToSeed(mnemonic!, passphrase), kind, profile, customPath);

        var doc = new WalletDocument
        {
            Network = profile.NetName,
            ScriptKind = kind.ToString(),
            Mnemonic = words.Trim(),
            Passphrase = passphrase,
            AccountPath = account.AccountPath.ToString(),
            AccountXpub = account.ToSlip132(),
            MasterFingerprint = Convert.ToHexString(account.MasterFingerprint.ToBytes()).ToLowerInvariant(),
        };
        return (doc, account);
    }
}
