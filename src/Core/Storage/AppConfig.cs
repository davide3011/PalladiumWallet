using System.Text.Json;

namespace PalladiumWallet.Core.Storage;

/// <summary>
/// Configurazione globale dell'applicazione (blueprint §8), separata dai file
/// wallet: lingua, unità di visualizzazione, ecc. Persistita in config.json
/// nella radice dei dati (vale per tutte le reti).
/// </summary>
public sealed class AppConfig
{
    /// <summary>Codice lingua UI.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Unità di visualizzazione degli importi (vedi <see cref="Wallet.CoinAmount.Units"/>).</summary>
    public string Unit { get; set; } = "PLM";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppConfig Load(string? path = null)
    {
        path ??= AppPaths.ConfigPath();
        if (!File.Exists(path))
            return new AppConfig();
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
        }
        catch (JsonException)
        {
            // Config corrotta: si riparte dai default senza bloccare l'avvio.
            return new AppConfig();
        }
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.ConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
