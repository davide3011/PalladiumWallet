using System.Globalization;

namespace PalladiumWallet.Core.Wallet;

/// <summary>
/// Conversione satoshi ↔ unità coin (8 decimali) per visualizzazione e input.
/// Si lavora sempre in satoshi internamente; la stringa è solo presentazione.
/// </summary>
public static class CoinAmount
{
    public const long SatsPerCoin = 100_000_000;

    public static string Format(long sats, string unit = "") =>
        (sats / (decimal)SatsPerCoin).ToString("0.00000000", CultureInfo.InvariantCulture)
        + (unit.Length > 0 ? " " + unit : "");

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
            sats = (long)(coins * SatsPerCoin);
        }
        catch (OverflowException)
        {
            return false;
        }
        return true;
    }
}
