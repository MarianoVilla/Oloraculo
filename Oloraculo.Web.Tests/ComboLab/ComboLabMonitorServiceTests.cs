using Microsoft.Extensions.Options;
using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.ComboLab.Monitor;

namespace Oloraculo.Web.Tests.ComboLab;

public class ComboLabMonitorServiceTests : TestFixtures
{
    [Fact]
    public async Task RefreshUniverse_FetchesTypesMarketsComboCatalogAndProjectsVerdicts()
    {
        var service = Monitor(new Dictionary<string, string>
        {
            ["https://gamma.test/sports/market-types"] = """
            {"data":["moneyline","soccer_player_shots","ignored_unknown_type"]}
            """,
            ["https://gamma.test/markets/keyset?closed=false&tag_id=102232&related_tags=true&sports_market_types=moneyline&limit=100"] = """
            [{"id":"m-nor","slug":"fifwc-irq-nor-2026-06-16-nor","question":"Norway","conditionId":"condition-nor","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"nor-yes\",\"nor-no\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"moneyline"}]
            """,
            ["https://gamma.test/markets/keyset?closed=false&tag_id=102232&related_tags=true&sports_market_types=soccer_player_shots&limit=100"] = """
            [{"id":"m-shots","slug":"player-shots","question":"Will Player record over 2.5 shots?","conditionId":"condition-shots","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"shots-yes\",\"shots-no\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","sportsMarketType":"soccer_player_shots","line":"2.5"}]
            """,
            ["https://combo.test/v1/rfq/combo-markets?limit=100"] = """
            {"data":[{"id":"combo-nor","slug":"fifwc-irq-nor-2026-06-16-nor","condition":"condition-nor"}]}
            """
        });

        var snapshot = await service.RefreshUniverseAsync(comboMaxPages: 1);

        Assert.False(snapshot.HasErrors);
        Assert.Equal(2, snapshot.Report.Summary.TotalMarkets);
        Assert.Equal(1, snapshot.Report.Summary.ComboEligibleMarkets);
        Assert.Equal(1, snapshot.ReadyForPricing);
        Assert.Equal(1, snapshot.NeedsModel);
        Assert.Contains(snapshot.RequestedSportsMarketTypes, type => type == "moneyline");
        Assert.Contains(snapshot.RequestedSportsMarketTypes, type => type == "soccer_player_shots");
        Assert.DoesNotContain(snapshot.RequestedSportsMarketTypes, type => type == "ignored_unknown_type");
        Assert.Equal(ComboLabMonitorVerdict.ReadyForPricing, snapshot.Rows.Single(row => row.MarketId == "m-nor").Verdict);
        Assert.Equal(ComboLabMonitorVerdict.NeedsModel, snapshot.Rows.Single(row => row.MarketId == "m-shots").Verdict);
    }

    [Fact]
    public void Verdict_PrioritizesSourceBlocksBeforePricingCoverage()
    {
        var report = PolymarketFootballUniverseReporter.Build(
            [Market("m-blocked", "condition-blocked", "moneyline", [PolymarketRejectReason.NoOrderBook])],
            [new PolymarketComboMarketSnapshot { ComboMarketId = "combo-blocked", ConditionId = "condition-blocked" }]);

        var row = Assert.Single(report.Rows);

        Assert.Equal(ComboLabMonitorVerdict.SourceBlocked, ComboLabMonitorService.Verdict(row));
    }

    private static ComboLabMonitorService Monitor(IReadOnlyDictionary<string, string> responses)
    {
        var options = Options.Create(new OloraculoConfig
        {
            PolymarketGammaBaseUrl = "https://gamma.test",
            PolymarketClobBaseUrl = "https://clob.test",
            PolymarketComboRfqBaseUrl = "https://combo.test"
        });
        var data = new PolymarketMarketDataService(new HttpClient(new FakeHttpMessageHandler(responses)), options);
        return new ComboLabMonitorService(data);
    }

    private static PolymarketMarketSnapshot Market(string id, string conditionId, string sportsMarketType, IReadOnlyList<PolymarketRejectReason> rejectReasons) => new()
    {
        MarketId = id,
        Slug = id,
        Question = id,
        ConditionId = conditionId,
        Active = rejectReasons.Count == 0,
        Closed = false,
        EnableOrderBook = rejectReasons.Count == 0,
        OrderMinSize = 5m,
        OrderPriceMinTickSize = .01m,
        SportsMarketType = sportsMarketType,
        Tokens = [new PolymarketOutcomeToken("Yes", $"{id}-yes", null), new PolymarketOutcomeToken("No", $"{id}-no", null)],
        RejectReasons = rejectReasons
    };
}
