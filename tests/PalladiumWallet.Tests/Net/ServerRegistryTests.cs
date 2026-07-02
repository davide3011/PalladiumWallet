using System.Text.Json;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Net;

namespace PalladiumWallet.Tests.Net;

public class PeerParsingTests
{
    [Fact]
    public void La_risposta_peers_subscribe_si_parsa_nel_formato_electrumx()
    {
        // Real format: [ip, hostname, ["v...", "pN", "tPORT", "sPORT"]].
        const string json = """
            [
              ["173.212.224.67", "173.212.224.67", ["v1.4.2", "p10000", "t50001", "s50002"]],
              ["10.0.0.1", "nodo.esempio.org", ["v1.4", "t"]],
              ["10.0.0.2", "solo-ssl.esempio.org", ["v1.4", "s50002"]],
              ["10.0.0.3", "senza-porte.esempio.org", ["v1.4"]]
            ]
            """;
        var peers = ElectrumApi.ParsePeers(JsonDocument.Parse(json).RootElement);

        Assert.Equal(3, peers.Count); // last entry has no ports → discarded

        Assert.Equal(new PeerInfo("173.212.224.67", 50001, 50002, "1.4.2"), peers[0]);
        // "t" without a number = default port (0 signals "resolve from profile").
        Assert.Equal(new PeerInfo("nodo.esempio.org", 0, null, "1.4"), peers[1]);
        Assert.Equal(new PeerInfo("solo-ssl.esempio.org", null, 50002, "1.4"), peers[2]);
    }
}

public class ServerRegistryTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"plm-servers-{Guid.NewGuid()}.json");

    [Fact]
    public void Il_registro_parte_dai_bootstrap_del_profilo()
    {
        var path = TempPath();
        var registry = new ServerRegistry(ChainProfiles.Mainnet, path);

        Assert.Equal(ChainProfiles.Mainnet.BootstrapServers.Count, registry.All.Count);
        Assert.Equal("173.212.224.67", registry.Default!.Host);
        Assert.Equal(50001, registry.Default.PortFor(useSsl: false));
        Assert.Equal(50002, registry.Default.PortFor(useSsl: true));
    }

    [Fact]
    public void I_peer_scoperti_si_aggiungono_e_persistono_senza_duplicati()
    {
        var path = TempPath();
        try
        {
            var registry = new ServerRegistry(ChainProfiles.Mainnet, path);
            var bootstrapCount = registry.All.Count;

            // Direct merge via DiscoverAsync requires a client: test persistence
            // by simulating the discovered servers file.
            var discovered = new[] { new KnownServer("nuovo.esempio.org", 50001, 50002, "1.4.2") };
            File.WriteAllText(path, JsonSerializer.Serialize(discovered));

            var reloaded = new ServerRegistry(ChainProfiles.Mainnet, path);
            Assert.Equal(bootstrapCount + 1, reloaded.All.Count);
            Assert.Contains(reloaded.All, s => s.Host == "nuovo.esempio.org");

            // A bootstrap duplicate in the file must not double-count.
            File.WriteAllText(path, JsonSerializer.Serialize(new[]
            {
                new KnownServer("173.212.224.67", 50001, 50002),
                new KnownServer("nuovo.esempio.org", 50001, 50002),
            }));
            var deduped = new ServerRegistry(ChainProfiles.Mainnet, path);
            Assert.Equal(bootstrapCount + 1, deduped.All.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task La_discovery_aggiunge_i_peer_annunciati_e_li_persiste()
    {
        var path = TempPath();
        try
        {
            await using var server = new FakeElectrumServer();
            server.Handle("server.peers.subscribe", _ => new object[]
            {
                // Full announcement, port-less announcement (→ profile defaults),
                // and a bootstrap duplicate that must not be re-added.
                new object[] { "10.0.0.9", "nuovo.esempio.org", new[] { "v1.4.2", "t50001", "s50002" } },
                new object[] { "10.0.0.8", "senza-porte.esempio.org", new[] { "v1.4", "t", "s" } },
                new object[] { "173.212.224.67", "173.212.224.67", new[] { "v1.4", "t50001" } },
            });
            await using var client = await ElectrumClient.ConnectAsync(server.Host, server.Port, useSsl: false);

            var registry = new ServerRegistry(ChainProfiles.Mainnet, path);
            var bootstrapCount = registry.All.Count;

            var added = await registry.DiscoverAsync(client);

            Assert.Equal(2, added);
            Assert.Equal(bootstrapCount + 2, registry.All.Count);
            var defaulted = registry.All.Single(s => s.Host == "senza-porte.esempio.org");
            Assert.Equal(ChainProfiles.Mainnet.DefaultTcpPort, defaulted.TcpPort);
            Assert.Equal(ChainProfiles.Mainnet.DefaultSslPort, defaulted.SslPort);

            // Persisted for the next session (bootstrap servers excluded from the file).
            var reloaded = new ServerRegistry(ChainProfiles.Mainnet, path);
            Assert.Equal(bootstrapCount + 2, reloaded.All.Count);
            var savedJson = File.ReadAllText(path);
            Assert.DoesNotContain("173.212.224.67", savedJson);

            // A second discovery of the same peers adds nothing.
            Assert.Equal(0, await registry.DiscoverAsync(client));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Un_file_server_corrotto_non_blocca_l_avvio()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ non-json ");
            var registry = new ServerRegistry(ChainProfiles.Mainnet, path);
            Assert.Equal(ChainProfiles.Mainnet.BootstrapServers.Count, registry.All.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
