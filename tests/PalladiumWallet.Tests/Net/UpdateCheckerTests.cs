using System.Net;
using System.Text;
using PalladiumWallet.Core.Net;

namespace PalladiumWallet.Tests.Net;

/// <summary>
/// Tests for the update check: tag parsing plus the full CheckAsync flow via
/// a stub HttpMessageHandler (the same seam the production overload wraps),
/// so no real GitHub call is ever made.
/// </summary>
public class UpdateCheckerTests
{
    /// <summary>Handler stub: replies with a fixed status/body, or throws.</summary>
    private sealed class StubHandler(HttpStatusCode status, string? body = null,
        Exception? throws = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            throws is not null
                ? Task.FromException<HttpResponseMessage>(throws)
                : Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body ?? "", Encoding.UTF8, "application/json"),
                });
    }

    private static Task<LatestRelease?> Check(string current, HttpStatusCode status,
        string? body = null, Exception? throws = null) =>
        UpdateChecker.CheckAsync(current, new StubHandler(status, body, throws));

    [Fact]
    public async Task Una_release_piu_nuova_viene_segnalata_con_tag_e_url()
    {
        var release = await Check("0.9.1", HttpStatusCode.OK,
            """{"tag_name":"v1.0.0","html_url":"https://example.test/rel/v1.0.0"}""");

        Assert.NotNull(release);
        Assert.Equal("v1.0.0", release!.Tag);
        Assert.Equal("https://example.test/rel/v1.0.0", release.HtmlUrl);
    }

    [Fact]
    public async Task Senza_html_url_viene_costruito_il_link_alla_pagina_della_release()
    {
        var release = await Check("0.9.1", HttpStatusCode.OK, """{"tag_name":"v1.0.0"}""");

        Assert.NotNull(release);
        Assert.Contains("/releases/tag/v1.0.0", release!.HtmlUrl);
    }

    [Theory]
    [InlineData("""{"tag_name":"v0.9.1"}""")] // same version
    [InlineData("""{"tag_name":"v0.9.0"}""")] // older
    [InlineData("""{"tag_name":"main"}""")]   // unparseable tag
    [InlineData("""{"tag_name":""}""")]       // empty tag
    [InlineData("""{}""")]                    // missing tag
    public async Task Nessun_aggiornamento_quando_il_tag_non_e_piu_nuovo(string body)
    {
        Assert.Null(await Check("0.9.1", HttpStatusCode.OK, body));
    }

    [Fact]
    public async Task Una_risposta_http_di_errore_risolve_a_null()
    {
        Assert.Null(await Check("0.9.1", HttpStatusCode.NotFound));
        Assert.Null(await Check("0.9.1", HttpStatusCode.InternalServerError));
    }

    [Fact]
    public async Task Json_malformato_o_errore_di_rete_risolvono_a_null()
    {
        Assert.Null(await Check("0.9.1", HttpStatusCode.OK, "not json at all"));
        Assert.Null(await Check("0.9.1", HttpStatusCode.OK,
            """{"tag_name":"v9.9.9"}""", throws: new HttpRequestException("offline")));
    }

    [Fact]
    public async Task Una_versione_locale_non_parsabile_risolve_a_null()
    {
        Assert.Null(await Check("dev-build", HttpStatusCode.OK, """{"tag_name":"v9.9.9"}"""));
    }

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
