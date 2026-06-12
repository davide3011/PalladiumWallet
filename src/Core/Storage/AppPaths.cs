using PalladiumWallet.Core.Chain;

namespace PalladiumWallet.Core.Storage;

/// <summary>
/// Percorsi dati per piattaforma (blueprint §8). La radice dati può essere:
/// 1. <b>portable</b>: cartella "palladium-data" accanto all'eseguibile;
/// 2. <b>personalizzata</b>: scelta dall'utente al primo avvio e memorizzata in
///    un piccolo file "puntatore" in una posizione di bootstrap fissa;
/// 3. <b>legacy</b>: vecchia posizione (%APPDATA%/PalladiumWallet) se contiene già dati;
/// 4. <b>default</b>: ~/.PalladiumWallet (Linux/macOS) o %ProgramFiles%\PalladiumWallet (Windows).
/// Sotto la radice c'è una sottocartella per rete (config, wallet, header, certificati).
/// </summary>
public static class AppPaths
{
    public const string PortableDirName = "palladium-data";

    /// <summary>Nome cartella applicazione, usato nei vari percorsi.</summary>
    public const string AppDirName = "PalladiumWallet";

    /// <summary>Override esplicito della radice dati (es. CLI --data-dir). Ha priorità su tutto.</summary>
    public static string? OverrideDataRoot { get; set; }

    /// <summary>
    /// Radice dati predefinita, secondo la convenzione di ogni piattaforma:
    /// Windows → %APPDATA%\PalladiumWallet (PascalCase, come Electrum/Bitcoin);
    /// Linux/macOS → ~/.palladium-wallet (dotfolder minuscolo, come ~/.bitcoin).
    /// Per-utente e sempre scrivibile, senza privilegi di amministratore.
    /// </summary>
    public static string DefaultDataRoot()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDirName);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".palladium-wallet");
    }

    /// <summary>File puntatore alla radice dati scelta dall'utente. Vive in una
    /// posizione di bootstrap sempre scrivibile e indipendente dalla radice dati.</summary>
    private static string LocationPointerPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDirName, "data-location");

    private static string PortableRoot() =>
        Path.Combine(AppContext.BaseDirectory, PortableDirName);

    private static bool HasData(string root) =>
        Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any();

    /// <summary>Radice dati effettiva, secondo l'ordine di precedenza documentato in classe.</summary>
    public static string DataRoot()
    {
        if (!string.IsNullOrEmpty(OverrideDataRoot))
            return OverrideDataRoot;

        var portable = PortableRoot();
        if (Directory.Exists(portable))
            return portable;

        if (ReadPointer() is { } custom)
            return custom;

        return DefaultDataRoot();
    }

    /// <summary>
    /// true se la posizione dei dati è già determinata e non serve chiederla
    /// all'utente: modalità portable, override, puntatore già scritto, oppure
    /// dati già presenti nella posizione predefinita.
    /// </summary>
    public static bool IsDataLocationConfigured() =>
        !string.IsNullOrEmpty(OverrideDataRoot)
        || Directory.Exists(PortableRoot())
        || ReadPointer() is not null
        || HasData(DefaultDataRoot());

    /// <summary>Memorizza la radice dati scelta dall'utente e la crea su disco.</summary>
    public static void ConfigureDataLocation(string root)
    {
        root = Path.GetFullPath(root.Trim());
        Directory.CreateDirectory(root);
        var pointer = LocationPointerPath();
        Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
        File.WriteAllText(pointer, root);
    }

    private static string? ReadPointer()
    {
        var pointer = LocationPointerPath();
        if (!File.Exists(pointer))
            return null;
        var path = File.ReadAllText(pointer).Trim();
        return string.IsNullOrEmpty(path) ? null : path;
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

    /// <summary>Tutti i file wallet della rete, ordinati per nome (multi-wallet §8).</summary>
    public static IReadOnlyList<string> WalletFiles(NetKind net)
    {
        var dir = WalletsDir(net);
        return Directory.EnumerateFiles(dir, "*.wallet.json")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string CertificatePinsPath(NetKind net) =>
        Path.Combine(ForNetwork(net), "server-certs.json");

    public static string ServersPath(NetKind net) =>
        Path.Combine(ForNetwork(net), "servers.json");

    public static string ConfigPath() =>
        Path.Combine(DataRoot(), "config.json");
}
