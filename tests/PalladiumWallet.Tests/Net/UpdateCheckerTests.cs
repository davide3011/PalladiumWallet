using PalladiumWallet.Core.Net;

namespace PalladiumWallet.Tests.Net;

/// <summary>
/// Tests for the release-tag parsing used by the update check. The network
/// call itself is best-effort by design (any failure → null) and is not
/// exercised here: only the version-comparison logic is deterministic.
/// </summary>
public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V0.9.1", "0.9.1")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("0.9.0-beta", "0.9.0")]
    [InlineData("v2.0.0-rc.1", "2.0.0")]
    [InlineData(" 1.4 ", "1.4")]
    public void I_tag_di_release_si_parsano_nella_versione_attesa(string tag, string expected)
    {
        Assert.True(UpdateChecker.TryParse(tag, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("non-una-versione")]
    public void I_tag_non_versionati_vengono_rifiutati(string tag)
    {
        Assert.False(UpdateChecker.TryParse(tag, out _));
    }

    [Fact]
    public void Il_confronto_remota_maggiore_di_locale_segue_l_ordine_semver()
    {
        // The same comparison CheckAsync applies between remote tag and running app.
        Assert.True(UpdateChecker.TryParse("v0.10.0", out var remote));
        Assert.True(UpdateChecker.TryParse("0.9.0", out var local));
        Assert.True(remote > local); // 0.10 > 0.9: numeric, not lexicographic
    }
}
