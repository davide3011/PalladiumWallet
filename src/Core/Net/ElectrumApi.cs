using System.Text.Json;

namespace PalladiumWallet.Core.Net;

/// <summary>Voce di storico di uno scripthash (blockchain.scripthash.get_history).</summary>
public readonly record struct HistoryItem(string TxHash, int Height, long FeeSats);

/// <summary>UTXO riportato dal server (blockchain.scripthash.listunspent).</summary>
public readonly record struct UnspentItem(string TxHash, int TxPos, long ValueSats, int Height);

/// <summary>Prova di Merkle (blockchain.transaction.get_merkle).</summary>
public sealed record MerkleProofResponse(int BlockHeight, int Pos, IReadOnlyList<string> Merkle);

/// <summary>Tip della catena notificato da blockchain.headers.subscribe.</summary>
public readonly record struct ChainTip(int Height, string HeaderHex);

/// <summary>
/// Helper tipizzati sui metodi del protocollo (blueprint §10), sopra il
/// trasporto JSON-RPC generico di <see cref="ElectrumClient"/>.
/// </summary>
public static class ElectrumApi
{
    public static async Task<ChainTip> SubscribeHeadersAsync(this ElectrumClient c, CancellationToken ct = default)
    {
        var r = await c.RequestAsync("blockchain.headers.subscribe", ct);
        return new ChainTip(r.GetProperty("height").GetInt32(), r.GetProperty("hex").GetString()!);
    }

    /// <summary>Status corrente dello scripthash (null = mai usato).</summary>
    public static async Task<string?> SubscribeScripthashAsync(this ElectrumClient c, string scripthash,
        CancellationToken ct = default)
    {
        var r = await c.RequestAsync("blockchain.scripthash.subscribe", ct, scripthash);
        return r.ValueKind == JsonValueKind.Null ? null : r.GetString();
    }

    public static async Task<IReadOnlyList<HistoryItem>> GetHistoryAsync(this ElectrumClient c,
        string scripthash, CancellationToken ct = default)
    {
        var r = await c.RequestAsync("blockchain.scripthash.get_history", ct, scripthash);
        return [.. r.EnumerateArray().Select(e => new HistoryItem(
            e.GetProperty("tx_hash").GetString()!,
            e.GetProperty("height").GetInt32(),
            e.TryGetProperty("fee", out var fee) ? fee.GetInt64() : 0))];
    }

    public static async Task<IReadOnlyList<UnspentItem>> ListUnspentAsync(this ElectrumClient c,
        string scripthash, CancellationToken ct = default)
    {
        var r = await c.RequestAsync("blockchain.scripthash.listunspent", ct, scripthash);
        return [.. r.EnumerateArray().Select(e => new UnspentItem(
            e.GetProperty("tx_hash").GetString()!,
            e.GetProperty("tx_pos").GetInt32(),
            e.GetProperty("value").GetInt64(),
            e.GetProperty("height").GetInt32()))];
    }

    public static async Task<string> GetTransactionAsync(this ElectrumClient c, string txid,
        CancellationToken ct = default)
    {
        var r = await c.RequestAsync("blockchain.transaction.get", ct, txid);
        return r.GetString()!;
    }

    public static async Task<MerkleProofResponse> GetMerkleAsync(this ElectrumClient c, string txid,
        int height, CancellationToken ct = default)
    {
        var r = await c.RequestAsync("blockchain.transaction.get_merkle", ct, txid, height);
        return new MerkleProofResponse(
            r.GetProperty("block_height").GetInt32(),
            r.GetProperty("pos").GetInt32(),
            [.. r.GetProperty("merkle").EnumerateArray().Select(m => m.GetString()!)]);
    }

    public static async Task<string> GetBlockHeaderAsync(this ElectrumClient c, int height,
        CancellationToken ct = default)
    {
        var r = await c.RequestAsync("blockchain.block.header", ct, height);
        return r.GetString()!;
    }

    public static async Task<string> BroadcastAsync(this ElectrumClient c, string rawTxHex,
        CancellationToken ct = default)
    {
        var r = await c.RequestAsync("blockchain.transaction.broadcast", ct, rawTxHex);
        return r.GetString()!;
    }

    /// <summary>Fee stimata in coin/kB per il target dato; -1 se il server non sa stimare.</summary>
    public static async Task<decimal> EstimateFeeAsync(this ElectrumClient c, int targetBlocks,
        CancellationToken ct = default)
    {
        var r = await c.RequestAsync("blockchain.estimatefee", ct, targetBlocks);
        return r.GetDecimal();
    }

    public static async Task<decimal> RelayFeeAsync(this ElectrumClient c, CancellationToken ct = default)
    {
        var r = await c.RequestAsync("blockchain.relayfee", ct);
        return r.GetDecimal();
    }

    public static async Task<string> BannerAsync(this ElectrumClient c, CancellationToken ct = default)
    {
        var r = await c.RequestAsync("server.banner", ct);
        return r.GetString()!;
    }

    public static Task PingAsync(this ElectrumClient c, CancellationToken ct = default) =>
        c.RequestAsync("server.ping", ct);
}
