using System.Text.Json;
using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Net;

/// <summary>Known indexing server (bootstrap or discovered from peers).</summary>
public sealed record KnownServer(string Host, int TcpPort, int SslPort, string? Version = null)
{
    public int PortFor(bool useSsl) => useSsl ? SslPort : TcpPort;

    public override string ToString() =>
        $"{Host}  ·  tcp {TcpPort} / ssl {SslPort}" + (Version is null ? "" : $"  ·  v{Version}");
}

/// <summary>
/// Server registry (blueprint §9): starts from the profile bootstrap list (§3),
/// is enriched with peers announced via server.peers.subscribe, and persists
/// discovered servers to file for subsequent sessions.
/// </summary>
public sealed class ServerRegistry
{
    private readonly ChainProfile _profile;
    private readonly string _filePath;
    private readonly List<KnownServer> _servers = [];
    private readonly object _lock = new();

    public ServerRegistry(ChainProfile profile, string filePath)
    {
        _profile = profile;
        _filePath = filePath;

        foreach (var s in profile.BootstrapServers)
            _servers.Add(new KnownServer(s.Host, s.TcpPort, s.SslPort));

        if (File.Exists(filePath))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<List<KnownServer>>(File.ReadAllText(filePath)) ?? [];
                foreach (var s in saved)
                    AddIfNew(s);
            }
            catch (JsonException)
            {
                // Corrupted file: fall back to bootstrap servers only.
            }
        }
    }

    public IReadOnlyList<KnownServer> All
    {
        get { lock (_lock) return [.. _servers]; }
    }

    /// <summary>First known server: default when the user does not specify one.</summary>
    public KnownServer? Default
    {
        get { lock (_lock) return _servers.FirstOrDefault(); }
    }

    /// <summary>
    /// Queries the connected server for its announced peers and adds new ones to the
    /// registry (missing ports fall back to the profile defaults). Returns the count of new entries.
    /// </summary>
    public async Task<int> DiscoverAsync(ElectrumClient client, CancellationToken ct = default)
    {
        var peers = await client.GetPeersAsync(ct);
        var added = 0;
        lock (_lock)
        {
            foreach (var peer in peers)
            {
                var server = new KnownServer(
                    peer.Host,
                    peer.TcpPort is > 0 ? peer.TcpPort.Value : _profile.DefaultTcpPort,
                    peer.SslPort is > 0 ? peer.SslPort.Value : _profile.DefaultSslPort,
                    peer.Version);
                if (AddIfNew(server))
                    added++;
            }
            if (added > 0)
                Save();
        }
        return added;
    }

    private bool AddIfNew(KnownServer server)
    {
        if (_servers.Any(s => string.Equals(s.Host, server.Host, StringComparison.OrdinalIgnoreCase)))
            return false;
        _servers.Add(server);
        return true;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        // Only discovered servers are saved; bootstrap servers come from the profile.
        var discovered = _servers
            .Where(s => !_profile.BootstrapServers.Any(b =>
                string.Equals(b.Host, s.Host, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        File.WriteAllText(_filePath, JsonSerializer.Serialize(discovered,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
