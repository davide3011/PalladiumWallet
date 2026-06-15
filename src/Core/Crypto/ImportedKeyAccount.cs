using NBitcoin;
using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Crypto;

/// <summary>
/// Account da chiavi WIF singole importate (blueprint §4.4 — "Imported"):
/// lista fissa di indirizzi, nessuna derivazione HD, nessuna catena di change.
/// Il change va sempre al primo indirizzo importato.
/// </summary>
public sealed class ImportedKeyAccount : IWalletAccount
{
    private readonly (BitcoinAddress Address, Key? PrivateKey)[] _entries;

    public ScriptKind Kind { get; }
    public ChainProfile Profile { get; }
    public bool IsWatchOnly => _entries.All(e => e.PrivateKey is null);

    public IReadOnlyList<(BitcoinAddress Address, bool IsChange, int Index)>? FixedAddresses =>
        _entries.Select((e, i) => (e.Address, false, i)).ToList();

    public ImportedKeyAccount(
        IReadOnlyList<(BitcoinAddress Address, Key? PrivateKey)> entries,
        ScriptKind kind, ChainProfile profile)
    {
        if (entries.Count == 0)
            throw new ArgumentException("Almeno un indirizzo richiesto.", nameof(entries));
        _entries = [.. entries];
        Kind = kind;
        Profile = profile;
    }

    public BitcoinAddress GetAddress(bool isChange, int index)
    {
        if (isChange || index < 0 || index >= _entries.Length)
            return _entries[0].Address;
        return _entries[index].Address;
    }

    public BitcoinAddress GetReceiveAddress(int index) => GetAddress(false, index);

    /// <summary>Il change torna sempre al primo indirizzo importato.</summary>
    public BitcoinAddress GetChangeAddress(int index) => _entries[0].Address;

    public PubKey? GetPublicKey(bool isChange, int index)
    {
        if (isChange || index < 0 || index >= _entries.Length)
            return null;
        return _entries[index].PrivateKey?.PubKey;
    }

    public Key? GetPrivateKey(bool isChange, int index)
    {
        if (isChange || index < 0 || index >= _entries.Length)
            return null;
        return _entries[index].PrivateKey;
    }
}
