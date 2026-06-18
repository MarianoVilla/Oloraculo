using Microsoft.Extensions.Options;
using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.ComboLab.Scalp;

namespace Oloraculo.Web.Tests.ComboLab;

public class SportsScalpScannerServiceTests : TestFixtures
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-16T18:00:00Z");

    [Fact]
    public void BuildTargets_UsesRoiDenominatorFormula()
    {
        var targets = SportsScalpScannerService.BuildTargets(.48m);

        Assert.Equal(.52m, targets.Breakeven);
        Assert.Equal(.5004m, targets.Roi2);
        Assert.Equal(.4724m, targets.Roi5);
        Assert.Equal(.4459m, targets.Roi8);
        Assert.Equal(.4291m, targets.Roi10);
        Assert.Equal(.4129m, targets.Roi12);
    }

    [Fact]
    public void BuildDefaultLadder_UsesFourHundredShareUnitAroundEightPercentTarget()
    {
        var ladder = SportsScalpScannerService.BuildDefaultLadder(.48m, 400m);

        Assert.Equal(4, ladder.Count);
        Assert.Equal(400m, ladder.Sum(level => level.Shares));
        Assert.Equal(.4759m, ladder[0].Price);
        Assert.Equal(.4559m, ladder[1].Price);
        Assert.Equal(.4359m, ladder[2].Price);
        Assert.Equal(.4159m, ladder[3].Price);
    }

    [Fact]
    public async Task ScanAsync_BuildsWorldCupPriorityTwoLegRowsAndHedgeTargets()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&series_id=11433&include_children=true&include_best_lines=true&limit=100"] = EventListJson(),
            ["https://gamma.test/events/keyset?closed=false&limit=100"] = """{"data":[]}""",
            ["POST https://clob.test/books\n[{\"token_id\":\"over-token\"},{\"token_id\":\"under-token\"}]"] = $$"""
            [
              {{BookJson("over-token", .43m, .44m, 500m)}},
              {{BookJson("under-token", .47m, .48m, 500m)}}
            ]
            """
        });

        var snapshot = await service.ScanAsync(new SportsScalpScanOptions
        {
            MaxEvents = 4,
            MaxTokenBooks = 8,
            TargetShares = 400m,
            NowUtc = Now,
            IncludeGenericActiveEvents = true,
            IncludeWorldCupPriorityEvents = true
        });

        Assert.Equal("WATCH_ONLY_PUBLIC_READS", snapshot.Mode);
        Assert.Single(snapshot.Events);
        Assert.Equal("WORLD_CUP", snapshot.Events[0].Priority);
        Assert.Equal(2, snapshot.Candidates.Count);
        Assert.Equal(2, snapshot.TradeNowCount);
        Assert.Empty(snapshot.BlockerCounts);

        var underEntry = Assert.Single(snapshot.Candidates, candidate => candidate.EntryOutcome == "Under");
        Assert.Equal(SportsScalpVerdict.TradeNow, underEntry.Verdict);
        Assert.Equal(.48m, underEntry.EntryAsk);
        Assert.Equal(.44m, underEntry.HedgeAskNow);
        Assert.Equal(.92m, underEntry.PairCostNow);
        Assert.Equal(32m, underEntry.LockedProfitNow);
        Assert.Equal(.4459m, underEntry.HedgeTargets.Roi8);
        Assert.Contains("Quiet 0-0", underEntry.Trigger, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, underEntry.SuggestedHedgeLadder.Count);
    }

    [Fact]
    public async Task ScanAsync_BlocksRowsWhenOppositeBookIsMissing()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&limit=100"] = EventListJson(),
            ["POST https://clob.test/books\n[{\"token_id\":\"over-token\"},{\"token_id\":\"under-token\"}]"] = $$"""
            [
              {{BookJson("over-token", .43m, .44m, 500m)}}
            ]
            """
        });

        var snapshot = await service.ScanAsync(new SportsScalpScanOptions { MaxEvents = 4, MaxTokenBooks = 8, NowUtc = Now });

        Assert.Contains(snapshot.Candidates, candidate => candidate.Verdict == SportsScalpVerdict.Blocked && candidate.Blocker.Contains("fetch_failed", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(snapshot.BlockerCounts);
    }

    [Fact]
    public async Task ScanAsync_UsesFourHundredShareVwapAcrossDepthNotBestAskSize()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&limit=100"] = EventListJson(),
            ["POST https://clob.test/books\n[{\"token_id\":\"over-token\"},{\"token_id\":\"under-token\"}]"] = $$"""
            [
              {{DepthBookJson("over-token", [.43m], [(.44m, 100m), (.45m, 300m)])}},
              {{DepthBookJson("under-token", [.47m], [(.48m, 100m), (.49m, 300m)])}}
            ]
            """
        });

        var snapshot = await service.ScanAsync(new SportsScalpScanOptions { MaxEvents = 4, MaxTokenBooks = 8, TargetShares = 400m, NowUtc = Now });

        var underEntry = Assert.Single(snapshot.Candidates, candidate => candidate.EntryOutcome == "Under");
        Assert.Equal(SportsScalpVerdict.TradeNow, underEntry.Verdict);
        Assert.Equal(.4875m, underEntry.EntryVwap);
        Assert.Equal(.4475m, underEntry.HedgeVwapNow);
        Assert.Equal(.935m, underEntry.PairCostNow);
        Assert.Equal(.49m, underEntry.EntryWorstAsk);
        Assert.Equal(.45m, underEntry.HedgeWorstAskNow);
        Assert.Equal(400m, underEntry.EntryFillableShares);
        Assert.Equal(400m, underEntry.HedgeFillableShares);
        Assert.DoesNotContain("INSUFFICIENT", underEntry.Blocker, StringComparison.OrdinalIgnoreCase);
    }

    private static SportsScalpScannerService Service(IReadOnlyDictionary<string, string> responses)
    {
        var options = Options.Create(new OloraculoConfig
        {
            PolymarketGammaBaseUrl = "https://gamma.test",
            PolymarketClobBaseUrl = "https://clob.test"
        });
        var markets = new PolymarketMarketDataService(new HttpClient(new FakeHttpMessageHandler(responses)), options);
        return new SportsScalpScannerService(markets);
    }

    private static string EventListJson() =>
        """
        {
          "data": [
            {
              "id": "event-1",
              "slug": "fifwc-fra-sen-2026-06-16",
              "title": "World Cup: France vs Senegal",
              "active": true,
              "closed": false,
              "live": false,
              "startTime": "2026-06-16T19:00:00Z",
              "markets": [
                {
                  "id": "market-total-25",
                  "slug": "fifwc-fra-sen-total-25",
                  "question": "France vs Senegal Total Goals 2.5",
                  "conditionId": "condition-total-25",
                  "outcomes": "[\"Over\",\"Under\"]",
                  "clobTokenIds": "[\"over-token\",\"under-token\"]",
                  "outcomePrices": "[\"0.44\",\"0.48\"]",
                  "active": true,
                  "closed": false,
                  "enableOrderBook": true,
                  "orderMinSize": "5",
                  "orderPriceMinTickSize": "0.01",
                  "sportsMarketType": "totals",
                  "line": "2.5"
                }
              ]
            }
          ]
        }
        """;

    private static string BookJson(string token, decimal bid, decimal ask, decimal askSize) =>
        $$"""
        {
          "market": "condition-total-25",
          "asset_id": "{{token}}",
          "bids": [{"price":"{{bid}}","size":"500"}],
          "asks": [{"price":"{{ask}}","size":"{{askSize}}"}],
          "min_order_size": "5",
          "tick_size": "0.01",
          "neg_risk": false,
          "last_trade_price": "{{ask}}"
        }
        """;

    private static string DepthBookJson(string token, decimal[] bids, (decimal Price, decimal Size)[] asks)
    {
        var bidJson = string.Join(",", bids.Select(price => $"{{\"price\":\"{price}\",\"size\":\"500\"}}"));
        var askJson = string.Join(",", asks.Select(level => $"{{\"price\":\"{level.Price}\",\"size\":\"{level.Size}\"}}"));
        return $$"""
        {
          "market": "condition-total-25",
          "asset_id": "{{token}}",
          "bids": [{{bidJson}}],
          "asks": [{{askJson}}],
          "min_order_size": "5",
          "tick_size": "0.01",
          "neg_risk": false,
          "last_trade_price": "0.44"
        }
        """;
    }
}
