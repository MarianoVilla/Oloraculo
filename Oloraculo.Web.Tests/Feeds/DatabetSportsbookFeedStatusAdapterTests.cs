using System.Net;
using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.Feeds;
using Oloraculo.Web.Feeds.Adapters;

namespace Oloraculo.Web.Tests.Feeds;

public sealed class DatabetSportsbookFeedStatusAdapterTests
{
    [Fact]
    public void Probe_MissingAuthReportsMissingConfig()
    {
        var adapter = Adapter("{}");

        var report = adapter.Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.MissingConfig, report.State);
        Assert.False(report.ConfigPresent);
        Assert.False(report.AuthPresent);
        Assert.Equal(["AUTH_CONFIG_MISSING"], report.Blockers);
    }

    [Fact]
    public void Probe_NetworkDisabledReportsCollectorPending()
    {
        var adapter = Adapter("{}", env: EnvWithToken());

        var report = adapter.Probe(FeedStatusAdapterTestSupport.NoNetworkContext);

        Assert.Equal(FeedAdapterState.Planned, report.State);
        Assert.True(report.ConfigPresent);
        Assert.True(report.AuthPresent);
        Assert.Equal(["COLLECTOR_NOT_ENABLED"], report.Blockers);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void Probe_AuthDeniedReportsEntitlementDenied(HttpStatusCode statusCode)
    {
        var adapter = Adapter("{}", statusCode, EnvWithToken());

        var report = adapter.Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Down, report.State);
        Assert.Equal(["ENTITLEMENT_DENIED"], report.Blockers);
    }

    [Fact]
    public void Probe_NonSuccessReportsSourceDown()
    {
        var adapter = Adapter("upstream unavailable", HttpStatusCode.BadGateway, EnvWithToken());

        var report = adapter.Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Down, report.State);
        Assert.Equal(["SOURCE_DOWN"], report.Blockers);
    }

    [Fact]
    public void Probe_MalformedJsonReportsParseError()
    {
        var adapter = Adapter("{ not json", env: EnvWithToken());

        var report = adapter.Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.ParseError, report.State);
        Assert.Equal(["PARSE_ERROR"], report.Blockers);
    }

    [Fact]
    public void Probe_SuccessWithNoEventsReportsEmpty()
    {
        var adapter = Adapter("""{"data":{"sportEventListByFilters":{"sportEvents":[]}}}""", env: EnvWithToken());

        var report = adapter.Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Empty, report.State);
        Assert.Equal(0, report.RowsLastMinute);
        Assert.Equal(["EMPTY_SOURCE"], report.Blockers);
    }

    [Fact]
    public void Probe_SuccessWithEventsReportsReadyAndDoesNotLeakAuth()
    {
        var handler = new CapturingHttpMessageHandler((_, _) =>
            FeedStatusAdapterTestSupport.Json(HttpStatusCode.OK, """{"data":{"sportEventListByFilters":{"sportEvents":[{"id":"10:event"}]}}}"""));
        var adapter = Adapter(handler, EnvWithToken());

        var report = adapter.Probe(FeedStatusAdapterTestSupport.NetworkContext);
        var row = FeedStatusRow.FromAdapter(report, FeedStatusAdapterTestSupport.Now);

        Assert.Equal(FeedAdapterState.Ready, report.State);
        Assert.Equal(1, report.RowsLastMinute);
        Assert.Equal(FeedStatusAdapterTestSupport.Now, report.LatestRecvTsUtc);
        Assert.Single(handler.XAuthHeaders);
        Assert.DoesNotContain("sportsbook-secret", string.Join(" ", handler.XAuthHeaders), StringComparison.Ordinal);
        Assert.DoesNotContain("sportsbook-secret", row.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("sportsbook-secret", row.LastErrorRedacted, StringComparison.Ordinal);
    }

    private static DatabetSportsbookFeedStatusAdapter Adapter(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        Func<string, string?>? env = null) =>
        Adapter(new CapturingHttpMessageHandler((_, _) => FeedStatusAdapterTestSupport.Json(statusCode, response)), env);

    private static DatabetSportsbookFeedStatusAdapter Adapter(
        HttpMessageHandler handler,
        Func<string, string?>? env = null) =>
        new(
            new HttpClient(handler),
            Options.Create(new OloraculoConfig { DatabetSportsbookBaseUrl = "https://sportsbook.test/graphql" }),
            env ?? FeedStatusAdapterTestSupport.Env(new Dictionary<string, string>()));

    private static Func<string, string?> EnvWithToken() =>
        FeedStatusAdapterTestSupport.Env(new Dictionary<string, string> { ["SPORTSBOOK_XAUTH"] = "sportsbook-secret" });
}
