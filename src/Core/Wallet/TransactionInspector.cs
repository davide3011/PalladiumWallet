using NBitcoin;
using PalladiumWallet.Core.Net;
using PalladiumWallet.Core.Spv;

namespace PalladiumWallet.Core.Wallet;

/// <summary>Un input di una transazione, con l'output speso risolto dal server.</summary>
public sealed record TxInputInfo(
    string PrevTxid, uint PrevIndex, long? AmountSats, string? Address, bool IsMine, bool IsCoinbase);

/// <summary>Un output di una transazione.</summary>
public sealed record TxOutputInfo(
    uint Index, long AmountSats, string? Address, string ScriptType, bool IsMine);

/// <summary>
/// Dati completi di una transazione, assemblati interrogando il server: la tx
/// grezza più gli output spesi dagli input (per ricavare importi, indirizzi e
/// fee) e l'header del blocco (per la data). Tutto ciò che il protocollo
/// ElectrumX-like (§10) permette di sapere su una transazione.
/// </summary>
public sealed class TransactionDetails
{
    public required string Txid { get; init; }
    /// <summary>Altezza del blocco; ≤0 = ancora in mempool.</summary>
    public required int Height { get; init; }
    public required int Confirmations { get; init; }
    /// <summary>Effetto netto sul saldo del wallet (delta calcolato in sincronizzazione).</summary>
    public required long NetSats { get; init; }
    /// <summary>Fee della transazione; null se un input ha importo non risolvibile (es. coinbase).</summary>
    public required long? FeeSats { get; init; }
    public required int TotalSize { get; init; }
    public required int VirtualSize { get; init; }
    public required uint Version { get; init; }
    public required uint LockTime { get; init; }
    public required bool RbfSignaled { get; init; }
    /// <summary>Merkle proof verificata in sincronizzazione (§7.4).</summary>
    public required bool Verified { get; init; }
    public required DateTimeOffset? BlockTime { get; init; }
    public required long TotalOutSats { get; init; }
    public required long? TotalInSats { get; init; }
    public required IReadOnlyList<TxInputInfo> Inputs { get; init; }
    public required IReadOnlyList<TxOutputInfo> Outputs { get; init; }

    public bool IsCoinbase => Inputs.Count > 0 && Inputs[0].IsCoinbase;
    public bool IsIncoming => NetSats >= 0;
    /// <summary>Importo verso destinatari esterni (output non nostri): l'importo "inviato".</summary>
    public long SentToOthersSats => Outputs.Where(o => !o.IsMine).Sum(o => o.AmountSats);
    public long ReceivedSats => Outputs.Where(o => o.IsMine).Sum(o => o.AmountSats);
    public double? FeeRateSatPerVb => FeeSats is { } f && VirtualSize > 0 ? (double)f / VirtualSize : null;
    /// <summary>
    /// Indirizzi della controparte: i destinatari esterni per un invio (output non
    /// nostri), i mittenti esterni per una ricezione (input non nostri).
    /// </summary>
    public IReadOnlyList<string> CounterpartyAddresses => IsIncoming
        ? [.. Inputs.Where(i => !i.IsMine && i.Address is not null).Select(i => i.Address!).Distinct()]
        : [.. Outputs.Where(o => !o.IsMine && o.Address is not null).Select(o => o.Address!).Distinct()];
}

/// <summary>
/// Recupera dal server tutti i dati di una singola transazione (blueprint §10):
/// la transazione grezza e gli output spesi dai suoi input, per ricostruire
/// importi, fee e indirizzi che il server non riassume.
/// </summary>
public static class TransactionInspector
{
    public static async Task<TransactionDetails> FetchAsync(
        ElectrumClient client, Network network, string txid, int tipHeight, int height,
        IReadOnlySet<string> ownedAddresses, long netSats, bool verified,
        IReadOnlyDictionary<string, Transaction>? cache = null,
        CancellationToken ct = default)
    {
        async Task<Transaction> GetTx(string id)
        {
            if (cache is not null && cache.TryGetValue(id, out var hit))
                return hit;
            return Transaction.Parse(await client.GetTransactionAsync(id, ct), network);
        }

        async Task<Transaction?> GetTxOrNull(string id)
        {
            try { return await GetTx(id); }
            catch { return null; }
        }

        async Task<DateTimeOffset?> GetBlockTimeOrNull()
        {
            try
            {
                var header = BlockHeaderInfo.Parse(await client.GetBlockHeaderAsync(height, ct));
                return DateTimeOffset.FromUnixTimeSeconds(header.Timestamp);
            }
            catch { return null; }
        }

        string? AddrOf(Script s)
        {
            try { return s.GetDestinationAddress(network)?.ToString(); }
            catch { return null; }
        }

        var tx = await GetTx(txid);

        var outputs = new List<TxOutputInfo>(tx.Outputs.Count);
        for (var i = 0; i < tx.Outputs.Count; i++)
        {
            var o = tx.Outputs[i];
            var addr = AddrOf(o.ScriptPubKey);
            outputs.Add(new TxOutputInfo(
                (uint)i, o.Value.Satoshi, addr, ScriptType(o.ScriptPubKey),
                addr is not null && ownedAddresses.Contains(addr)));
        }

        var rbf = tx.Inputs.Any(i => i.Sequence.IsRBF);

        // Le transazioni degli input servono per importi/indirizzi/fee. Si
        // scaricano in parallelo (id univoci, richieste concorrenti supportate
        // da ElectrumClient): in sequenza la finestra impiegava un round-trip
        // per input. Anche l'header del blocco è recuperato in parallelo.
        var prevTxids = tx.IsCoinBase
            ? []
            : tx.Inputs.Select(i => i.PrevOut.Hash.ToString()).Distinct().ToList();

        var prevFetch = prevTxids.ToDictionary(id => id, id => GetTxOrNull(id));
        var headerTask = height > 0 ? GetBlockTimeOrNull() : Task.FromResult<DateTimeOffset?>(null);
        await Task.WhenAll(prevFetch.Values.Cast<Task>().Append(headerTask));

        var prevTxs = prevFetch.ToDictionary(kv => kv.Key, kv => kv.Value.Result);
        var blockTime = await headerTask;

        var inputs = new List<TxInputInfo>(tx.Inputs.Count);
        var feeKnown = !tx.IsCoinBase;
        long inSum = 0;
        foreach (var inp in tx.Inputs)
        {
            if (tx.IsCoinBase)
            {
                inputs.Add(new TxInputInfo("", inp.PrevOut.N, null, null, false, true));
                continue;
            }

            long? amt = null;
            string? addr = null;
            var prev = prevTxs.GetValueOrDefault(inp.PrevOut.Hash.ToString());
            if (prev is not null && inp.PrevOut.N < prev.Outputs.Count)
            {
                var po = prev.Outputs[(int)inp.PrevOut.N];
                amt = po.Value.Satoshi;
                addr = AddrOf(po.ScriptPubKey);
                inSum += po.Value.Satoshi;
            }
            else feeKnown = false;

            inputs.Add(new TxInputInfo(
                inp.PrevOut.Hash.ToString(), inp.PrevOut.N, amt, addr,
                addr is not null && ownedAddresses.Contains(addr), false));
        }

        var outSum = tx.Outputs.Sum(o => o.Value.Satoshi);

        return new TransactionDetails
        {
            Txid = txid,
            Height = height,
            Confirmations = height > 0 && tipHeight >= height ? tipHeight - height + 1 : 0,
            NetSats = netSats,
            FeeSats = feeKnown ? inSum - outSum : null,
            TotalSize = tx.ToBytes().Length,
            VirtualSize = tx.GetVirtualSize(),
            Version = tx.Version,
            LockTime = tx.LockTime.Value,
            RbfSignaled = rbf,
            Verified = verified,
            BlockTime = blockTime,
            TotalOutSats = outSum,
            TotalInSats = feeKnown ? inSum : null,
            Inputs = inputs,
            Outputs = outputs,
        };
    }

    private static string ScriptType(Script script)
    {
        try
        {
            var t = StandardScripts.GetTemplateFromScriptPubKey(script);
            return t is null
                ? "nonstandard"
                : t.GetType().Name.Replace("PayTo", "").Replace("Template", "");
        }
        catch { return "—"; }
    }
}
