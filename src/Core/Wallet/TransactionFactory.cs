using NBitcoin;
using NBitcoin.Policy;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.Core.Wallet;

/// <summary>Result of a transaction build operation.</summary>
public sealed class BuiltTransaction
{
    public required Transaction Transaction { get; init; }
    public required Money Fee { get; init; }
    public required FeeRate FeeRate { get; init; }
    public required bool Signed { get; init; }

    /// <summary>PSBT for watch-only/air-gapped/multisig flows (§6.5).</summary>
    public required PSBT Psbt { get; init; }

    public string ToHex() => Transaction.ToHex();
    public string Txid => Transaction.GetHash().ToString();
}

/// <summary>
/// Transaction construction and signing (blueprint §6) on top of NBitcoin primitives:
/// coin selection (manual or automatic), fixed fee rate, send-all with fee subtracted,
/// change on the internal chain, RBF on by default.
/// With a watch-only account produces an unsigned PSBT (§6.5).
/// </summary>
public sealed class TransactionFactory(IWalletAccount account)
{
    private Network Network => PalladiumNetworks.For(account.Profile.Kind);

    /// <summary>
    /// Builds (and signs if possible) a transaction.
    /// </summary>
    /// <param name="utxos">Selected UTXOs (coin control §6.2) or all spendable ones.</param>
    /// <param name="transactions">Source transactions for the UTXOs (txid → tx), from sync.</param>
    /// <param name="destination">Recipient address.</param>
    /// <param name="amountSats">Amount; ignored when <paramref name="sendAll"/> is true.</param>
    /// <param name="feeRateSatPerVByte">Fixed fee rate in sat/vByte (§6.4).</param>
    /// <param name="changeIndex">Index of the next change address (internal chain).</param>
    /// <param name="tipHeight">Current chain tip height, used to enforce confirmation thresholds.</param>
    /// <param name="sendAll">Send all: fee subtracted from the amount (§6.1).</param>
    public BuiltTransaction Build(
        IReadOnlyList<CachedUtxo> utxos,
        IReadOnlyDictionary<string, Transaction> transactions,
        BitcoinAddress destination,
        long amountSats,
        decimal feeRateSatPerVByte,
        int changeIndex,
        int tipHeight,
        bool sendAll = false)
    {
        var profile = account.Profile;
        var spendable = utxos.Where(u => u.IsSpendable(profile, tipHeight)).ToList();

        if (spendable.Count == 0)
        {
            var reasons = new System.Text.StringBuilder();

            var mempool = utxos.Where(u => !u.Frozen && u.Height <= 0).ToList();
            if (mempool.Count > 0)
                reasons.Append($"{mempool.Count} output(s) unconfirmed ({CoinAmount.Format(mempool.Sum(u => u.ValueSats))} in mempool). ");

            var immature = utxos.Where(u =>
                !u.Frozen && u.Height > 0 && u.IsCoinbase &&
                u.Confirmations(tipHeight) < u.RequiredConfirmations(profile)).ToList();
            if (immature.Count > 0)
            {
                var best = immature.Max(u => u.Confirmations(tipHeight));
                var threshold = profile.CoinbaseMaturity + 1;
                reasons.Append($"{immature.Count} coinbase output(s) not yet mature ({best}/{threshold} confirmations). ");
            }

            var underConf = utxos.Where(u =>
                !u.Frozen && u.Height > 0 && !u.IsCoinbase &&
                u.Confirmations(tipHeight) < u.RequiredConfirmations(profile)).ToList();
            if (underConf.Count > 0)
            {
                var best = underConf.Max(u => u.Confirmations(tipHeight));
                reasons.Append($"{underConf.Count} output(s) need {profile.MinConfirmations} confirmations ({best} so far). ");
            }

            throw new WalletSpendException(reasons.Length > 0
                ? $"No spendable UTXOs: {reasons.ToString().TrimEnd()}"
                : "No spendable UTXOs selected.");
        }

        var coins = spendable.Select(u => new Coin(
            new OutPoint(uint256.Parse(u.Txid), (uint)u.Vout),
            transactions[u.Txid].Outputs[u.Vout])).ToList();

        var feeRate = new FeeRate(Money.Satoshis(feeRateSatPerVByte * 1000m), 1000);
        var builder = Network.CreateTransactionBuilder();
        builder.SetVersion(2);
        // RBF sequence to allow fee bumping (§6.6).
        builder.OptInRBF = true;
        builder.AddCoins(coins);
        builder.SetChange(account.GetChangeAddress(changeIndex));
        builder.SendEstimatedFees(feeRate);

        if (sendAll)
            builder.Send(destination, coins.Sum(c => (Money)c.Amount)).SubtractFees();
        else
            builder.Send(destination, Money.Satoshis(amountSats));

        if (!account.IsWatchOnly)
        {
            builder.AddKeys(spendable
                .Select(u => account.GetPrivateKey(u.IsChange, u.AddressIndex))
                .OfType<Key>()
                .ToArray());
        }

        Transaction tx;
        try
        {
            tx = builder.BuildTransaction(sign: !account.IsWatchOnly);
        }
        catch (NotEnoughFundsException ex)
        {
            throw new WalletSpendException($"Insufficient funds: {ex.Message}");
        }

        if (!account.IsWatchOnly)
        {
            if (!builder.Verify(tx, out TransactionPolicyError[] errors))
                throw new WalletSpendException(
                    "Invalid transaction: " + string.Join("; ", errors.Select(e => e.ToString())));
        }

        return new BuiltTransaction
        {
            Transaction = tx,
            Fee = GetFee(tx, coins),
            FeeRate = feeRate,
            Signed = !account.IsWatchOnly,
            Psbt = builder.BuildPSBT(sign: !account.IsWatchOnly),
        };
    }

    private static Money GetFee(Transaction tx, IReadOnlyList<Coin> coins)
    {
        var spentOutpoints = tx.Inputs.Select(i => i.PrevOut).ToHashSet();
        var inputSum = coins.Where(c => spentOutpoints.Contains(c.Outpoint))
            .Sum(c => (Money)c.Amount);
        return inputSum - tx.Outputs.Sum(o => o.Value);
    }
}

/// <summary>Error during transaction construction/signing (funds, policy, parameters).</summary>
public sealed class WalletSpendException(string message) : Exception(message);
