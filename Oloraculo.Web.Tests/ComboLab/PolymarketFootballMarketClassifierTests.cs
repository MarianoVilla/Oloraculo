using Oloraculo.Web.ComboLab.Markets;

namespace Oloraculo.Web.Tests.ComboLab;

public class PolymarketFootballMarketClassifierTests
{
    [Theory]
    [InlineData("moneyline", PolymarketFootballMarketFamily.Moneyline)]
    [InlineData("spreads", PolymarketFootballMarketFamily.Spread)]
    [InlineData("totals", PolymarketFootballMarketFamily.MatchTotal)]
    [InlineData("soccer_team_totals", PolymarketFootballMarketFamily.TeamTotal)]
    [InlineData("btts", PolymarketFootballMarketFamily.BothTeamsToScore)]
    [InlineData("soccer_exact_score", PolymarketFootballMarketFamily.ExactScore)]
    public void ClassifiesScorelineGridFamilies(string sportsMarketType, PolymarketFootballMarketFamily expectedFamily)
    {
        var classification = PolymarketFootballMarketClassifier.Classify(sportsMarketType);

        Assert.Equal(expectedFamily, classification.Family);
        Assert.Equal(PolymarketFootballModelCoverage.ScorelineGrid, classification.Coverage);
        Assert.True(classification.IsScorelineGridPriced);
    }

    [Theory]
    [InlineData("soccer_player_shots", PolymarketFootballMarketFamily.PlayerShots, PolymarketFootballModelCoverage.PlayerModelNeeded)]
    [InlineData("soccer_player_shots_on_target", PolymarketFootballMarketFamily.PlayerShotsOnTarget, PolymarketFootballModelCoverage.PlayerModelNeeded)]
    [InlineData("soccer_player_goals", PolymarketFootballMarketFamily.PlayerGoals, PolymarketFootballModelCoverage.PlayerModelNeeded)]
    [InlineData("soccer_player_assists", PolymarketFootballMarketFamily.PlayerAssists, PolymarketFootballModelCoverage.PlayerModelNeeded)]
    [InlineData("goalkeeper_saves", PolymarketFootballMarketFamily.GoalkeeperSaves, PolymarketFootballModelCoverage.PlayerModelNeeded)]
    [InlineData("total_corners", PolymarketFootballMarketFamily.TotalCorners, PolymarketFootballModelCoverage.CornerModelNeeded)]
    [InlineData("soccer_team_total_corners", PolymarketFootballMarketFamily.TeamTotalCorners, PolymarketFootballModelCoverage.CornerModelNeeded)]
    [InlineData("second_half_totals", PolymarketFootballMarketFamily.SecondHalfTotal, PolymarketFootballModelCoverage.HalfSplitModelNeeded)]
    public void ClassifiesFamiliesThatNeedNewModels(string sportsMarketType, PolymarketFootballMarketFamily expectedFamily, PolymarketFootballModelCoverage expectedCoverage)
    {
        var classification = PolymarketFootballMarketClassifier.Classify(sportsMarketType);

        Assert.Equal(expectedFamily, classification.Family);
        Assert.Equal(expectedCoverage, classification.Coverage);
        Assert.True(classification.NeedsNewModel);
    }

    [Fact]
    public void CarriesDocumentedFifaWorldCupSurfaceIdentity()
    {
        Assert.Equal("fifwc", PolymarketWorldCupMarketSurface.SportSlug);
        Assert.Equal(102232, PolymarketWorldCupMarketSurface.TagId);
        Assert.Equal(11433, PolymarketWorldCupMarketSurface.SeriesId);
    }
}
