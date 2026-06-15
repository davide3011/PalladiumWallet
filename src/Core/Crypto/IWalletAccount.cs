using NBitcoin;
using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Crypto;

/// <summary>
/// Astrazione su tutti i tipi di account wallet (HD da seed, HD da xpub/xprv importata,
/// chiavi WIF importate). Consente a WalletSynchronizer e TransactionFactory di operare
/// indipendentemente dal tipo di keystore sottostante (blueprint §4.4–§4.5).
/// </summary>
public interface IWalletAccount
{
    ScriptKind Kind { get; }
    ChainProfile Profile { get; }

    /// <summary>True se l'account non può firmare (assenza di chiavi private).</summary>
    bool IsWatchOnly { get; }

    BitcoinAddress GetAddress(bool isChange, int index);
    BitcoinAddress GetReceiveAddress(int index);
    BitcoinAddress GetChangeAddress(int index);

    /// <summary>Null se la chiave pubblica non è ricavabile dall'account (indirizzi puri watch-only).</summary>
    PubKey? GetPublicKey(bool isChange, int index);

    /// <summary>Null se l'account è watch-only o l'indice è fuori range.</summary>
    Key? GetPrivateKey(bool isChange, int index);

    /// <summary>
    /// Per gli account con indirizzi fissi (WIF importati) restituisce la lista
    /// completa da scansionare; null per gli account HD che usano il gap limit.
    /// </summary>
    IReadOnlyList<(BitcoinAddress Address, bool IsChange, int Index)>? FixedAddresses { get; }
}
