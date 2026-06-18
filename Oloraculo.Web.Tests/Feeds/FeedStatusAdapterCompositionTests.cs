using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.Feeds;

namespace Oloraculo.Web.Tests.Feeds;

public sealed class FeedStatusAdapterCompositionTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-18T12:00:00Z");

    [Fact]
    public void Snapshot_UsesFakeReadyAdapterForPolymarketClob()
    {
        var service = Service(
            adapters:
            [
                new FakeAdapter(new FeedAdapterReport(
                    SourceId: "polymarket_clob",
                    Source: "Polymarket CLOB",
                    Role: "executable book/trade hotpath",
                    State: FeedAdapterState.Ready,
                    ConfigPresent: true,
                    LatestRecvTsUtc: FixedNow.AddSeconds(-2),
                    RowsLastMinute: 7,
                    Detail: "fresh fake book"))
            ]);

        var row = service.Snapshot().Rows.Single(item => item.SourceId == "polymarket_clob");

        Assert.Equal(FeedReadiness.Ready, row.Readiness);
        Assert.True(row.Present);
        Assert.Empty(row.Blockers);
        Assert.Equal(string.Empty, row.Blocker);
    }

    [Fact]
    public void Snapshot_RedactsAdapterErrorAndUsesCustomBlockers()
    {
        var service = Service(
            adapters:
            [
                new FakeAdapter(new FeedAdapterReport(
                    SourceId: "polymarket_clob",
                    Source: "Polymarket CLOB",
                    Role: "executable book/trade hotpath",
                    State: FeedAdapterState.ParseError,
                    ConfigPresent: true,
                    LatestRecvTsUtc: FixedNow.AddSeconds(-1),
                    LastError: "parse failed token=super-secret-value",
                    Detail: "malformed fake CLOB",
                    Blockers: ["CLOB_PARSE_ERROR"]))
            ]);

        var row = service.Snapshot().Rows.Single(item => item.SourceId == "polymarket_clob");

        Assert.Equal(FeedReadiness.Blocked, row.Readiness);
        Assert.Equal(["CLOB_PARSE_ERROR"], row.Blockers);
        Assert.Equal("CLOB_PARSE_ERROR", row.Blocker);
        Assert.Contains("<redacted", row.LastErrorRedacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret-value", row.LastErrorRedacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_MissingAdapterFallsBackToPlannedOrConfigRows()
    {
        var service = Service();

        var polymarket = service.Snapshot().Rows.Single(item => item.SourceId == "polymarket_clob");
        var databet = service.Snapshot().Rows.Single(item => item.SourceId == "databet_sportsbook");

        Assert.Equal(FeedReadiness.Planned, polymarket.Readiness);
        Assert.Equal("LIVE_COLLECTOR_PENDING", polymarket.Blocker);
        Assert.Equal(FeedReadiness.ConfigMissing, databet.Readiness);
        Assert.Equal("AUTH_CONFIG_MISSING", databet.Blocker);
    }

    [Fact]
    public void Snapshot_SourceOrderRemainsCatalogOrder()
    {
        var service = Service(
            adapters:
            [
                new FakeAdapter(new FeedAdapterReport(
                    SourceId: "grid",
                    Source: "GRID",
                    Role: "esports telemetry",
                    State: FeedAdapterState.Ready,
                    ConfigPresent: true,
                    LatestRecvTsUtc: FixedNow,
                    Detail: "out-of-order registered fake adapter"))
            ]);

        Assert.Equal(
            FeedStatusSourceCatalog.All.Select(source => source.SourceId),
            service.Snapshot().Rows.Select(row => row.SourceId));
    }

    private static FeedStatusService Service(
        IReadOnlySet<string>? presentSecrets = null,
        IEnumerable<IFeedStatusAdapter>? adapters = null,
        IFeedStatusHealthStore? store = null)
    {
        return new FeedStatusService(
            new TestSecretPresenceReader(presentSecrets ?? new HashSet<string>(StringComparer.Ordinal)),
            Options.Create(new OloraculoConfig()),
            adapters ?? [],
            store ?? new InMemoryFeedStatusHealthStore(),
            Options.Create(new FeedStatusOptions()),
            () => FixedNow);
    }

    private sealed class FakeAdapter : IFeedStatusAdapter
    {
        private readonly FeedAdapterReport _report;

        public FakeAdapter(FeedAdapterReport report)
        {
            _report = report;
            SourceId = report.SourceId;
        }

        public string SourceId { get; }

        public FeedAdapterReport Probe(FeedStatusProbeContext context)
        {
            Assert.False(context.AllowNetwork);
            return _report;
        }
    }

    private sealed class TestSecretPresenceReader : ISecretPresenceReader
    {
        private readonly IReadOnlySet<string> _present;

        public TestSecretPresenceReader(IReadOnlySet<string> present) => _present = present;

        public bool IsPresent(string name) => _present.Contains(name);
    }
}
