using System.Net;
using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.Feeds;
using Oloraculo.Web.Feeds.Adapters;

namespace Oloraculo.Web.Tests.Feeds;

public sealed class GridFeedStatusAdapterTests
{
    [Fact]
    public void Probe_MissingKeyReportsMissingConfig()
    {
        var report = Adapter("{}").Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.MissingConfig, report.State);
        Assert.Equal(["AUTH_CONFIG_MISSING"], report.Blockers);
    }

    [Fact]
    public void Probe_KeyPresentWithoutEndpointReportsNotImplemented()
    {
        var report = Adapter("{}", env: EnvWithKey(), configure: config => config.GridBaseUrl = "")
            .Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.NotImplemented, report.State);
        Assert.Equal(["GRID_PROBE_NOT_IMPLEMENTED"], report.Blockers);
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
    public void Probe_ZeroTelemetryReportsEmpty()
    {
        var report = Adapter("""{"events":[]}""", env: EnvWithKey()).Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Empty, report.State);
        Assert.Equal(["EMPTY_SOURCE"], report.Blockers);
    }

    [Fact]
    public void Probe_MalformedJsonReportsParseError()
    {
        var report = Adapter("{", env: EnvWithKey()).Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.ParseError, report.State);
        Assert.Equal(["PARSE_ERROR"], report.Blockers);
    }

    [Fact]
    public void Probe_TelemetryCountReportsReady()
    {
        var report = Adapter("""{"events":[{"id":"grid-event"}],"telemetry":[{"id":"tick"}]}""", env: EnvWithKey())
            .Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Ready, report.State);
        Assert.Equal(2, report.RowsLastMinute);
    }

    private static GridFeedStatusAdapter Adapter(
        string response,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        Func<string, string?>? env = null,
        Action<OloraculoConfig>? configure = null)
    {
        var handler = new CapturingHttpMessageHandler((_, _) => FeedStatusAdapterTestSupport.Json(statusCode, response));
        var config = new OloraculoConfig
        {
            GridBaseUrl = "https://grid.test",
            GridHealthPath = "health"
        };
        configure?.Invoke(config);
        return new GridFeedStatusAdapter(new HttpClient(handler), Options.Create(config), env ?? FeedStatusAdapterTestSupport.Env(new Dictionary<string, string>()));
    }

    private static Func<string, string?> EnvWithKey() =>
        FeedStatusAdapterTestSupport.Env(new Dictionary<string, string> { ["GRID_KEY"] = "grid-secret" });
}
