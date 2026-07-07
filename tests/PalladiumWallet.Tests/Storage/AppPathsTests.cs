using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.Tests.Storage;

/// <summary>
/// Tests for the data-path resolution (§8), pinned to a temporary root via
/// <see cref="AppPaths.OverrideDataRoot"/> — the same seam the Android head and
/// the CLI --data-dir use — so nothing outside the temp folder is touched.
/// The pointer/portable/default precedence has its own sandboxed tests in
/// <see cref="AppPathsResolutionTests"/>; both classes share a collection
/// because AppPaths state is static.
/// </summary>
[Collection("AppPaths")]
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

/// <summary>
/// Precedence tests for <see cref="AppPaths.DataRoot"/> — override → portable →
/// pointer → default — using the internal bootstrap seams to sandbox the
/// machine-global locations (APPDATA pointer dir, executable dir, default root).
/// </summary>
[Collection("AppPaths")]
public sealed class AppPathsResolutionTests : IDisposable
{
    private readonly string _sandbox;
    private readonly string? _savedOverride;

    public AppPathsResolutionTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), $"plm-paths-{Guid.NewGuid()}");
        Directory.CreateDirectory(_sandbox);
        _savedOverride = AppPaths.OverrideDataRoot;
        AppPaths.OverrideDataRoot = null;
        AppPaths.BootstrapDirOverride = Path.Combine(_sandbox, "bootstrap");
        AppPaths.PortableBaseOverride = Path.Combine(_sandbox, "exe");
        AppPaths.DefaultRootOverride = Path.Combine(_sandbox, "default-root");
        Directory.CreateDirectory(AppPaths.PortableBaseOverride);
    }

    public void Dispose()
    {
        AppPaths.OverrideDataRoot = _savedOverride;
        AppPaths.BootstrapDirOverride = null;
        AppPaths.PortableBaseOverride = null;
        AppPaths.DefaultRootOverride = null;
        if (Directory.Exists(_sandbox))
            Directory.Delete(_sandbox, recursive: true);
    }

    private string PortableDir => Path.Combine(_sandbox, "exe", AppPaths.PortableDirName);

    [Fact]
    public void Senza_alcuna_configurazione_vince_il_default_e_la_posizione_non_e_configurata()
    {
        Assert.Equal(Path.Combine(_sandbox, "default-root"), AppPaths.DataRoot());
        Assert.False(AppPaths.IsDataLocationConfigured());
    }

    [Fact]
    public void La_cartella_portable_accanto_all_eseguibile_vince_sul_pointer_e_sul_default()
    {
        AppPaths.ConfigureDataLocation(Path.Combine(_sandbox, "custom"));
        Directory.CreateDirectory(PortableDir);

        Assert.Equal(PortableDir, AppPaths.DataRoot());
        Assert.True(AppPaths.IsDataLocationConfigured());
    }

    [Fact]
    public void Il_pointer_scritto_da_ConfigureDataLocation_vince_sul_default()
    {
        var custom = Path.Combine(_sandbox, "custom");
        AppPaths.ConfigureDataLocation($"  {custom}  "); // trims and creates

        Assert.True(Directory.Exists(custom));
        Assert.Equal(custom, AppPaths.DataRoot());
        Assert.True(AppPaths.IsDataLocationConfigured());
    }

    [Fact]
    public void Un_pointer_vuoto_viene_ignorato_e_si_ricade_sul_default()
    {
        Directory.CreateDirectory(AppPaths.BootstrapDirOverride!);
        File.WriteAllText(Path.Combine(AppPaths.BootstrapDirOverride!, "data-location"), "   ");

        Assert.Equal(Path.Combine(_sandbox, "default-root"), AppPaths.DataRoot());
        Assert.False(AppPaths.IsDataLocationConfigured());
    }

    [Fact]
    public void Dati_gia_presenti_nel_default_contano_come_posizione_configurata()
    {
        var defaultRoot = Path.Combine(_sandbox, "default-root");
        Directory.CreateDirectory(defaultRoot);
        Assert.False(AppPaths.IsDataLocationConfigured()); // exists but empty

        File.WriteAllText(Path.Combine(defaultRoot, "config.json"), "{}");
        Assert.True(AppPaths.IsDataLocationConfigured());
    }

    [Fact]
    public void L_override_esplicito_vince_anche_su_portable_e_pointer()
    {
        Directory.CreateDirectory(PortableDir);
        AppPaths.ConfigureDataLocation(Path.Combine(_sandbox, "custom"));
        AppPaths.OverrideDataRoot = Path.Combine(_sandbox, "override");

        Assert.Equal(Path.Combine(_sandbox, "override"), AppPaths.DataRoot());
    }
}
