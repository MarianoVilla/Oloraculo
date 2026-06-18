using Microsoft.Extensions.Options;
using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.ComboLab.Scalp;
using System.Text.Json;

namespace Oloraculo.Web.Tests.ComboLab;

public class SportsScalpUniverseTests : TestFixtures
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-18T16:00:00Z");

    [Fact]
    public void NearWindow_IncludesEventStartingInThreeHoursFiftyNineMinutesUtc()
    {
        var decision = SportsScalpNearWindow.Create(Now).Evaluate(Event(start: Now.AddHours(3).AddMinutes(59)));

        Assert.True(decision.IsNear);
        Assert.Equal("start_within_near_window", decision.IncludeReason);
        Assert.Equal(Now.AddHours(-4), decision.WindowStartUtc);
        Assert.Equal(Now.AddHours(4), decision.WindowEndUtc);
    }

    [Fact]
    public void NearWindow_ExcludesEventStartingInFourHoursOneMinuteUnlessDebug()
    {
        var window = SportsScalpNearWindow.Create(Now);
        var ev = Event(start: Now.AddHours(4).AddMinutes(1));

        var normal = window.Evaluate(ev);
        var debug = window.Evaluate(ev, includeDebugOldEvents: true);

        Assert.False(normal.IsNear);
        Assert.Equal("outside_near_window", normal.ExcludeReason);
        Assert.True(debug.IsDiagnosticVisible);
    }

    [Fact]
    public void NearWindow_IncludesActiveStartedEventWithinPastTolerance()
    {
        var decision = SportsScalpNearWindow.Create(Now).Evaluate(Event(start: Now.AddHours(-1)));

        Assert.True(decision.IsNear);
        Assert.Equal("start_within_near_window", decision.IncludeReason);
    }

    [Fact]
    public void NearWindow_ExcludesOldNonLiveEvent()
    {
        var decision = SportsScalpNearWindow.Create(Now).Evaluate(Event(start: Now.AddHours(-6)));

        Assert.False(decision.IsNear);
        Assert.Equal("outside_near_window", decision.ExcludeReason);
    }

    [Fact]
    public void NearWindow_ExcludesClosedOrEndedEvents()
    {
        var closed = SportsScalpNearWindow.Create(Now).Evaluate(Event(start: Now, closed: true));
        var ended = SportsScalpNearWindow.Create(Now).Evaluate(Event(start: Now, ended: true));

        Assert.False(closed.IsNear);
        Assert.Equal("closed_or_resolved", closed.ExcludeReason);
        Assert.False(ended.IsNear);
        Assert.Equal("closed_or_resolved", ended.ExcludeReason);
    }

    [Fact]
    public void NearWindow_PutsMissingStartTimeInDiagnosticBucket()
    {
        var decision = SportsScalpNearWindow.Create(Now).Evaluate(Event(start: null));

        Assert.False(decision.IsNear);
        Assert.Equal("missing_start_time_needs_review", decision.DiagnosticBucket);
    }

    [Fact]
    public void NearWindow_UsesUtcInstantNotBrowserLocalTimezone()
    {
        var localOffsetStart = DateTimeOffset.Parse("2026-06-18T15:59:00-03:00");
        var decision = SportsScalpNearWindow.Create(Now).Evaluate(Event(start: localOffsetStart));

        Assert.True(decision.IsNear);
        Assert.Equal(DateTimeOffset.Parse("2026-06-18T18:59:00Z"), decision.UtcStartTime);
    }

    [Fact]
    public void Parser_ParsesClobTokenIdsFromJsonStringAndArray()
    {
        var jsonStringMarket = ParseMarket("""
        {"id":"m1","question":"Over 2.5","outcomes":"[\"Over\",\"Under\"]","clobTokenIds":"[\"over-token\",\"under-token\"]","active":true,"closed":false,"enableOrderBook":true,"conditionId":"c1"}
        """);
        var arrayMarket = ParseMarket("""
        {"id":"m2","question":"BTTS","outcomes":["Yes","No"],"clobTokenIds":["yes-token","no-token"],"active":true,"closed":false,"enableOrderBook":true,"conditionId":"c2"}
        """);

        Assert.Equal(["over-token", "under-token"], jsonStringMarket.ClobTokenIds);
        Assert.Equal(["yes-token", "no-token"], arrayMarket.ClobTokenIds);
        Assert.False(jsonStringMarket.DataQuality.MissingTokenIds);
    }

    [Fact]
    public void Parser_ParsesTokenObjectsSnakeCaseFieldsAndMalformedArraysWithoutCrashing()
    {
        var tokenObjectMarket = ParseMarket("""
        {
          "id":"m3",
          "question":"Ghana moneyline",
          "outcome_names":"[\"Yes\",\"No\"]",
          "tokens":[{"outcome":"Yes","token_id":"ghana-yes"},{"outcome":"No","token_id":"ghana-no"}],
          "active":true,
          "closed":false,
          "archived":false,
          "accepting_orders":true,
          "enableOrderBook":true,
          "condition_id":"c3"
        }
        """);
        var malformedMarket = ParseMarket("""{"id":"m4","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[not-json","active":true,"closed":false}""");

        Assert.Equal("c3", tokenObjectMarket.ConditionId);
        Assert.True(tokenObjectMarket.AcceptingOrders);
        Assert.Equal(["ghana-yes", "ghana-no"], tokenObjectMarket.ClobTokenIds);
        Assert.True(malformedMarket.DataQuality.MissingTokenIds);
    }

    [Fact]
    public void Parser_PreservesUnknownAndConfirmedZeroLiquidity()
    {
        var missing = ParseMarket("""{"id":"missing","outcomes":["Yes","No"],"clobTokenIds":["yes","no"],"active":true,"closed":false}""");
        var numeric = ParseMarket("""{"id":"numeric","outcomes":["Yes","No"],"clobTokenIds":["yes","no"],"liquidity":"1234.56","volume24hrClob":"789.10","active":true,"closed":false}""");
        var zero = ParseMarket("""{"id":"zero","outcomes":["Yes","No"],"clobTokenIds":["yes","no"],"liquidityNum":0,"active":true,"closed":false}""");

        Assert.Null(missing.LiquidityGamma);
        Assert.Equal(1234.56m, numeric.LiquidityGamma);
        Assert.Equal(789.10m, numeric.Volume24h);
        Assert.Equal(0m, zero.LiquidityGamma);
        Assert.True(zero.DataQuality.LiquidityZeroConfirmed);
    }

    [Theory]
    [InlineData("Over 2.5 goals", PolymarketFootballMarketFamily.MatchTotal)]
    [InlineData("Both teams to score?", PolymarketFootballMarketFamily.BothTeamsToScore)]
    [InlineData("Ghana -1.5", PolymarketFootballMarketFamily.Spread)]
    [InlineData("Who will win?", PolymarketFootballMarketFamily.Moneyline)]
    [InlineData("First goal", PolymarketFootballMarketFamily.FirstToScore)]
    [InlineData("France team total over 1.5", PolymarketFootballMarketFamily.TeamTotal)]
    public void Classifier_RecognizesCommonSoccerMarketText(string question, PolymarketFootballMarketFamily expected)
    {
        var classification = PolymarketFootballMarketClassifier.Classify(
            sportsMarketType: null,
            marketType: null,
            question: question,
            slug: question.Replace(' ', '-'),
            description: null);

        Assert.Equal(expected, classification.Family);
    }

    [Fact]
    public async Task OrderbookTop_ComputesBestSpreadAndDepthWithinTwoCents()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://clob.test/book?token_id=token-ok"] = """
            {
              "market":"condition-1",
              "bids":[{"price":"0.47","size":"20"},{"price":"0.48","size":"10"}],
              "asks":[{"price":"0.53","size":"20"},{"price":"0.50","size":"10"},{"price":"0.51","size":"5"}],
              "last_trade_price":"0.49",
              "min_order_size":"5",
              "tick_size":"0.01"
            }
            """
        });

        var top = await service.FetchTokenBookTopAsync("token-ok");

        Assert.Equal(TokenBookRawStatus.Ok, top.RawBookStatus);
        Assert.Equal(.48m, top.BestBid);
        Assert.Equal(.50m, top.BestAsk);
        Assert.Equal(.49m, top.Mid);
        Assert.Equal(.02m, top.Spread);
        Assert.Equal(7.55m, top.DepthAsk2c);
        Assert.Equal(4.8m, top.DepthBid1c);
    }

    [Fact]
    public async Task OrderbookTop_DistinguishesEmptyBookAndFetchFailedFromZeroLiquidity()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://clob.test/book?token_id=empty-token"] = """{"market":"condition-1","bids":[],"asks":[],"min_order_size":"5","tick_size":"0.01"}"""
        });

        var empty = await service.FetchTokenBookTopAsync("empty-token");
        var failed = await service.FetchTokenBookTopAsync("missing-token");
        var missingToken = await service.FetchTokenBookTopAsync("");

        Assert.Equal(TokenBookRawStatus.EmptyBook, empty.RawBookStatus);
        Assert.Null(empty.DepthAsk2c);
        Assert.Equal(TokenBookRawStatus.FetchFailed, failed.RawBookStatus);
        Assert.Null(failed.DepthAsk2c);
        Assert.Equal(TokenBookRawStatus.MissingToken, missingToken.RawBookStatus);
    }

    [Fact]
    public async Task ScanAsync_BuildsPolymarketFirstNearUniverseWithWorldCupMarketsAndBookDepth()
    {
        var service = new SportsScalpScannerService(Service(new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&limit=100"] = GammaActiveEventsJson(),
            ["POST https://clob.test/books\n[{\"token_id\":\"ghana-yes\"},{\"token_id\":\"ghana-no\"},{\"token_id\":\"over-25\"},{\"token_id\":\"under-25\"},{\"token_id\":\"btts-yes\"},{\"token_id\":\"btts-no\"},{\"token_id\":\"spread-yes\"},{\"token_id\":\"spread-no\"}]"] = $$"""
            [
              {{BookJson("ghana-yes", .61m, .62m, 500m)}},
              {{BookJson("ghana-no", .37m, .38m, 500m)}},
              {{BookJson("over-25", .48m, .50m, 500m)}},
              {{BookJson("under-25", .49m, .51m, 500m)}},
              {{BookJson("btts-yes", .44m, .45m, 500m)}},
              {{BookJson("btts-no", .54m, .55m, 500m)}},
              {{BookJson("spread-yes", .46m, .47m, 500m)}},
              {{BookJson("spread-no", .52m, .53m, 500m)}}
            ]
            """
        }));

        var snapshot = await service.ScanAsync(new SportsScalpScanOptions
        {
            MaxEvents = 10,
            MaxTokenBooks = 20,
            TargetShares = 100m,
            NowUtc = Now
        });

        var worldCup = Assert.Single(snapshot.Events, row => row.Slug == "fifwc-ghana-panama-2026-06-18");
        Assert.Equal("polymarket_gamma", worldCup.Source);
        Assert.True(worldCup.IsNear);
        Assert.Equal(4, worldCup.MarketCount);
        Assert.Equal(8, worldCup.TokenIdCount);
        Assert.Equal(8, worldCup.LiveOrderBookCount);
        Assert.Equal("clob_depth", worldCup.LiquiditySource);
        Assert.Equal("ok", worldCup.LiquidityQuality);
        Assert.True(worldCup.ClobDepth2cUsd > 0);
        Assert.Contains(snapshot.Candidates, candidate => candidate.MarketFamily == nameof(PolymarketFootballMarketFamily.MatchTotal));
        Assert.Contains(snapshot.Candidates, candidate => candidate.MarketFamily == nameof(PolymarketFootballMarketFamily.BothTeamsToScore));
        Assert.Contains(snapshot.Candidates, candidate => candidate.MarketFamily == nameof(PolymarketFootballMarketFamily.Spread));
        Assert.Contains(snapshot.Candidates, candidate => candidate.MarketFamily == nameof(PolymarketFootballMarketFamily.Moneyline));
        Assert.Equal(1, snapshot.Diagnostics.NearEventsCount);
        Assert.Equal(1, snapshot.Diagnostics.OldEventsExcludedCount);
        Assert.Equal(8, snapshot.Diagnostics.TokenBooksRequestedCount);
        Assert.Equal(8, snapshot.Diagnostics.TokenBooksOkCount);
    }

    private static PolymarketEventSnapshot Event(DateTimeOffset? start, bool active = true, bool closed = false, bool live = false, bool ended = false) => new()
    {
        EventId = "event-1",
        Slug = "event-1",
        Title = "Event 1",
        Active = active,
        Closed = closed,
        Live = live,
        Ended = ended,
        StartTimeUtc = start,
        FetchedAtUtc = Now
    };

    private static PolymarketMarketSnapshot ParseMarket(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PolymarketMarketDataService.ParseMarket(document.RootElement);
    }

    private static PolymarketMarketDataService Service(IReadOnlyDictionary<string, string> responses)
    {
        var options = Options.Create(new OloraculoConfig
        {
            PolymarketGammaBaseUrl = "https://gamma.test",
            PolymarketClobBaseUrl = "https://clob.test",
            PolymarketComboRfqBaseUrl = "https://combo.test"
        });
        return new PolymarketMarketDataService(new HttpClient(new FakeHttpMessageHandler(responses)), options);
    }

    private static string GammaActiveEventsJson() =>
        """
        {
          "data": [
            {
              "id": "old-event",
              "slug": "old-sports-event",
              "title": "Old Sports Event",
              "active": true,
              "closed": false,
              "startTime": "2026-06-18T09:00:00Z",
              "markets": []
            },
            {
              "id": "wc-event",
              "slug": "fifwc-ghana-panama-2026-06-18",
              "title": "World Cup: Ghana vs Panama",
              "active": true,
              "closed": false,
              "startTime": "2026-06-18T18:30:00Z",
              "volume24hr": "10000",
              "liquidity": "0",
              "markets": [
                {"id":"m-moneyline","slug":"ghana-panama-ghana","question":"Who will win? Ghana","conditionId":"c-moneyline","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"ghana-yes\",\"ghana-no\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"moneyline","liquidity":"0","volume24hr":"5000"},
                {"id":"m-total","slug":"ghana-panama-over-2pt5","question":"Over 2.5 goals","conditionId":"c-total","outcomes":"[\"Over\",\"Under\"]","clobTokenIds":"[\"over-25\",\"under-25\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"totals","line":"2.5","liquidity":"0","volume24hr":"3000"},
                {"id":"m-btts","slug":"ghana-panama-btts","question":"Both teams to score?","conditionId":"c-btts","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"btts-yes\",\"btts-no\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"btts"},
                {"id":"m-spread","slug":"ghana-panama-spread","question":"Ghana -1.5","conditionId":"c-spread","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"spread-yes\",\"spread-no\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"spreads","line":"-1.5"}
              ]
            }
          ]
        }
        """;

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
}
