using System.Net;
using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.Feeds;
using Oloraculo.Web.Feeds.Adapters;

namespace Oloraculo.Web.Tests.Feeds;

public sealed class OddsPapiPinnacleFeedStatusAdapterTests
{
    [Fact]
    public void Probe_MissingKeyReportsMissingConfig()
    {
        var report = Adapter("[]").Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.MissingConfig, report.State);
        Assert.Equal(["AUTH_CONFIG_MISSING"], report.Blockers);
    }

    [Fact]
    public void Probe_NetworkDisabledReportsCollectorPending()
    {
        var report = Adapter("[]", env: EnvWithKey()).Probe(FeedStatusAdapterTestSupport.NoNetworkContext);

        Assert.Equal(FeedAdapterState.Planned, report.State);
        Assert.Equal(["COLLECTOR_NOT_ENABLED"], report.Blockers);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Probe_AuthDeniedReportsEntitlementDenied(HttpStatusCode statusCode)
    {
        var report = Adapter("{}", statusCode, EnvWithKey()).Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Down, report.State);
        Assert.Equal(["ENTITLEMENT_DENIED"], report.Blockers);
    }

    [Fact]
    public void Probe_NoEventsReportsEmpty()
    {
        var report = Adapter("""{"data":[]}""", env: EnvWithKey()).Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Empty, report.State);
        Assert.Equal(["EMPTY_SOURCE"], report.Blockers);
    }

    [Fact]
    public void Probe_EventsWithoutPinnacleRowsReportsCoverageMissing()
    {
        var report = Adapter("""{"data":[{"fixtureId":"f1","bookmakerOdds":{"bet365":{"markets":{}}}}]}""", env: EnvWithKey())
            .Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Empty, report.State);
        Assert.Equal(["PINNACLE_COVERAGE_MISSING"], report.Blockers);
        Assert.Equal(0, report.JoinCoverage);
    }

    [Fact]
    public void Probe_MalformedJsonReportsParseError()
    {
        var report = Adapter("[not-json", env: EnvWithKey()).Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.ParseError, report.State);
        Assert.Equal(["PARSE_ERROR"], report.Blockers);
    }

    [Fact]
    public void Probe_PinnacleRowsReportReadyAndRedactKeyFromStatus()
    {
        var handler = new CapturingHttpMessageHandler((_, _) =>
            FeedStatusAdapterTestSupport.Json(HttpStatusCode.OK, """{"data":[{"fixtureId":"f1","bookmakerOdds":{"pinnacle":{"markets":{"101":{}}}}}]}"""));
        var report = Adapter(handler, EnvWithKey()).Probe(FeedStatusAdapterTestSupport.NetworkContext);
        var row = FeedStatusRow.FromAdapter(report, FeedStatusAdapterTestSupport.Now);

        Assert.Equal(FeedAdapterState.Ready, report.State);
        Assert.Equal(1, report.RowsLastMinute);
        Assert.Equal(1, report.JoinCoverage);
        Assert.DoesNotContain("oddspapi-secret", string.Join(" ", handler.RequestUris), StringComparison.Ordinal);
        Assert.DoesNotContain("oddspapi-secret", row.LastErrorRedacted, StringComparison.Ordinal);
    }

    private static OddsPapiPinnacleFeedStatusAdapter Adapter(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        Func<string, string?>? env = null) =>
        Adapter(new CapturingHttpMessageHandler((_, _) => FeedStatusAdapterTestSupport.Json(statusCode, response)), env);

    private static OddsPapiPinnacleFeedStatusAdapter Adapter(
        HttpMessageHandler handler,
        Func<string, string?>? env = null) =>
        new(
            new HttpClient(handler),
            Options.Create(new OloraculoConfig
            {
                OddsPapiBaseUrl = "https://oddspapi.test/v4/",
                OddsPapiHealthPath = "odds-by-tournaments?tournamentIds=16"
            }),
            env ?? FeedStatusAdapterTestSupport.Env(new Dictionary<string, string>()));

    private static Func<string, string?> EnvWithKey() =>
        FeedStatusAdapterTestSupport.Env(new Dictionary<string, string> { ["ODDSPAPI_KEY"] = "oddspapi-secret" });
}
