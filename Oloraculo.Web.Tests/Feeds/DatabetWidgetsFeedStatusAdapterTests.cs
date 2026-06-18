using System.Net;
using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.Feeds;
using Oloraculo.Web.Feeds.Adapters;

namespace Oloraculo.Web.Tests.Feeds;

public sealed class DatabetWidgetsFeedStatusAdapterTests
{
    [Fact]
    public void Probe_MissingTokenReportsMissingConfig()
    {
        var report = Adapter("{}").Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.MissingConfig, report.State);
        Assert.Equal(["AUTH_CONFIG_MISSING"], report.Blockers);
    }

    [Fact]
    public void Probe_NetworkDisabledReportsCollectorPending()
    {
        var report = Adapter("{}", env: EnvWithToken()).Probe(FeedStatusAdapterTestSupport.NoNetworkContext);

        Assert.Equal(FeedAdapterState.Planned, report.State);
        Assert.Equal(["COLLECTOR_NOT_ENABLED"], report.Blockers);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Probe_AuthDeniedReportsEntitlementDenied(HttpStatusCode statusCode)
    {
        var report = Adapter("{}", statusCode, EnvWithToken()).Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Down, report.State);
        Assert.Equal(["ENTITLEMENT_DENIED"], report.Blockers);
    }

    [Fact]
    public void Probe_EmptyWidgetListReportsEmpty()
    {
        var report = Adapter("""{"data":{"listSportEventWidgets":[]}}""", env: EnvWithToken())
            .Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Empty, report.State);
        Assert.Equal(0, report.RowsLastMinute);
        Assert.Equal(["EMPTY_SOURCE"], report.Blockers);
    }

    [Fact]
    public void Probe_MalformedJsonReportsParseError()
    {
        var report = Adapter("{ nope", env: EnvWithToken()).Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.ParseError, report.State);
        Assert.Equal(["PARSE_ERROR"], report.Blockers);
    }

    [Fact]
    public void Probe_WidgetSnapshotsReportReadyWithoutTokenLeak()
    {
        var handler = new CapturingHttpMessageHandler((_, _) =>
            FeedStatusAdapterTestSupport.Json(HttpStatusCode.OK, """{"data":{"listSportEventWidgets":[{"id":"widget-1","sport":"ESPORTS_LOL","status":"LIVE"}]}}"""));
        var report = Adapter(handler, EnvWithToken()).Probe(FeedStatusAdapterTestSupport.NetworkContext);
        var row = FeedStatusRow.FromAdapter(report, FeedStatusAdapterTestSupport.Now);

        Assert.Equal(FeedAdapterState.Ready, report.State);
        Assert.Equal(1, report.RowsLastMinute);
        Assert.Contains(handler.AuthorizationHeaders, value => value.StartsWith("Bearer ", StringComparison.Ordinal));
        Assert.DoesNotContain("widget-secret", string.Join(" ", handler.AuthorizationHeaders), StringComparison.Ordinal);
        Assert.DoesNotContain("widget-secret", row.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("widget-secret", row.LastErrorRedacted, StringComparison.Ordinal);
    }

    private static DatabetWidgetsFeedStatusAdapter Adapter(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        Func<string, string?>? env = null) =>
        Adapter(new CapturingHttpMessageHandler((_, _) => FeedStatusAdapterTestSupport.Json(statusCode, response)), env);

    private static DatabetWidgetsFeedStatusAdapter Adapter(
        HttpMessageHandler handler,
        Func<string, string?>? env = null) =>
        new(
            new HttpClient(handler),
            Options.Create(new OloraculoConfig { DatabetWidgetsBaseUrl = "https://widgets.test/graphql" }),
            env ?? FeedStatusAdapterTestSupport.Env(new Dictionary<string, string>()));

    private static Func<string, string?> EnvWithToken() =>
        FeedStatusAdapterTestSupport.Env(new Dictionary<string, string> { ["DATABET_WIDGET_TOKEN"] = "widget-secret" });
}
