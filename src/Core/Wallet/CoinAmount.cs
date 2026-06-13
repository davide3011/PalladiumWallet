using System.Globalization;

namespace PalladiumWallet.Core.Wallet;

/// <summary>
/// Conversione satoshi ↔ unità coin (8 decimali) per visualizzazione e input.
/// Si lavora sempre in satoshi internamente; la stringa è solo presentazione.
/// </summary>
public static class CoinAmount
{
    public const long SatsPerCoin = 100_000_000;

    /// <summary>Unità di visualizzazione selezionabili (config §8).</summary>
    public static readonly string[] Units = ["PLM", "mPLM", "µPLM", "sat"];

    /// <summary>(satoshi per unità, decimali mostrati) di ciascuna unità.</summary>
    private static (long Factor, int Decimals) Of(string unit) => unit switch
    {
        "mPLM" => (100_000, 5),
        "µPLM" => (100, 2),
        "sat" => (1, 0),
        _ => (SatsPerCoin, 8), // PLM
    };

    public static string Format(long sats, string unit = "") =>
        (sats / (decimal)SatsPerCoin).ToString("0.00000000", CultureInfo.InvariantCulture)
        + (unit.Length > 0 ? " " + unit : "");

    /// <summary>Formatta nell'unità scelta (es. 150000 sat → "1.50000 mPLM").</summary>
    public static string FormatIn(long sats, string unit, bool withLabel = true)
    {
        var (factor, decimals) = Of(unit);
        var value = (sats / (decimal)factor).ToString(
            decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture);
        return withLabel ? $"{value} {unit}" : value;
    }

    /// <summary>Parsa un importo espresso nell'unità scelta in satoshi.</summary>
    public static bool TryParseIn(string text, string unit, out long sats)
    {
        sats = 0;
        var (factor, _) = Of(unit);
        text = text.Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            || value < 0)
            return false;
        try
        {
            var satsDecimal = value * factor;
            if (satsDecimal % 1 != 0)
                return false;
            sats = (long)satsDecimal;
        }
        catch (OverflowException)
        {
            return false;
        }
        return true;
    }

    /// <summary>Parsa un importo in coin (punto o virgola decimale) in satoshi.</summary>
    public static bool TryParseCoins(string text, out long sats)
    {
        sats = 0;
        text = text.Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var coins)
            || coins < 0)
            return false;
        try
        {
            var satsDecimal = coins * SatsPerCoin;
            if (satsDecimal % 1 != 0)
                return false;
            sats = (long)satsDecimal;
        }
        catch (OverflowException)
        {
            return false;
        }
        return true;
    }
}
