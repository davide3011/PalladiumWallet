using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.Core.Wallet;

/// <summary>
/// Factory dei tipi di wallet (blueprint §4.5): dal documento ricostruisce il tipo
/// di account corretto (HD da seed, HD da xprv importata, importato WIF, watch-only).
/// </summary>
public static class WalletLoader
{
    public static ChainProfile ProfileOf(WalletDocument doc) =>
        ChainProfiles.For(Enum.Parse<NetKind>(doc.Network, ignoreCase: true));

    public static IWalletAccount ToAccount(WalletDocument doc)
    {
        var profile = ProfileOf(doc);
        var kind = Enum.Parse<ScriptKind>(doc.ScriptKind);
        var network = PalladiumNetworks.For(profile.Kind);

        // 1. HD da seed (caso più comune)
        if (doc.Mnemonic is { } words)
        {
            if (!Bip39.TryParse(words, out var mnemonic))
                throw new InvalidDataException("Mnemonica del file wallet non valida.");
            var path = KeyPath.Parse(doc.AccountPath ?? DerivationPaths.AccountPath(kind, profile).ToString());
            return HdAccount.FromSeed(Bip39.ToSeed(mnemonic!, doc.Passphrase), kind, profile, path);
        }

        // 2. HD da xprv importata (spendibile, senza seed)
        if (doc.AccountXprv is { } xprvStr)
        {
            if (!Slip132.TryDecodePrivate(xprvStr, profile, out var xprv, out _))
                throw new InvalidDataException("Xprv del file wallet non valida per questa rete.");
            var path = doc.AccountPath is { Length: > 0 } p ? KeyPath.Parse(p) : null;
            return HdAccount.FromAccountXprv(xprv!, kind, profile, path);
        }

        // 3. Chiavi WIF importate
        if (doc.WifKeys is { Count: > 0 } wifKeys)
        {
            var entries = wifKeys.Select(wif =>
            {
                var secret = new BitcoinSecret(wif, network);
                var addr = secret.PrivateKey.PubKey
                    .GetAddress(DerivationPaths.ScriptPubKeyTypeFor(kind), network);
                return (addr, (Key?)secret.PrivateKey);
            }).ToList();
            return new ImportedKeyAccount(entries, kind, profile);
        }

        // 4. Watch-only da xpub
        if (doc.AccountXpub is null)
            throw new InvalidDataException("File wallet senza xpub e senza seed.");
        if (!Slip132.TryDecodePublic(doc.AccountXpub, profile, out var xpub, out _))
            throw new InvalidDataException("Xpub del file wallet non valida per questa rete.");
        var accountPath = doc.AccountPath is { Length: > 0 } ap ? KeyPath.Parse(ap) : null;
        return HdAccount.FromAccountXpub(xpub!, kind, profile, accountPath);
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

    /// <summary>
    /// Crea il documento da una xpub SLIP-132 importata (watch-only).
    /// Rileva il ScriptKind dagli header SLIP-132; <paramref name="kindOverride"/> lo sovrascrive
    /// per i prefissi ambigui (xpub può essere Legacy o Taproot).
    /// </summary>
    public static (WalletDocument Doc, HdAccount Account) NewFromXpub(
        string slip132, ChainProfile profile, ScriptKind? kindOverride = null)
    {
        if (!Slip132.TryDecodePublic(slip132.Trim(), profile, out var xpub, out var detectedKind))
            throw new InvalidDataException("Chiave pubblica estesa non valida o non riconosciuta per questa rete.");
        var kind = kindOverride ?? detectedKind;
        var account = HdAccount.FromAccountXpub(xpub!, kind, profile);

        var doc = new WalletDocument
        {
            Network = profile.NetName,
            ScriptKind = kind.ToString(),
            AccountPath = account.AccountPath.ToString(),
            AccountXpub = slip132.Trim(),
        };
        return (doc, account);
    }

    /// <summary>
    /// Crea il documento da una xprv SLIP-132 importata (spendibile senza seed).
    /// Il documento contiene la xprv in chiaro: deve essere obbligatoriamente cifrato.
    /// </summary>
    public static (WalletDocument Doc, HdAccount Account) NewFromXprv(
        string slip132, ChainProfile profile, ScriptKind? kindOverride = null)
    {
        if (!Slip132.TryDecodePrivate(slip132.Trim(), profile, out var xprv, out var detectedKind))
            throw new InvalidDataException("Chiave privata estesa non valida o non riconosciuta per questa rete.");
        var kind = kindOverride ?? detectedKind;
        var account = HdAccount.FromAccountXprv(xprv!, kind, profile);

        var doc = new WalletDocument
        {
            Network = profile.NetName,
            ScriptKind = kind.ToString(),
            AccountPath = account.AccountPath.ToString(),
            AccountXpub = account.ToSlip132(),
            AccountXprv = slip132.Trim(),
        };
        return (doc, account);
    }

    /// <summary>
    /// Crea il documento da una o più chiavi WIF importate.
    /// Il documento contiene le chiavi WIF in chiaro: deve essere obbligatoriamente cifrato.
    /// </summary>
    public static (WalletDocument Doc, ImportedKeyAccount Account) NewFromWif(
        IReadOnlyList<string> wifKeys, ScriptKind kind, ChainProfile profile)
    {
        if (wifKeys.Count == 0)
            throw new InvalidDataException("Almeno una chiave WIF richiesta.");

        var network = PalladiumNetworks.For(profile.Kind);
        var entries = new List<(BitcoinAddress, Key?)>();
        var wifStrings = new List<string>();

        foreach (var raw in wifKeys)
        {
            BitcoinSecret secret;
            try
            {
                secret = new BitcoinSecret(raw.Trim(), network);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Chiave WIF non valida: {ex.Message}");
            }
            var addr = secret.PrivateKey.PubKey
                .GetAddress(DerivationPaths.ScriptPubKeyTypeFor(kind), network);
            entries.Add((addr, secret.PrivateKey));
            wifStrings.Add(raw.Trim());
        }

        var account = new ImportedKeyAccount(entries, kind, profile);
        var doc = new WalletDocument
        {
            Network = profile.NetName,
            ScriptKind = kind.ToString(),
            WifKeys = wifStrings,
        };
        return (doc, account);
    }
}
