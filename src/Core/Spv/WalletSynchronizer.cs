using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Net;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.Core.Spv;

/// <summary>Indirizzo derivato e tracciato durante la sincronizzazione.</summary>
public sealed record TrackedAddress(
    BitcoinAddress Address, string ScriptHash, bool IsChange, int Index)
{
    public Script ScriptPubKey => Address.ScriptPubKey;
}

/// <summary>Esito di una passata di sincronizzazione.</summary>
public sealed class SyncResult
{
    public required int TipHeight { get; init; }
    public required long ConfirmedSats { get; init; }
    public required long UnconfirmedSats { get; init; }
    public required int NextReceiveIndex { get; init; }
    public required int NextChangeIndex { get; init; }
    public required IReadOnlyList<CachedTx> History { get; init; }
    public required IReadOnlyList<CachedUtxo> Utxos { get; init; }
    public required IReadOnlyList<TrackedAddress> Addresses { get; init; }
    public required IReadOnlyList<CachedAddress> AddressRows { get; init; }
    public required IReadOnlyDictionary<string, Transaction> Transactions { get; init; }
}

/// <summary>
/// Sincronizzazione del wallet (blueprint §7.4): per ogni indirizzo calcola lo
/// scripthash e si sottoscrive; scarica storico e transazioni; verifica ogni tx
/// confermata con la prova di Merkle contro l'header del suo blocco (le risposte
/// del server non sono fidate, §17); ricostruisce localmente UTXO e saldo;
/// estende la scansione fino al gap limit (§5).
/// </summary>
public sealed class WalletSynchronizer(HdAccount account, ElectrumClient client, int gapLimit = 20)
{
    /// <summary>Avanzamento leggibile (per CLI e barra di stato GUI).</summary>
    public event Action<string>? Progress;

    public async Task<SyncResult> SyncOnceAsync(CancellationToken ct = default)
    {
        var tip = await client.SubscribeHeadersAsync(ct);
        Progress?.Invoke($"tip della catena: {tip.Height}");

        // 1-2. Scansione indirizzi con gap limit, per catena receiving e change.
        var tracked = new List<TrackedAddress>();
        var historyByAddress = new Dictionary<string, IReadOnlyList<HistoryItem>>();
        var nextReceive = await ScanChainAsync(isChange: false, tracked, historyByAddress, ct);
        var nextChange = await ScanChainAsync(isChange: true, tracked, historyByAddress, ct);

        // 3. Storico unico (txid → altezza massima riportata).
        var txHeights = new Dictionary<string, int>();
        foreach (var item in historyByAddress.Values.SelectMany(h => h))
            txHeights[item.TxHash] = item.Height;

        // 4. Scarica le transazioni.
        Progress?.Invoke($"scarico {txHeights.Count} transazioni…");
        var network = PalladiumNetworks.For(account.Profile.Kind);
        var transactions = new Dictionary<string, Transaction>();
        foreach (var txid in txHeights.Keys)
        {
            var hex = await client.GetTransactionAsync(txid, ct);
            transactions[txid] = Transaction.Parse(hex, network);
        }

        // 5. Verifica Merkle delle confermate (§7.4 punto 4).
        var verified = new Dictionary<string, bool>();
        foreach (var (txid, height) in txHeights)
        {
            if (height <= 0)
            {
                verified[txid] = false; // in mempool: nessuna prova possibile
                continue;
            }
            var proof = await client.GetMerkleAsync(txid, height, ct);
            var header = BlockHeaderInfo.Parse(await client.GetBlockHeaderAsync(height, ct));
            var ok = MerkleProof.Verify(
                uint256.Parse(txid),
                proof.Pos,
                proof.Merkle.Select(uint256.Parse),
                header.MerkleRoot);
            if (!ok)
                throw new SpvVerificationException(
                    $"Prova di Merkle non valida per {txid} (blocco {height}): server non affidabile.");
            verified[txid] = true;
        }

        // 6. Ricostruzione locale degli UTXO: accrediti = output verso nostri
        //    script; spesi = outpoint consumati da una qualunque tx del wallet.
        var byScript = tracked.ToDictionary(t => t.ScriptPubKey, t => t);
        var spent = transactions.Values
            .SelectMany(tx => tx.Inputs)
            .Select(i => i.PrevOut)
            .ToHashSet();

        var utxos = new List<CachedUtxo>();
        foreach (var (txid, tx) in transactions)
        {
            for (var vout = 0; vout < tx.Outputs.Count; vout++)
            {
                var output = tx.Outputs[vout];
                if (!byScript.TryGetValue(output.ScriptPubKey, out var addr))
                    continue;
                if (spent.Contains(new OutPoint(tx, vout)))
                    continue;
                utxos.Add(new CachedUtxo
                {
                    Txid = txid,
                    Vout = vout,
                    ValueSats = output.Value.Satoshi,
                    Address = addr.Address.ToString(),
                    IsChange = addr.IsChange,
                    AddressIndex = addr.Index,
                    Height = txHeights[txid],
                });
            }
        }

        // 7. Delta per voce di storico (entrate - uscite del wallet).
        var history = new List<CachedTx>();
        foreach (var (txid, tx) in transactions)
        {
            var received = tx.Outputs
                .Where(o => byScript.ContainsKey(o.ScriptPubKey))
                .Sum(o => o.Value.Satoshi);
            var sentSats = tx.Inputs
                .Where(i => transactions.TryGetValue(i.PrevOut.Hash.ToString(), out var prev)
                    && byScript.ContainsKey(prev.Outputs[i.PrevOut.N].ScriptPubKey))
                .Sum(i => transactions[i.PrevOut.Hash.ToString()].Outputs[i.PrevOut.N].Value.Satoshi);
            history.Add(new CachedTx
            {
                Txid = txid,
                Height = txHeights[txid],
                DeltaSats = received - sentSats,
                Verified = verified[txid],
            });
        }
        history.Sort((a, b) =>
        {
            // Non confermate (height<=0) in cima, poi per altezza decrescente.
            var ha = a.Height <= 0 ? int.MaxValue : a.Height;
            var hb = b.Height <= 0 ? int.MaxValue : b.Height;
            return hb.CompareTo(ha);
        });

        // Saldo e numero di transazioni per singolo indirizzo (vista indirizzi).
        var balanceByAddress = utxos
            .GroupBy(u => u.Address)
            .ToDictionary(g => g.Key, g => g.Sum(u => u.ValueSats));
        var addressRows = tracked
            .OrderBy(t => t.IsChange).ThenBy(t => t.Index)
            .Select(t => new CachedAddress
            {
                Address = t.Address.ToString(),
                IsChange = t.IsChange,
                Index = t.Index,
                BalanceSats = balanceByAddress.GetValueOrDefault(t.Address.ToString()),
                TxCount = historyByAddress.TryGetValue(t.ScriptHash, out var h) ? h.Count : 0,
            })
            .ToList();

        return new SyncResult
        {
            TipHeight = tip.Height,
            ConfirmedSats = utxos.Where(u => u.Height > 0).Sum(u => u.ValueSats),
            UnconfirmedSats = utxos.Where(u => u.Height <= 0).Sum(u => u.ValueSats),
            NextReceiveIndex = nextReceive,
            NextChangeIndex = nextChange,
            History = history,
            Utxos = utxos,
            Addresses = tracked,
            AddressRows = addressRows,
            Transactions = transactions,
        };
    }

    /// <summary>
    /// Scansiona una catena (receiving o change) finché trova gapLimit indirizzi
    /// vuoti consecutivi (§5). Ritorna il primo indice non usato.
    /// </summary>
    private async Task<int> ScanChainAsync(bool isChange, List<TrackedAddress> tracked,
        Dictionary<string, IReadOnlyList<HistoryItem>> historyByAddress, CancellationToken ct)
    {
        var consecutiveEmpty = 0;
        var index = 0;
        var firstUnused = 0;
        for (; consecutiveEmpty < gapLimit; index++)
        {
            var address = account.GetAddress(isChange, index);
            var scripthash = Scripthash.FromAddress(address);
            tracked.Add(new TrackedAddress(address, scripthash, isChange, index));

            // La subscribe registra anche la notifica push per i cambi futuri.
            var status = await client.SubscribeScripthashAsync(scripthash, ct);
            if (status is null)
            {
                consecutiveEmpty++;
                continue;
            }

            historyByAddress[scripthash] = await client.GetHistoryAsync(scripthash, ct);
            consecutiveEmpty = 0;
            firstUnused = index + 1;
        }
        return firstUnused;
    }
}

/// <summary>La verifica SPV è fallita: i dati del server contraddicono le prove (§17).</summary>
public sealed class SpvVerificationException(string message) : Exception(message);
