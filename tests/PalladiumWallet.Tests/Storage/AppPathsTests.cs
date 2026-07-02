using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.Tests.Storage;

/// <summary>
/// Tests for the data-path resolution (§8), pinned to a temporary root via
/// <see cref="AppPaths.OverrideDataRoot"/> — the same seam the Android head and
/// the CLI --data-dir use — so nothing outside the temp folder is touched.
/// The pointer-file and portable-mode branches read machine-global locations
/// and are deliberately not exercised here.
/// </summary>
public class AppPathsTests : IDisposable
{
    private readonly string _root;
    private readonly string? _savedOverride;

    public AppPathsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"plm-data-{Guid.NewGuid()}");
        _savedOverride = AppPaths.OverrideDataRoot;
        AppPaths.OverrideDataRoot = _root;
    }

    public void Dispose()
    {
        AppPaths.OverrideDataRoot = _savedOverride;
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void L_override_ha_priorita_su_tutto()
    {
        Assert.Equal(_root, AppPaths.DataRoot());
        Assert.True(AppPaths.IsDataLocationConfigured());
    }

    [Fact]
    public void Ogni_rete_ha_la_propria_sottocartella_con_il_nome_del_profilo()
    {
        foreach (var net in new[] { NetKind.Mainnet, NetKind.Testnet, NetKind.Regtest })
        {
            var dir = AppPaths.ForNetwork(net);
            Assert.Equal(Path.Combine(_root, ChainProfiles.For(net).NetName), dir);
            Assert.True(Directory.Exists(dir));
        }
    }

    [Fact]
    public void I_percorsi_dei_file_di_rete_stanno_sotto_la_cartella_della_rete()
    {
        var netDir = AppPaths.ForNetwork(NetKind.Mainnet);

        Assert.Equal(Path.Combine(netDir, "server-certs.json"), AppPaths.CertificatePinsPath(NetKind.Mainnet));
        Assert.Equal(Path.Combine(netDir, "servers.json"), AppPaths.ServersPath(NetKind.Mainnet));
        Assert.Equal(Path.Combine(netDir, "wallets", "default.wallet.json"),
            AppPaths.DefaultWalletPath(NetKind.Mainnet));
        Assert.Equal(Path.Combine(_root, "config.json"), AppPaths.ConfigPath());
    }

    [Fact]
    public void WalletFiles_elenca_solo_i_wallet_in_ordine_alfabetico()
    {
        var dir = AppPaths.WalletsDir(NetKind.Regtest);
        File.WriteAllText(Path.Combine(dir, "zeta.wallet.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "alfa.wallet.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "non-un-wallet.txt"), "x");
        File.WriteAllText(Path.Combine(dir, "default.wallet.json.lock"), "x");

        var files = AppPaths.WalletFiles(NetKind.Regtest);

        Assert.Equal(2, files.Count);
        Assert.EndsWith("alfa.wallet.json", files[0]);
        Assert.EndsWith("zeta.wallet.json", files[1]);
    }

    [Fact]
    public void Le_reti_non_condividono_le_cartelle_wallet()
    {
        Assert.NotEqual(AppPaths.WalletsDir(NetKind.Mainnet), AppPaths.WalletsDir(NetKind.Testnet));
        Assert.NotEqual(AppPaths.WalletsDir(NetKind.Testnet), AppPaths.WalletsDir(NetKind.Regtest));
    }
}
