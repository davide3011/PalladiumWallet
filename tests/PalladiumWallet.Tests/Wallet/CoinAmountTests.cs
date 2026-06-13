using PalladiumWallet.Core.Wallet;

namespace PalladiumWallet.Tests.Wallet;

public class CoinAmountTests
{
    // --- importi validi ---

    [Theory]
    [InlineData("1", "sat", 1)]
    [InlineData("0", "sat", 0)]
    [InlineData("1.5", "PLM", 150_000_000)]
    [InlineData("0.00000001", "PLM", 1)]
    [InlineData("1", "PLM", 100_000_000)]
    [InlineData("1.00000", "mPLM", 100_000)]
    [InlineData("1.00", "µPLM", 100)]
    public void Importo_valido_viene_accettato(string input, string unit, long expectedSats)
    {
        Assert.True(CoinAmount.TryParseIn(input, unit, out var sats));
        Assert.Equal(expectedSats, sats);
    }

    // --- importi con troppi decimali: devono essere rifiutati ---

    [Theory]
    [InlineData("1.9", "sat")]          // sat non è divisibile
    [InlineData("0.001", "µPLM")]       // 0.001 × 100 = 0.1 sat
    [InlineData("1.500000001", "PLM")]  // 9 decimali, uno di troppo
    [InlineData("0.000000001", "PLM")]  // sotto al satoshi
    [InlineData("1.1", "sat")]
    public void Importo_con_troppi_decimali_viene_rifiutato(string input, string unit)
    {
        Assert.False(CoinAmount.TryParseIn(input, unit, out _));
    }

    // --- TryParseCoins ---

    [Fact]
    public void TryParseCoins_accetta_precisione_massima()
    {
        Assert.True(CoinAmount.TryParseCoins("0.00000001", out var sats));
        Assert.Equal(1L, sats);
    }

    [Fact]
    public void TryParseCoins_rifiuta_sotto_al_satoshi()
    {
        Assert.False(CoinAmount.TryParseCoins("0.000000001", out _));
    }

    [Fact]
    public void TryParseCoins_rifiuta_importo_negativo()
    {
        Assert.False(CoinAmount.TryParseCoins("-1", out _));
    }
}
