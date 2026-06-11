using System.Text.Json;
using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Net;

/// <summary>Server di indicizzazione noto (bootstrap o scoperto dai peer).</summary>
public sealed record KnownServer(string Host, int TcpPort, int SslPort, string? Version = null)
{
    public int PortFor(bool useSsl) => useSsl ? SslPort : TcpPort;

    public override string ToString() =>
        $"{Host}  ·  tcp {TcpPort} / ssl {SslPort}" + (Version is null ? "" : $"  ·  v{Version}");
}

/// <summary>
/// Registro dei server (blueprint §9): parte dai bootstrap del profilo (§3),
/// si arricchisce con i peer annunciati via server.peers.subscribe e persiste
/// le scoperte su file per le sessioni successive.
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
                // File corrotto: si riparte dai soli bootstrap.
            }
        }
    }

    public IReadOnlyList<KnownServer> All
    {
        get { lock (_lock) return [.. _servers]; }
    }

    /// <summary>Primo server noto: default quando l'utente non ne indica uno.</summary>
    public KnownServer? Default
    {
        get { lock (_lock) return _servers.FirstOrDefault(); }
    }

    /// <summary>
    /// Chiede al server connesso i peer che annuncia e integra i nuovi nel
    /// registro (porte mancanti → default del profilo). Ritorna quanti sono nuovi.
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
        // Si salvano solo gli scoperti: i bootstrap vengono dal profilo.
        var discovered = _servers
            .Where(s => !_profile.BootstrapServers.Any(b =>
                string.Equals(b.Host, s.Host, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        File.WriteAllText(_filePath, JsonSerializer.Serialize(discovered,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
