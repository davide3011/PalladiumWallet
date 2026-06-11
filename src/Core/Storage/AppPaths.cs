using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Storage;

/// <summary>
/// Percorsi dati per piattaforma (blueprint §8): ~/.palladium-wallet (Linux) o
/// %APPDATA%/PalladiumWallet (Windows), con sottocartella per rete. La modalità
/// portable (dati accanto all'eseguibile) si attiva se accanto all'eseguibile
/// esiste una cartella "palladium-data".
/// </summary>
public static class AppPaths
{
    public const string PortableDirName = "palladium-data";

    public static string DataRoot()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, PortableDirName);
        if (Directory.Exists(portable))
            return portable;

        // Windows → %APPDATA%\PalladiumWallet
        // Linux   → $XDG_CONFIG_HOME/PalladiumWallet  (default ~/.config/PalladiumWallet)
        // macOS   → ~/Library/Application Support/PalladiumWallet
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PalladiumWallet");
    }

    /// <summary>Cartella dati della rete (config, wallet, header, certificati).</summary>
    public static string ForNetwork(NetKind net)
    {
        var dir = Path.Combine(DataRoot(), ChainProfiles.For(net).NetName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string WalletsDir(NetKind net)
    {
        var dir = Path.Combine(ForNetwork(net), "wallets");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string DefaultWalletPath(NetKind net) =>
        Path.Combine(WalletsDir(net), "default.wallet.json");

    public static string CertificatePinsPath(NetKind net) =>
        Path.Combine(ForNetwork(net), "server-certs.json");

    public static string ServersPath(NetKind net) =>
        Path.Combine(ForNetwork(net), "servers.json");

    public static string ConfigPath() =>
        Path.Combine(DataRoot(), "config.json");
}
