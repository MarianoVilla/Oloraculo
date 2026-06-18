using Oloraculo.Web.ComboLab.Markets;

namespace Oloraculo.Web.Tests.ComboLab;

public class PolymarketFootballUniverseReporterTests
{
    [Fact]
    public void Build_ClassifiesMarketsAndJoinsComboEligibilityByConditionOrSlug()
    {
        var markets = new[]
        {
            Market("m-ml", "fifwc-irq-nor-2026-06-16-nor", "condition-nor", "moneyline", "Norway"),
            Market("m-total", "fifwc-irq-nor-2026-06-16-total-2pt5", "condition-total", "totals", "Total: Over 2.5 Goals"),
            Market("m-player", "mbappe-shots", "condition-shots", "soccer_player_shots", "Will Mbappe record over 2.5 shots?"),
            Market("m-broken", "broken", null, "moneyline", "Broken market")
        };
        var comboMarkets = new[]
        {
            new PolymarketComboMarketSnapshot { ComboMarketId = "combo-nor", ConditionId = "condition-nor" },
            new PolymarketComboMarketSnapshot { ComboMarketId = "combo-total", Slug = "fifwc-irq-nor-2026-06-16-total-2pt5" }
        };

        var report = PolymarketFootballUniverseReporter.Build(markets, comboMarkets);

        Assert.Equal(4, report.Summary.TotalMarkets);
        Assert.Equal(2, report.Summary.ComboEligibleMarkets);
        Assert.Equal(1, report.Summary.SourceRejectedMarkets);
        Assert.Equal(3, report.Summary.CoverageCounts[PolymarketFootballModelCoverage.ScorelineGrid]);
        Assert.Equal(1, report.Summary.CoverageCounts[PolymarketFootballModelCoverage.PlayerModelNeeded]);
        Assert.Equal("combo-nor", report.Rows.Single(row => row.MarketId == "m-ml").ComboMarketId);
        Assert.Equal("combo-total", report.Rows.Single(row => row.MarketId == "m-total").ComboMarketId);
        Assert.False(report.Rows.Single(row => row.MarketId == "m-player").ComboEligible);
    }

    private static PolymarketMarketSnapshot Market(string id, string slug, string? conditionId, string sportsMarketType, string question) => new()
    {
        MarketId = id,
        Slug = slug,
        Question = question,
        ConditionId = conditionId,
        Active = true,
        Closed = false,
        EnableOrderBook = true,
        OrderMinSize = 5m,
        OrderPriceMinTickSize = .01m,
        SportsMarketType = sportsMarketType,
        Tokens = [new PolymarketOutcomeToken("Yes", $"token-{id}-yes", null), new PolymarketOutcomeToken("No", $"token-{id}-no", null)],
        RejectReasons = string.IsNullOrWhiteSpace(conditionId) ? [PolymarketRejectReason.MissingConditionId] : []
    };
}
