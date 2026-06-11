using NBitcoin;
using NBitcoin.Policy;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.Core.Wallet;

/// <summary>Esito della costruzione di una transazione.</summary>
public sealed class BuiltTransaction
{
    public required Transaction Transaction { get; init; }
    public required Money Fee { get; init; }
    public required FeeRate FeeRate { get; init; }
    public required bool Signed { get; init; }

    /// <summary>PSBT per i flussi watch-only/air-gapped/multisig (§6.5).</summary>
    public required PSBT Psbt { get; init; }

    public string ToHex() => Transaction.ToHex();
    public string Txid => Transaction.GetHash().ToString();
}

/// <summary>
/// Costruzione e firma delle transazioni (blueprint §6) sopra le primitive
/// NBitcoin: selezione monete (manuale o automatica), fee a rate fisso,
/// invia-tutto con fee sottratta, change sulla catena interna, RBF di default.
/// Con un account watch-only produce la PSBT non firmata (§6.5).
/// </summary>
public sealed class TransactionFactory(HdAccount account)
{
    private Network Network => PalladiumNetworks.For(account.Profile.Kind);

    /// <summary>
    /// Costruisce (e se possibile firma) una transazione.
    /// </summary>
    /// <param name="utxos">UTXO selezionati (coin control §6.2) o tutti quelli spendibili.</param>
    /// <param name="transactions">Tx di provenienza degli UTXO (txid → tx), dalla sincronizzazione.</param>
    /// <param name="destination">Indirizzo destinatario.</param>
    /// <param name="amountSats">Importo; ignorato se <paramref name="sendAll"/>.</param>
    /// <param name="feeRateSatPerVByte">Fee rate fisso in sat/vByte (§6.4).</param>
    /// <param name="changeIndex">Indice del prossimo indirizzo di change (catena interna).</param>
    /// <param name="sendAll">Invia tutto: fee sottratta dall'importo (§6.1).</param>
    public BuiltTransaction Build(
        IReadOnlyList<CachedUtxo> utxos,
        IReadOnlyDictionary<string, Transaction> transactions,
        BitcoinAddress destination,
        long amountSats,
        decimal feeRateSatPerVByte,
        int changeIndex,
        bool sendAll = false)
    {
        var spendable = utxos.Where(u => !u.Frozen).ToList();
        if (spendable.Count == 0)
            throw new WalletSpendException("Nessun UTXO spendibile selezionato.");

        var coins = spendable.Select(u => new Coin(
            new OutPoint(uint256.Parse(u.Txid), (uint)u.Vout),
            transactions[u.Txid].Outputs[u.Vout])).ToList();

        var feeRate = new FeeRate(Money.Satoshis(feeRateSatPerVByte * 1000m), 1000);
        var builder = Network.CreateTransactionBuilder();
        builder.SetVersion(2);
        // Sequence RBF per consentire il bump della fee (§6.6).
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
                .Select(u => account.GetExtPrivateKey(u.IsChange, u.AddressIndex))
                .ToArray());
        }

        Transaction tx;
        try
        {
            tx = builder.BuildTransaction(sign: !account.IsWatchOnly);
        }
        catch (NotEnoughFundsException ex)
        {
            throw new WalletSpendException($"Fondi insufficienti: {ex.Message}");
        }

        if (!account.IsWatchOnly)
        {
            if (!builder.Verify(tx, out TransactionPolicyError[] errors))
                throw new WalletSpendException(
                    "Transazione non valida: " + string.Join("; ", errors.Select(e => e.ToString())));
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

/// <summary>Errore di costruzione/firma della spesa (fondi, policy, parametri).</summary>
public sealed class WalletSpendException(string message) : Exception(message);
