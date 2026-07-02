using System.Collections.Concurrent;
using System.Threading;
using NBitcoin;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Net;
using PalladiumWallet.Core.Storage;
using PalladiumWallet.Core.Wallet;

namespace PalladiumWallet.Core.Spv;

/// <summary>Address derived and tracked during synchronisation.</summary>
public sealed record TrackedAddress(
    BitcoinAddress Address, string ScriptHash, bool IsChange, int Index)
{
    public Script ScriptPubKey => Address.ScriptPubKey;
}

/// <summary>Result of a synchronisation pass.</summary>
public sealed class SyncResult
{
    public required int TipHeight { get; init; }
    public required long ConfirmedSats { get; init; }
    public required long UnconfirmedSats { get; init; }

    /// <summary>
    /// Confirmed but not yet spendable: coinbase outputs below maturity or regular
    /// outputs below <see cref="Chain.ChainProfile.MinConfirmations"/>. Subset of
    /// <see cref="ConfirmedSats"/>.
    /// </summary>
    public required long ImmatureSats { get; init; }
    public required int NextReceiveIndex { get; init; }
    public required int NextChangeIndex { get; init; }
    public required IReadOnlyList<CachedTx> History { get; init; }
    public required IReadOnlyList<CachedUtxo> Utxos { get; init; }
    public required IReadOnlyList<TrackedAddress> Addresses { get; init; }
    public required IReadOnlyList<CachedAddress> AddressRows { get; init; }
    public required IReadOnlyDictionary<string, Transaction> Transactions { get; init; }
}

/// <summary>
/// Wallet synchronisation (blueprint §7.4).
/// </summary>
public sealed class WalletSynchronizer(IWalletAccount account, ElectrumClient client, int gapLimit = 20)
{
    /// <summary>Human-readable progress (for CLI and GUI status bar).</summary>
    public event Action<string>? Progress;

    private readonly ConcurrentDictionary<string, Transaction> _txCache = new();
    private readonly Dictionary<string, int> _verifiedAtHeight = [];
    private readonly ConcurrentDictionary<int, Task<string>> _headerFetches = new();

    // Indices known from the previous sync: used by ScanChainAsync for incremental
    // discovery — already-used addresses are fetched in a single burst instead of
    // sequential batches, reducing round-trips from O(used/gapLimit) to O(1).
    private int _knownReceiveIndex;
    private int _knownChangeIndex;

    /// <summary>
    /// Pre-populates internal caches from data saved on disk.
    /// Call before SyncOnceAsync to avoid re-downloading already known transactions.
    /// </summary>
    public void PreloadCaches(
        Dictionary<string, string> rawTxHex,
        Dictionary<string, int> verifiedAt,
        Dictionary<int, string>? blockHeaders,
        int knownReceiveIndex,
        int knownChangeIndex,
        Network network)
    {
        foreach (var (txid, hex) in rawTxHex)
            _txCache.TryAdd(txid, Transaction.Parse(hex, network));
        foreach (var (txid, height) in verifiedAt)
            if (!_verifiedAtHeight.ContainsKey(txid))
                _verifiedAtHeight[txid] = height;
        if (blockHeaders is not null)
            foreach (var (height, hex) in blockHeaders)
                _headerFetches.TryAdd(height, Task.FromResult(hex));
        _knownReceiveIndex = knownReceiveIndex;
        _knownChangeIndex  = knownChangeIndex;
    }

    /// <summary>
    /// Exports the current caches in a serialisable form for disk storage.
    /// Only confirmed transactions (height > 0) are included: unconfirmed ones
    /// may change (RBF) and must always be re-downloaded.
    /// </summary>
    public (Dictionary<string, string> RawTxHex,
            Dictionary<string, int> VerifiedAt,
            Dictionary<int, string> BlockHeaders)
        ExportCaches(Network network)
    {
        var rawHex = _verifiedAtHeight.Keys
            .Where(_txCache.ContainsKey)
            .ToDictionary(txid => txid, txid => _txCache[txid].ToHex());

        // Only already-completed headers: in-progress Task<string> instances
        // are not persisted (they will be re-fetched on the next sync if needed).
        var headers = new Dictionary<int, string>();
        foreach (var (height, task) in _headerFetches)
            if (task.IsCompletedSuccessfully)
                headers[height] = task.Result;

        return (rawHex, new Dictionary<string, int>(_verifiedAtHeight), headers);
    }

    public async Task<SyncResult> SyncOnceAsync(CancellationToken ct = default)
    {
        var tip = await client.SubscribeHeadersAsync(ct);
        Progress?.Invoke($"chain tip: {tip.Height}");

        // 1-2. Address scanning.
        var tracked = new List<TrackedAddress>();
        var historyByAddress = new Dictionary<string, IReadOnlyList<HistoryItem>>();
        int nextReceive, nextChange;

        if (account.FixedAddresses is { } fixedAddresses)
        {
            foreach (var (addr, isChange, idx) in fixedAddresses)
                tracked.Add(new TrackedAddress(addr, Scripthash.FromAddress(addr), isChange, idx));
            nextReceive = tracked.Count(t => !t.IsChange);
            nextChange  = 0;

            await Task.WhenAll(tracked.Select(t => RetryOnBusyAsync(async () =>
            {
                var h = await client.GetHistoryAsync(t.ScriptHash, ct);
                if (h.Count > 0) historyByAddress[t.ScriptHash] = h;
            }, ct)).Concat(tracked.Select(t =>
                RetryOnBusyAsync(() => client.SubscribeScripthashAsync(t.ScriptHash, ct), ct))));
        }
        else
        {
            // Receive and change chains in parallel (independent by definition).
            // ScanChainAsync uses _knownReceiveIndex/_knownChangeIndex for incremental
            // discovery: already-used addresses are fetched in a single burst.
            var receiveTask = ScanChainAsync(isChange: false, _knownReceiveIndex, ct);
            var changeTask  = ScanChainAsync(isChange: true,  _knownChangeIndex,  ct);
            var rxScan = await receiveTask;
            var chScan = await changeTask;

            tracked.AddRange(rxScan.Tracked);
            tracked.AddRange(chScan.Tracked);
            foreach (var (k, v) in rxScan.History) historyByAddress[k] = v;
            foreach (var (k, v) in chScan.History) historyByAddress[k] = v;
            nextReceive = rxScan.NextIndex;
            nextChange  = chScan.NextIndex;

            var gapAddresses = tracked.Where(t =>
                (!t.IsChange && t.Index >= nextReceive && t.Index < nextReceive + gapLimit) ||
                ( t.IsChange && t.Index >= nextChange  && t.Index < nextChange  + gapLimit)).ToList();
            if (gapAddresses.Count > 0)
                await Task.WhenAll(gapAddresses.Select(t =>
                    RetryOnBusyAsync(() => client.SubscribeScripthashAsync(t.ScriptHash, ct), ct)));
        }

        // 3. Merged history (txid → highest reported height).
        var txHeights = new Dictionary<string, int>();
        foreach (var item in historyByAddress.Values.SelectMany(h => h))
            txHeights[item.TxHash] = item.Height;

        // 4+5. Download missing transactions and verify Merkle proofs in parallel.
        var network  = PalladiumNetworks.For(account.Profile.Kind);
        var missing  = txHeights.Keys.Where(txid => !_txCache.ContainsKey(txid)).ToList();
        var toVerify = txHeights
            .Where(kv => kv.Value > 0
                && (!_verifiedAtHeight.TryGetValue(kv.Key, out var h) || h != kv.Value))
            .ToList();

        if (missing.Count > 0 || toVerify.Count > 0)
        {
            Progress?.Invoke($"downloading {missing.Count} txs, verifying {toVerify.Count} proofs…");
            var dlDone   = 0;
            var merkDone = 0;

            var dlTasks = missing.Select(txid => RetryOnBusyAsync(async () =>
            {
                var raw = await client.GetTransactionAsync(txid, ct);
                _txCache[txid] = Transaction.Parse(raw, network);
                var n = Interlocked.Increment(ref dlDone);
                if (n % 50 == 0 || n == missing.Count)
                    Progress?.Invoke($"tx {n}/{missing.Count}, proofs {merkDone}/{toVerify.Count}…");
            }, ct));

            var merkTasks = toVerify.Select(kv => RetryOnBusyAsync(async () =>
            {
                var (txid, height) = kv;
                var proofTask  = client.GetMerkleAsync(txid, height, ct);
                var headerTask = _headerFetches.GetOrAdd(height,
                    h => client.GetBlockHeaderAsync(h, ct));
                var proof  = await proofTask;
                var header = BlockHeaderInfo.Parse(await headerTask);
                if (!MerkleProof.Verify(
                        uint256.Parse(txid), proof.Pos,
                        proof.Merkle.Select(uint256.Parse), header.MerkleRoot))
                    throw new SpvVerificationException(
                        $"Invalid Merkle proof for {txid} (block {height}): server is not trustworthy.");
                var n = Interlocked.Increment(ref merkDone);
                if (n % 50 == 0 || n == toVerify.Count)
                    Progress?.Invoke($"tx {dlDone}/{missing.Count}, proofs {n}/{toVerify.Count}…");
            }, ct));

            await Task.WhenAll(dlTasks.Concat(merkTasks));
            foreach (var (txid, height) in toVerify)
                _verifiedAtHeight[txid] = height;
        }

        var transactions = txHeights.Keys.ToDictionary(txid => txid, txid => _txCache[txid]);
        var verified     = txHeights.ToDictionary(kv => kv.Key, kv => kv.Value > 0);

        // 6. Local UTXO reconstruction.
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
                    Txid          = txid,
                    Vout          = vout,
                    ValueSats     = output.Value.Satoshi,
                    Address       = addr.Address.ToString(),
                    IsChange      = addr.IsChange,
                    AddressIndex  = addr.Index,
                    Height        = txHeights[txid],
                    IsCoinbase    = tx.IsCoinBase,
                });
            }
        }

        // 7. Delta per history entry.
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
                Txid      = txid,
                Height    = txHeights[txid],
                DeltaSats = received - sentSats,
                Verified  = verified[txid],
            });
        }
        history.Sort((a, b) =>
        {
            var ha = a.Height <= 0 ? int.MaxValue : a.Height;
            var hb = b.Height <= 0 ? int.MaxValue : b.Height;
            return hb.CompareTo(ha);
        });

        var balanceByAddress = utxos
            .GroupBy(u => u.Address)
            .ToDictionary(g => g.Key, g => g.Sum(u => u.ValueSats));
        var addressRows = tracked
            .OrderBy(t => t.IsChange).ThenBy(t => t.Index)
            .Select(t => new CachedAddress
            {
                Address      = t.Address.ToString(),
                IsChange     = t.IsChange,
                Index        = t.Index,
                BalanceSats  = balanceByAddress.GetValueOrDefault(t.Address.ToString()),
                TxCount      = historyByAddress.TryGetValue(t.ScriptHash, out var h) ? h.Count : 0,
            })
            .ToList();

        return new SyncResult
        {
            TipHeight        = tip.Height,
            ConfirmedSats    = utxos.Where(u => u.Height > 0).Sum(u => u.ValueSats),
            UnconfirmedSats  = utxos.Where(u => u.Height <= 0).Sum(u => u.ValueSats),
            ImmatureSats     = utxos.Where(u =>
                u.Height > 0 && u.Confirmations(tip.Height) < u.RequiredConfirmations(account.Profile))
                .Sum(u => u.ValueSats),
            NextReceiveIndex = nextReceive,
            NextChangeIndex  = nextChange,
            History          = history,
            Utxos            = utxos,
            Addresses        = tracked,
            AddressRows      = addressRows,
            Transactions     = transactions,
        };
    }

    /// <summary>
    /// Scans one chain (receiving or change).
    ///
    /// Phase 1 — known addresses (0..fromIndex-1): all GetHistoryAsync calls are
    /// fired in a single parallel burst, with no sequential batching. A wallet
    /// with 100 used addresses costs 1 RTT instead of 5 sequential gap-limit rounds.
    ///
    /// Phase 2 — discovery from fromIndex onwards: gap-limit batching as before,
    /// required to know when to stop.
    /// </summary>
    private async Task<(int NextIndex,
                         List<TrackedAddress> Tracked,
                         Dictionary<string, IReadOnlyList<HistoryItem>> History)>
        ScanChainAsync(bool isChange, int fromIndex, CancellationToken ct)
    {
        var tracked = new List<TrackedAddress>();
        var history = new Dictionary<string, IReadOnlyList<HistoryItem>>();

        // Phase 1: single burst for all already-known addresses.
        if (fromIndex > 0)
        {
            var known = Enumerable.Range(0, fromIndex).Select(i =>
            {
                var addr = account.GetAddress(isChange, i);
                return new TrackedAddress(addr, Scripthash.FromAddress(addr), isChange, i);
            }).ToList();
            tracked.AddRange(known);

            var knownHistories = await Task.WhenAll(
                known.Select(t => RetryOnBusyAsync(
                    () => client.GetHistoryAsync(t.ScriptHash, ct), ct)));
            for (var i = 0; i < known.Count; i++)
                if (knownHistories[i].Count > 0)
                    history[known[i].ScriptHash] = knownHistories[i];
        }

        // Phase 2: gap-limit discovery from fromIndex onwards.
        var consecutiveEmpty = 0;
        var index      = fromIndex;
        var firstUnused = fromIndex;

        while (consecutiveEmpty < gapLimit)
        {
            var batch = Enumerable.Range(index, gapLimit).Select(i =>
            {
                var addr = account.GetAddress(isChange, i);
                return new TrackedAddress(addr, Scripthash.FromAddress(addr), isChange, i);
            }).ToList();
            index += batch.Count;
            tracked.AddRange(batch);

            var histories = await Task.WhenAll(
                batch.Select(t => RetryOnBusyAsync(
                    () => client.GetHistoryAsync(t.ScriptHash, ct), ct)));

            for (var i = 0; i < batch.Count && consecutiveEmpty < gapLimit; i++)
            {
                if (histories[i].Count == 0)
                {
                    consecutiveEmpty++;
                }
                else
                {
                    consecutiveEmpty = 0;
                    firstUnused = batch[i].Index + 1;
                    history[batch[i].ScriptHash] = histories[i];
                }
            }
        }

        return (firstUnused, tracked, history);
    }

    private static async Task RetryOnBusyAsync(Func<Task> op, CancellationToken ct)
    {
        var delay = 200;
        for (var attempt = 0; ; attempt++)
        {
            try   { await op(); return; }
            catch (ElectrumServerException ex)
                when (IsBusy(ex) && attempt < 7)
            {
                await Task.Delay(delay, ct);
                delay = Math.Min(delay * 2, 5_000);
            }
        }
    }

    private static async Task<T> RetryOnBusyAsync<T>(Func<Task<T>> op, CancellationToken ct)
    {
        var delay = 200;
        for (var attempt = 0; ; attempt++)
        {
            try   { return await op(); }
            catch (ElectrumServerException ex)
                when (IsBusy(ex) && attempt < 7)
            {
                await Task.Delay(delay, ct);
                delay = Math.Min(delay * 2, 5_000);
            }
        }
    }

    private static bool IsBusy(ElectrumServerException ex) =>
        ex.Message.Contains("-102") ||
        ex.Message.Contains("-101") ||
        ex.Message.Contains("server busy", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("excessive resource usage", StringComparison.OrdinalIgnoreCase);
}

/// <summary>SPV verification failed: server data contradicts the proofs (§17).</summary>
public sealed class SpvVerificationException(string message) : Exception(message);
