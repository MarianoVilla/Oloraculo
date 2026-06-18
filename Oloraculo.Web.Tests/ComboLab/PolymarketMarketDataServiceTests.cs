using Microsoft.Extensions.Options;
using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.Helpers;

namespace Oloraculo.Web.Tests.ComboLab;

public class PolymarketMarketDataServiceTests : TestFixtures
{
    [Fact]
    public async Task FetchEventBySlug_ParsesGammaMarketsTokensAndRejectReasons()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/events/slug/france-senegal?include_chat=false&include_template=false"] = GammaEventJson()
        });

        var snapshot = await service.FetchEventBySlugAsync("france-senegal");

        Assert.Equal("event-1", snapshot.EventId);
        Assert.Equal("france-senegal", snapshot.Slug);
        Assert.True(snapshot.Live);
        Assert.Equal("0-0", snapshot.Score);
        Assert.Equal(CryptoUtil.GetSha256(GammaEventJson()), snapshot.RawPayloadHash);
        Assert.Equal(2, snapshot.Markets.Count);

        var main = snapshot.Markets[0];
        Assert.Equal("market-1", main.MarketId);
        Assert.Equal("condition-1", main.ConditionId);
        Assert.True(main.EnableOrderBook);
        Assert.Empty(main.RejectReasons);
        Assert.Equal(2, main.Tokens.Count);
        Assert.Equal("France", main.Tokens[0].Outcome);
        Assert.Equal("token-france", main.Tokens[0].TokenId);
        Assert.Equal(.60m, main.Tokens[0].OutcomePrice);
        Assert.Equal("Senegal", main.Tokens[1].Outcome);
        Assert.Equal("moneyline", main.SportsMarketType);
        Assert.Null(main.Line);

        var rejected = snapshot.Markets[1];
        Assert.Contains(PolymarketRejectReason.MissingConditionId, rejected.RejectReasons);
        Assert.Contains(PolymarketRejectReason.MissingTokenId, rejected.RejectReasons);
        Assert.Contains(PolymarketRejectReason.ClosedOrNotAccepting, rejected.RejectReasons);
        Assert.Contains(PolymarketRejectReason.NoOrderBook, rejected.RejectReasons);
    }

    [Fact]
    public async Task FetchBook_ChoosesBestBidAndAskFromUnsortedBook()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://clob.test/book?token_id=token-france"] = BookJson()
        });

        var book = await service.FetchBookAsync("token-france");

        Assert.Equal("token-france", book.TokenId);
        Assert.Equal("condition-1", book.ConditionId);
        Assert.Equal(.45m, book.BestBid);
        Assert.Equal(100m, book.BidSize);
        Assert.Equal(.46m, book.BestAsk);
        Assert.Equal(150m, book.AskSize);
        Assert.Equal([.46m, .48m], book.Asks.Select(level => level.Price).ToArray());
        Assert.Equal([.45m, .43m], book.Bids.Select(level => level.Price).ToArray());
        Assert.Equal(5m, book.MinOrderSize);
        Assert.Equal(.01m, book.TickSize);
        Assert.Equal(.44m, book.LastTradePrice);
        Assert.Equal(CryptoUtil.GetSha256(BookJson()), book.RawPayloadHash);
        Assert.Empty(book.RejectReasons);

        var fill = book.Buy(400m);
        Assert.True(fill.IsComplete);
        Assert.Equal(400m, fill.FilledShares);
        Assert.Equal(.4725m, fill.Vwap);
        Assert.Equal(.48m, fill.WorstPrice);

        var capped = book.BuyUpTo(400m, .46m);
        Assert.False(capped.IsComplete);
        Assert.Equal(150m, capped.FilledShares);
    }

    [Fact]
    public async Task FetchBook_RecordsEmptyBookRejectReasons()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://clob.test/book?token_id=empty-token"] = """
            {
              "market": "condition-1",
              "asset_id": "empty-token",
              "bids": [],
              "asks": [],
              "neg_risk": false
            }
            """
        });

        var book = await service.FetchBookAsync("empty-token");

        Assert.Contains(PolymarketRejectReason.NoBid, book.RejectReasons);
        Assert.Contains(PolymarketRejectReason.NoAsk, book.RejectReasons);
        Assert.Contains(PolymarketRejectReason.MissingMinOrderSize, book.RejectReasons);
        Assert.Contains(PolymarketRejectReason.MissingTickSize, book.RejectReasons);
    }

    [Fact]
    public async Task FetchBooksAsync_PostsDocumentedTokenArrayAndParsesReturnedBooks()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["POST https://clob.test/books\n[{\"token_id\":\"token-france\"},{\"token_id\":\"token-senegal\"}]"] = $$"""
            [
              {{BookJson("token-france", .45m, .46m)}},
              {{BookJson("token-senegal", .39m, .40m)}}
            ]
            """
        });

        var books = await service.FetchBooksAsync(["token-france", "token-senegal", "token-france"]);

        Assert.Equal(2, books.Count);
        Assert.Equal("token-france", books[0].TokenId);
        Assert.Equal(.46m, books[0].BestAsk);
        Assert.Equal("token-senegal", books[1].TokenId);
        Assert.Equal(.39m, books[1].BestBid);
    }

    [Fact]
    public async Task FetchWorldCupMarketsByType_UsesDocumentedTagAndSportsMarketType()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/markets/keyset?closed=false&tag_id=102232&related_tags=true&sports_market_types=totals&limit=100"] = """
            [
              {
                "id": "market-total-25",
                "slug": "fifwc-irq-nor-2026-06-16-total-2pt5",
                "question": "Total: Over 2.5 Goals",
                "conditionId": "condition-total-25",
                "outcomes": "[\"Yes\",\"No\"]",
                "clobTokenIds": "[\"token-over\",\"token-under\"]",
                "active": true,
                "closed": false,
                "enableOrderBook": true,
                "orderMinSize": "5",
                "orderPriceMinTickSize": "0.01",
                "sportsMarketType": "totals",
                "line": "2.5"
              }
            ]
            """
        });

        var markets = await service.FetchWorldCupMarketsByTypeAsync("totals");

        var market = Assert.Single(markets);
        Assert.Equal("market-total-25", market.MarketId);
        Assert.Equal("totals", market.SportsMarketType);
        Assert.Equal(2.5m, market.Line);
        Assert.Empty(market.RejectReasons);
    }

    [Fact]
    public async Task FetchWorldCupMarketsByType_PagesWithNextCursorAndDedupes()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/markets/keyset?closed=false&tag_id=102232&related_tags=true&sports_market_types=moneyline&limit=100"] = """
            {"data":[{"id":"m1","slug":"page-one","conditionId":"condition-1","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"yes\",\"no\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"moneyline"}],"next_cursor":"cursor-2"}
            """,
            ["https://gamma.test/markets/keyset?closed=false&tag_id=102232&related_tags=true&sports_market_types=moneyline&limit=100&after_cursor=cursor-2"] = """
            {"data":[{"id":"m1-duplicate","slug":"duplicate","conditionId":"condition-1","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"yes2\",\"no2\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"moneyline"},{"id":"m2","slug":"page-two","conditionId":"condition-2","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"yes3\",\"no3\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"moneyline"}]}
            """
        });

        var markets = await service.FetchWorldCupMarketsByTypeAsync("moneyline");

        Assert.Equal(2, markets.Count);
        Assert.Contains(markets, market => market.MarketId == "m1" && market.ConditionId == "condition-1");
        Assert.Contains(markets, market => market.MarketId == "m2" && market.ConditionId == "condition-2");
    }

    [Fact]
    public async Task FetchSportsMarketTypes_ParsesDocumentedResponseShapes()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/sports/market-types"] = """
            {
              "data": [
                {"slug": "moneyline", "name": "Moneyline", "sport": "soccer"},
                {"slug": "soccer_exact_score", "name": "Exact Score", "sport": "soccer"},
                "soccer_player_shots"
              ]
            }
            """
        });

        var types = await service.FetchSportsMarketTypesAsync();

        Assert.Contains(types, type => type.Slug == "moneyline" && type.Name == "Moneyline" && type.SportSlug == "soccer");
        Assert.Contains(types, type => type.Slug == "soccer_exact_score");
        Assert.Contains(types, type => type.Slug == "soccer_player_shots");
    }

    [Fact]
    public async Task FetchWorldCupEvents_UsesWorldCupSeriesAndParsesEventList()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&series_id=11433&include_children=true&include_best_lines=true&limit=100"] = """
            {
              "events": [
                {"id":"event-1","slug":"fifwc-fra-sen-2026-06-16","title":"France vs Senegal","active":true,"closed":false,"startTime":"2026-06-16T19:00:00Z"},
                {"id":"event-2","slug":"fifwc-arg-alg-2026-06-16","title":"Argentina vs Algeria","active":true,"closed":false}
              ]
            }
            """
        });

        var events = await service.FetchWorldCupEventsAsync();

        Assert.Equal(2, events.Count);
        Assert.Contains(events, item => item.EventId == "event-1" && item.Slug == "fifwc-fra-sen-2026-06-16" && item.Title == "France vs Senegal");
        Assert.Contains(events, item => item.EventId == "event-2" && item.Slug == "fifwc-arg-alg-2026-06-16");
    }

    [Fact]
    public async Task FetchWorldCupEvents_PagesWithNextCursorAndFiltersInactiveEvents()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&series_id=11433&include_children=true&include_best_lines=true&limit=2"] = """
            {
              "events": [
                {"id":"event-1","slug":"fifwc-page-one","title":"Page One","active":true,"closed":false},
                {"id":"event-closed","slug":"inactive","title":"Inactive","active":false,"closed":false}
              ],
              "next_cursor":"cursor-2"
            }
            """,
            ["https://gamma.test/events/keyset?closed=false&series_id=11433&include_children=true&include_best_lines=true&limit=2&after_cursor=cursor-2"] = """
            {
              "events": [
                {"id":"event-1","slug":"fifwc-page-one-duplicate","title":"Duplicate","active":true,"closed":false},
                {"id":"event-2","slug":"fifwc-page-two","title":"Page Two","active":true,"closed":false}
              ]
            }
            """
        });

        var events = await service.FetchWorldCupEventsPagedAsync(maxPages: 3, limit: 2);

        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, ev => ev.EventId == "event-closed");
        Assert.Contains(events, ev => ev.EventId == "event-1" && ev.Slug == "fifwc-page-one");
        Assert.Contains(events, ev => ev.EventId == "event-2" && ev.Slug == "fifwc-page-two");
    }

    [Fact]
    public async Task FetchEvents_UsesGenericActiveEventsWhenNoTagProvided()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&limit=25"] = """
            {
              "events": [
                {"id":"event-1","slug":"nba-final","title":"NBA Final","active":true,"closed":false},
                {"id":"event-2","slug":"fifwc-fra-sen-2026-06-16","title":"France vs Senegal","active":true,"closed":false,"startTime":"2026-06-16T19:00:00Z"}
              ]
            }
            """
        });

        var events = await service.FetchEventsAsync(limit: 25);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, item => item.Slug == "nba-final" && item.Title == "NBA Final");
        Assert.Contains(events, item => item.Slug == "fifwc-fra-sen-2026-06-16" && item.StartTimeUtc.HasValue);
    }

    [Fact]
    public async Task FetchEventsPageAsync_UsesKeysetCursorAndParsesNextCursor()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&limit=2&after_cursor=cursor-1"] = """
            {
              "events": [
                {"id":"event-2","slug":"page-two","title":"Page Two","active":true,"closed":false}
              ],
              "next_cursor": "cursor-2"
            }
            """
        });

        var page = await service.FetchEventsPageAsync(limit: 2, cursor: "cursor-1");

        Assert.Equal("cursor-2", page.NextCursor);
        var ev = Assert.Single(page.Events);
        Assert.Equal("page-two", ev.Slug);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task FetchEventsPageAsync_RejectsLimitsOutsideDocumentedRange(int limit)
    {
        var service = Service(new Dictionary<string, string>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.FetchEventsPageAsync(limit: limit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task FetchWorldCupMarketsByTypePageAsync_RejectsLimitsOutsideDocumentedRange(int limit)
    {
        var service = Service(new Dictionary<string, string>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.FetchWorldCupMarketsByTypePageAsync("moneyline", limit: limit));
    }

    [Fact]
    public async Task FetchCurrentPositionsAsync_UsesPublicDataApiPositions()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://data.test/positions?user=0x56687bf447db6ffa42ffe2204a05edaa20f55839&sizeThreshold=0&limit=100&offset=0"] = """
            [
              {
                "proxyWallet": "0x56687bf447db6ffa42ffe2204a05edaa20f55839",
                "asset": "token-yes",
                "conditionId": "0xdd22472e552920b8438158ea7238bfadfa4f736aa4cee91a6b86c39ead110917",
                "size": 12.5,
                "avgPrice": 0.42,
                "currentValue": 5.5,
                "cashPnl": 0.25,
                "curPrice": 0.44,
                "redeemable": false,
                "mergeable": true,
                "title": "France vs Senegal",
                "slug": "france-senegal",
                "eventSlug": "fifwc-france-senegal",
                "outcome": "France",
                "outcomeIndex": 0,
                "oppositeOutcome": "Senegal",
                "oppositeAsset": "token-no",
                "negativeRisk": false
              }
            ]
            """
        });

        var positions = await service.FetchCurrentPositionsAsync("0x56687bf447db6ffa42ffe2204a05edaa20f55839", sizeThreshold: 0);

        var position = Assert.Single(positions);
        Assert.Equal("token-yes", position.Asset);
        Assert.Equal(12.5m, position.Size);
        Assert.Equal(.44m, position.CurrentPrice);
        Assert.True(position.Mergeable);
        Assert.Equal("fifwc-france-senegal", position.EventSlug);
    }

    [Fact]
    public async Task FetchWorldCupMarketsByTypes_DedupesAcrossFamilyScans()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://gamma.test/markets/keyset?closed=false&tag_id=102232&related_tags=true&sports_market_types=moneyline&limit=100"] = """
            [{"id":"m1","slug":"norway","question":"Norway","conditionId":"condition-shared","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"yes\",\"no\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"moneyline"}]
            """,
            ["https://gamma.test/markets/keyset?closed=false&tag_id=102232&related_tags=true&sports_market_types=totals&limit=100"] = """
            [{"id":"m1-duplicate","slug":"norway-duplicate","question":"Norway duplicate","conditionId":"condition-shared","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"yes2\",\"no2\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"totals"}]
            """
        });

        var markets = await service.FetchWorldCupMarketsByTypesAsync(["moneyline", "totals"]);

        var market = Assert.Single(markets);
        Assert.Equal("m1", market.MarketId);
    }

    [Fact]
    public async Task FetchComboMarketsPage_ParsesCursorConditionAndPrices()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://combo.test/v1/rfq/combo-markets?limit=2"] = """
            {
              "data": [
                {
                  "id": "1897087",
                  "slug": "fifwc-irq-nor-2026-06-16-nor",
                  "title": "Norway",
                  "condition": "0xcee0d9074a1f8766b4f66d806608a4097d027c94f85de367410fbbb166a29424",
                  "sportsMarketType": "moneyline",
                  "outcomes": ["Yes", "No"],
                  "clobTokenIds": ["token-yes", "token-no"],
                  "prices": ["0.775", "0.225"]
                }
              ],
              "next_cursor": "cursor-2"
            }
            """
        });

        var page = await service.FetchComboMarketsPageAsync(limit: 2);

        Assert.Equal("cursor-2", page.NextCursor);
        var market = Assert.Single(page.Markets);
        Assert.Equal("1897087", market.ComboMarketId);
        Assert.Equal("0xcee0d9074a1f8766b4f66d806608a4097d027c94f85de367410fbbb166a29424", market.ConditionId);
        Assert.Equal("moneyline", market.SportsMarketType);
        Assert.Equal(.775m, market.Prices[0]);
        Assert.Equal("token-yes", market.Tokens[0].TokenId);
    }

    [Fact]
    public async Task FetchComboMarkets_PagesUntilNoCursorAndDedupes()
    {
        var service = Service(new Dictionary<string, string>
        {
            ["https://combo.test/v1/rfq/combo-markets?limit=1"] = """
            {"data":[{"id":"combo-1","slug":"market-1","condition":"condition-1"}],"next_cursor":"next"}
            """,
            ["https://combo.test/v1/rfq/combo-markets?limit=1&cursor=next"] = """
            {"data":[{"id":"combo-1-duplicate","slug":"market-duplicate","condition":"condition-1"},{"id":"combo-2","slug":"market-2","condition":"condition-2"}]}
            """
        });

        var markets = await service.FetchComboMarketsAsync(maxPages: 3, limit: 1);

        Assert.Equal(2, markets.Count);
        Assert.Contains(markets, market => market.ConditionId == "condition-1" && market.ComboMarketId == "combo-1");
        Assert.Contains(markets, market => market.ConditionId == "condition-2");
    }

    private static PolymarketMarketDataService Service(IReadOnlyDictionary<string, string> responses)
    {
        var options = Options.Create(new OloraculoConfig
        {
            PolymarketGammaBaseUrl = "https://gamma.test",
            PolymarketClobBaseUrl = "https://clob.test",
            PolymarketDataBaseUrl = "https://data.test",
            PolymarketComboRfqBaseUrl = "https://combo.test"
        });
        return new PolymarketMarketDataService(new HttpClient(new FakeHttpMessageHandler(responses)), options);
    }

    private static string GammaEventJson() =>
        """
        {
          "id": "event-1",
          "slug": "france-senegal",
          "title": "France vs Senegal",
          "active": true,
          "closed": false,
          "live": true,
          "ended": false,
          "score": "0-0",
          "elapsed": "28'",
          "startTime": "2026-06-16T19:00:00Z",
          "markets": [
            {
              "id": "market-1",
              "slug": "france-vs-senegal-moneyline",
              "question": "France vs Senegal",
              "description": "Regulation winner.",
              "conditionId": "condition-1",
              "outcomes": "[\"France\",\"Senegal\"]",
              "clobTokenIds": "[\"token-france\",\"token-senegal\"]",
              "outcomePrices": "[\"0.60\",\"0.40\"]",
              "active": true,
              "closed": false,
              "enableOrderBook": true,
              "orderMinSize": 5,
              "orderPriceMinTickSize": 0.01,
              "category": "Sports",
              "marketType": "moneyline",
              "sportsMarketType": "moneyline"
            },
            {
              "id": "market-2",
              "question": "Broken market",
              "outcomes": "[\"Yes\",\"No\"]",
              "active": false,
              "closed": true,
              "enableOrderBook": false
            }
          ]
        }
        """;

    private static string BookJson() =>
        """
        {
          "market": "condition-1",
          "asset_id": "token-france",
          "timestamp": "1234567890",
          "hash": "hash-1",
          "bids": [
            {"price": "0.43", "size": "200"},
            {"price": "0.45", "size": "100"}
          ],
          "asks": [
            {"price": "0.48", "size": "250"},
            {"price": "0.46", "size": "150"}
          ],
          "min_order_size": "5",
          "tick_size": "0.01",
          "neg_risk": false,
          "last_trade_price": "0.44"
        }
        """;

    private static string BookJson(string tokenId, decimal bid, decimal ask) =>
        $$"""
        {
          "market": "condition-1",
          "asset_id": "{{tokenId}}",
          "timestamp": "1234567890",
          "hash": "hash-1",
          "bids": [
            {"price": "{{bid}}", "size": "100"}
          ],
          "asks": [
            {"price": "{{ask}}", "size": "150"}
          ],
          "min_order_size": "5",
          "tick_size": "0.01",
          "neg_risk": false,
          "last_trade_price": "{{bid}}"
        }
        """;
}
