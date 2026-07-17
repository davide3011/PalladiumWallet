using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.Core.Wallet;

/// <summary>
/// Confirmation-threshold rules shared between coin selection (<see cref="TransactionFactory"/>)
/// and balance reporting: a coinbase output needs COINBASE_MATURITY + 1 confirmations
/// (consensus rule nSpendHeight - nHeight >= 120, plus one block of safety margin, matching
/// the Qt wallet); a regular output needs <see cref="ChainProfile.MinConfirmations"/> (wallet
/// policy, no consensus rule).
/// </summary>
public static class UtxoSpendability
{
    public static int RequiredConfirmations(this CachedUtxo utxo, ChainProfile profile) =>
        utxo.IsCoinbase ? profile.CoinbaseMaturity + 1 : profile.MinConfirmations;

    public static int Confirmations(this CachedUtxo utxo, int tipHeight) =>
        utxo.Height <= 0 ? 0 : tipHeight - utxo.Height + 1;

    /// <summary>
    /// True when the UTXO has met its confirmation threshold, is not frozen, and its Merkle
    /// proof has actually been checked. Without the <see cref="CachedUtxo.Verified"/> gate a
    /// malicious server could report a fake confirmed UTXO and have it spent before background
    /// verification ever caught the forgery.
    /// </summary>
    public static bool IsSpendable(this CachedUtxo utxo, ChainProfile profile, int tipHeight) =>
        !utxo.Frozen && utxo.Height > 0 && utxo.Verified
        && utxo.Confirmations(tipHeight) >= utxo.RequiredConfirmations(profile);
}
