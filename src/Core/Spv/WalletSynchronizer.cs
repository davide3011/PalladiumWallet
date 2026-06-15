using System.Collections.Concurrent;
using System.Threading;
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
public sealed class WalletSynchronizer(IWalletAccount account, ElectrumClient client, int gapLimit = 20)
{
    /// <summary>Avanzamento leggibile (per CLI e barra di stato GUI).</summary>
    public event Action<string>? Progress;

    // Richieste contemporanee verso il server. Troppo alte → -102 "server busy";
    // troppo basse → throughput scarso su storie grandi.
    private const int MaxConcurrent = 20;

    // Cache tra le passate (stesso synchronizer per tutta la vita della
    // connessione): le tx già scaricate e le prove di Merkle già verificate a
    // una data altezza non si rifanno — le risincronizzazioni da notifica
    // costano solo ciò che è cambiato (modello Electrum).
    private readonly Dictionary<string, Transaction> _txCache = [];
    private readonly Dictionary<string, int> _verifiedAtHeight = [];

    // Header grezzi per altezza: una Task<string> per altezza, condivisa tra
    // tutte le tx dello stesso blocco → ogni blocco viene scaricato una sola
    // volta anche con centinaia di tx confermate nello stesso blocco.
    private readonly ConcurrentDictionary<int, Task<string>> _headerFetches = new();

    /// <summary>
    /// Pre-popola le cache interne da dati salvati su disco (SyncCache).
    /// Chiamare prima di SyncOnceAsync per evitare di riscaricale le tx già note.
    /// </summary>
    public void PreloadCaches(Dictionary<string, string> rawTxHex,
        Dictionary<string, int> verifiedAt, Network network)
    {
        foreach (var (txid, hex) in rawTxHex)
            if (!_txCache.ContainsKey(txid))
                _txCache[txid] = Transaction.Parse(hex, network);
        foreach (var (txid, height) in verifiedAt)
            if (!_verifiedAtHeight.ContainsKey(txid))
                _verifiedAtHeight[txid] = height;
    }

    /// <summary>
    /// Esporta le cache correnti in forma serializzabile su disco.
    /// Solo le tx confermate (height > 0) vengono incluse: le non confermate
    /// possono cambiare (RBF) e vanno sempre riscaricate.
    /// </summary>
    public (Dictionary<string, string> RawTxHex, Dictionary<string, int> VerifiedAt)
        ExportCaches(Network network)
    {
        // Includi solo le tx associate a una prova di Merkle verificata
        // (cioè confermate e verificate): sono le uniche immutabili.
        var rawHex = _verifiedAtHeight.Keys
            .Where(_txCache.ContainsKey)
            .ToDictionary(txid => txid, txid => _txCache[txid].ToHex());
        return (rawHex, new Dictionary<string, int>(_verifiedAtHeight));
    }

    public async Task<SyncResult> SyncOnceAsync(CancellationToken ct = default)
    {
        var tip = await client.SubscribeHeadersAsync(ct);
        Progress?.Invoke($"tip della catena: {tip.Height}");

        // 1-2. Scansione indirizzi.
        var tracked = new List<TrackedAddress>();
        var historyByAddress = new Dictionary<string, IReadOnlyList<HistoryItem>>();
        int nextReceive, nextChange;

        if (account.FixedAddresses is { } fixedAddresses)
        {
            // Importati WIF: lista fissa, nessun gap limit.
            // Pochi indirizzi → subscribe diretto per notifiche push.
            foreach (var (addr, isChange, idx) in fixedAddresses)
                tracked.Add(new TrackedAddress(addr, Scripthash.FromAddress(addr), isChange, idx));
            nextReceive = tracked.Count(t => !t.IsChange);
            nextChange = 0;

            var histories = await Task.WhenAll(
                tracked.Select(t => client.GetHistoryAsync(t.ScriptHash, ct)));
            for (var i = 0; i < tracked.Count; i++)
            {
                if (histories[i].Count > 0)
                    historyByAddress[tracked[i].ScriptHash] = histories[i];
            }
            // Subscribe a tutti (pochi): notifiche push per ogni indirizzo importato.
            await Task.WhenAll(tracked.Select(t => client.SubscribeScripthashAsync(t.ScriptHash, ct)));
        }
        else
        {
            // HD: discovery con GetHistoryAsync (senza subscription → no -101 su wallet grandi);
            // subscribe solo al gap window per ricevere notifiche push di nuove tx.
            nextReceive = await ScanChainAsync(isChange: false, tracked, historyByAddress, ct);
            nextChange  = await ScanChainAsync(isChange: true,  tracked, historyByAddress, ct);

            // Iscriviti al gap window (prossimi indirizzi attesi) per notifiche push.
            // In questo modo il numero di subscription è sempre ≤ 2×gapLimit, indipendentemente
            // dalla dimensione dello storico — nessun rischio di -101.
            var gapAddresses = tracked.Where(t =>
                (!t.IsChange && t.Index >= nextReceive && t.Index < nextReceive + gapLimit) ||
                ( t.IsChange && t.Index >= nextChange  && t.Index < nextChange  + gapLimit)).ToList();
            if (gapAddresses.Count > 0)
                await Task.WhenAll(gapAddresses.Select(t => client.SubscribeScripthashAsync(t.ScriptHash, ct)));
        }

        // 3. Storico unico (txid → altezza massima riportata).
        var txHeights = new Dictionary<string, int>();
        foreach (var item in historyByAddress.Values.SelectMany(h => h))
            txHeights[item.TxHash] = item.Height;

        // 4. Scarica le transazioni nuove: semaforo MaxConcurrent per non saturare
        //    il server, con aggiornamento progresso in tempo reale.
        var network = PalladiumNetworks.For(account.Profile.Kind);
        var missing = txHeights.Keys.Where(txid => !_txCache.ContainsKey(txid)).ToList();
        if (missing.Count > 0)
        {
            var dlSem = new SemaphoreSlim(MaxConcurrent, MaxConcurrent);
            var dlDone = 0;
            Progress?.Invoke($"scarico 0/{missing.Count} transazioni…");
            await Task.WhenAll(missing.Select(async txid =>
            {
                await dlSem.WaitAsync(ct);
                try
                {
                    var raw = await client.GetTransactionAsync(txid, ct);
                    _txCache[txid] = Transaction.Parse(raw, network);
                    var n = Interlocked.Increment(ref dlDone);
                    Progress?.Invoke($"scarico {n}/{missing.Count} transazioni…");
                }
                finally { dlSem.Release(); }
            }));
        }
        var transactions = txHeights.Keys.ToDictionary(txid => txid, txid => _txCache[txid]);

        // 5. Verifica Merkle delle confermate (§7.4 punto 4).
        //    Gli header per altezza sono condivisi via _headerFetches: se 500 tx
        //    stanno nello stesso blocco, l'header viene scaricato una sola volta.
        var toVerify = txHeights
            .Where(kv => kv.Value > 0
                && (!_verifiedAtHeight.TryGetValue(kv.Key, out var h) || h != kv.Value))
            .ToList();
        if (toVerify.Count > 0)
        {
            var merkSem = new SemaphoreSlim(MaxConcurrent, MaxConcurrent);
            var merkDone = 0;
            Progress?.Invoke($"verifico 0/{toVerify.Count} prove di Merkle…");
            await Task.WhenAll(toVerify.Select(async kv =>
            {
                await merkSem.WaitAsync(ct);
                try
                {
                    var (txid, height) = kv;
                    // Proof e header in parallelo; l'header è condiviso per altezza.
                    var proofTask   = client.GetMerkleAsync(txid, height, ct);
                    var headerTask  = _headerFetches.GetOrAdd(height,
                        h => client.GetBlockHeaderAsync(h, ct));
                    var proof  = await proofTask;
                    var header = BlockHeaderInfo.Parse(await headerTask);
                    if (!MerkleProof.Verify(
                            uint256.Parse(txid), proof.Pos,
                            proof.Merkle.Select(uint256.Parse), header.MerkleRoot))
                        throw new SpvVerificationException(
                            $"Prova di Merkle non valida per {txid} (blocco {height}): server non affidabile.");
                    var n = Interlocked.Increment(ref merkDone);
                    Progress?.Invoke($"verifico {n}/{toVerify.Count} prove di Merkle…");
                }
                finally { merkSem.Release(); }
            }));
            foreach (var (txid, height) in toVerify)
                _verifiedAtHeight[txid] = height;
        }
        var verified = txHeights.ToDictionary(kv => kv.Key, kv => kv.Value > 0);

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
    /// vuoti consecutivi (§5), procedendo a batch paralleli di gapLimit per volta.
    /// Usa GetHistoryAsync per la discovery — senza subscription → nessun rischio di
    /// -101 "excessive resource usage" su wallet con molti indirizzi storici.
    /// Le subscription per notifiche push vengono gestite dal chiamante (solo gap window).
    /// Ritorna il primo indice non usato.
    /// </summary>
    private async Task<int> ScanChainAsync(bool isChange, List<TrackedAddress> tracked,
        Dictionary<string, IReadOnlyList<HistoryItem>> historyByAddress, CancellationToken ct)
    {
        var consecutiveEmpty = 0;
        var index = 0;
        var firstUnused = 0;
        while (consecutiveEmpty < gapLimit)
        {
            var batch = Enumerable.Range(index, gapLimit).Select(i =>
            {
                var address = account.GetAddress(isChange, i);
                return new TrackedAddress(address, Scripthash.FromAddress(address), isChange, i);
            }).ToList();
            index += batch.Count;
            tracked.AddRange(batch);

            // GetHistoryAsync per discovery: risposta vuota [] se inutilizzato,
            // lista di tx se usato — un solo round-trip per indirizzo.
            var histories = await Task.WhenAll(
                batch.Select(t => client.GetHistoryAsync(t.ScriptHash, ct)));

            for (var i = 0; i < batch.Count && consecutiveEmpty < gapLimit; i++)
            {
                var history = histories[i];
                if (history.Count == 0)
                {
                    consecutiveEmpty++;
                }
                else
                {
                    consecutiveEmpty = 0;
                    firstUnused = batch[i].Index + 1;
                    historyByAddress[batch[i].ScriptHash] = history;
                }
            }
        }
        return firstUnused;
    }
}

/// <summary>La verifica SPV è fallita: i dati del server contraddicono le prove (§17).</summary>
public sealed class SpvVerificationException(string message) : Exception(message);
