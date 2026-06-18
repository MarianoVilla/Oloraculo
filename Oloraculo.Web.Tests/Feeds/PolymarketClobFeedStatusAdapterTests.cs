using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.Feeds;
using Oloraculo.Web.Feeds.Adapters;

namespace Oloraculo.Web.Tests.Feeds;

public sealed class PolymarketClobFeedStatusAdapterTests : TestFixtures
{
    [Fact]
    public void Probe_NetworkDisabledWithoutCachedBooksReportsCollectorPending()
    {
        var report = Adapter(new Dictionary<string, string>(), ["token-ok"]).Probe(FeedStatusAdapterTestSupport.NoNetworkContext);

        Assert.Equal(FeedAdapterState.Planned, report.State);
        Assert.Equal(["LIVE_COLLECTOR_PENDING"], report.Blockers);
    }

    [Fact]
    public void Probe_NoConfiguredTokensReportsEmpty()
    {
        var report = Adapter(new Dictionary<string, string>(), []).Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Empty, report.State);
        Assert.Equal(["NO_CLOB_TOKENS"], report.Blockers);
    }

    [Fact]
    public void Probe_PublicClobFetchFailureReportsDown()
    {
        var report = Adapter(new Dictionary<string, string>(), ["missing-token"]).Probe(FeedStatusAdapterTestSupport.NetworkContext);

        Assert.Equal(FeedAdapterState.Down, report.State);
        Assert.Equal(["CLOB_FETCH_FAILED"], report.Blockers);
    }

    [Fact]
    public void Probe_FreshBooksCanMakeSnapshotReadyWithoutPrivateCredential()
    {
        var store = new InMemoryFeedStatusHealthStore();
        var adapter = Adapter(new Dictionary<string, string>
        {
            ["POST https://clob.test/books\n[{\"token_id\":\"token-ok\"}]"] = $$"""[{{BookJson("token-ok", .48m, .50m, 100m)}}]"""
        }, ["token-ok"]);
        store.Upsert(adapter.Probe(FeedStatusAdapterTestSupport.NetworkContext));

        var service = new FeedStatusService(
            new TestSecretPresenceReader(),
            Options.Create(new OloraculoConfig()),
            [],
            store,
            Options.Create(new FeedStatusOptions()),
            () => FeedStatusAdapterTestSupport.Now);

        var row = service.Snapshot().Rows.Single(item => item.SourceId == "polymarket_clob");

        Assert.Equal(FeedReadiness.Ready, row.Readiness);
        Assert.True(row.Present);
        Assert.False(row.AuthPresent);
        Assert.Empty(row.Blockers);
        Assert.DoesNotContain("private", row.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", row.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private static PolymarketClobFeedStatusAdapter Adapter(IReadOnlyDictionary<string, string> responses, string[] tokenIds)
    {
        var config = Options.Create(new OloraculoConfig
        {
            PolymarketClobBaseUrl = "https://clob.test",
            PolymarketClobStatusTokenIds = tokenIds,
            PolymarketClobStatusMinimumDepthUsd = 1m
        });
        var markets = new PolymarketMarketDataService(new HttpClient(new FakeHttpMessageHandler(responses)), config);
        return new PolymarketClobFeedStatusAdapter(markets, config);
    }

    private static string BookJson(string tokenId, decimal bid, decimal ask, decimal size) =>
        $$"""
        {
          "market": "condition",
          "asset_id": "{{tokenId}}",
          "bids": [{"price":"{{bid}}","size":"{{size}}"}],
          "asks": [{"price":"{{ask}}","size":"{{size}}"}],
          "min_order_size": "5",
          "tick_size": "0.01",
          "last_trade_price": "{{ask}}"
        }
        """;

    private sealed class TestSecretPresenceReader : ISecretPresenceReader
    {
        public bool IsPresent(string name) => false;
    }
}
