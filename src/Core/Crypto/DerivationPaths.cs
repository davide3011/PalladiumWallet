using NBitcoin;
using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Crypto;

/// <summary>
/// Costruzione dei derivation path BIP44/49/84 (blueprint §4.2) e mappatura
/// ScriptKind → purpose / ScriptPubKeyType. I purpose sono costanti di protocollo
/// BIP (chain-independent); il coin_type viene sempre dal ChainProfile.
/// </summary>
public static class DerivationPaths
{
    /// <summary>Purpose BIP44 (P2PKH legacy).</summary>
    public const int PurposeLegacy = 44;

    /// <summary>Purpose BIP49 (P2SH-P2WPKH).</summary>
    public const int PurposeWrappedSegwit = 49;

    /// <summary>Purpose BIP84 (P2WPKH nativo).</summary>
    public const int PurposeNativeSegwit = 84;

    /// <summary>Purpose BIP48 (multisig — fase successiva, §16 passo 8).</summary>
    public const int PurposeMultisig = 48;

    public static int PurposeFor(ScriptKind kind) => kind switch
    {
        ScriptKind.Legacy => PurposeLegacy,
        ScriptKind.WrappedSegwit => PurposeWrappedSegwit,
        ScriptKind.NativeSegwit => PurposeNativeSegwit,
        ScriptKind.WrappedSegwitMultisig or ScriptKind.NativeSegwitMultisig => PurposeMultisig,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Tipo di scriptPubKey NBitcoin per la derivazione da pubkey singola.
    /// I tipi multisig derivano da redeem script, non da una pubkey: arriveranno
    /// con i wallet M-di-N (§4.5).
    /// </summary>
    public static ScriptPubKeyType ScriptPubKeyTypeFor(ScriptKind kind) => kind switch
    {
        ScriptKind.Legacy => ScriptPubKeyType.Legacy,
        ScriptKind.WrappedSegwit => ScriptPubKeyType.SegwitP2SH,
        ScriptKind.NativeSegwit => ScriptPubKeyType.Segwit,
        ScriptKind.WrappedSegwitMultisig or ScriptKind.NativeSegwitMultisig =>
            throw new NotSupportedException(
                "I tipi multisig derivano da redeem script: supporto previsto con i wallet M-di-N (§4.5)."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Path di account relativo alla root: purpose'/coin'/account' (§4.2).
    /// Il coin_type è quello del profilo (746 mainnet, 1 testnet).
    /// </summary>
    public static KeyPath AccountPath(ScriptKind kind, ChainProfile profile, int account = 0)
    {
        if (account < 0)
            throw new ArgumentOutOfRangeException(nameof(account));
        return new KeyPath(
            $"{PurposeFor(kind)}'/{profile.Bip44CoinType}'/{account}'");
    }

    /// <summary>Sottopath non-hardened change/index sotto l'account (change=0 receiving, change=1 change).</summary>
    public static KeyPath AddressSubPath(bool isChange, int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new KeyPath($"{(isChange ? 1 : 0)}/{index}");
    }

    /// <summary>
    /// Parsing di derivation path personalizzati in import (Sparrow-like, §4.2):
    /// accetta il prefisso "m/" opzionale e gli hardened marker ' oppure h.
    /// </summary>
    public static bool TryParse(string path, out KeyPath? keyPath)
    {
        keyPath = null;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        try
        {
            keyPath = KeyPath.Parse(path.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
