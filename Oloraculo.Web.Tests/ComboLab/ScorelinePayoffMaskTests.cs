using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Pricing;

namespace Oloraculo.Web.Tests.ComboLab;

public class ScorelinePayoffMaskTests
{
    [Fact]
    public void MoneylineMasksPartitionScorelines()
    {
        var home = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.Moneyline,
            Selection = ContractSelection.Home
        });
        var draw = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.Moneyline,
            Selection = ContractSelection.Draw
        });
        var away = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.Moneyline,
            Selection = ContractSelection.Away
        });
        var notAway = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.Moneyline,
            Selection = ContractSelection.NotAway
        });

        Assert.True(home.Pays(2, 1));
        Assert.False(home.Pays(1, 1));
        Assert.True(draw.Pays(1, 1));
        Assert.True(away.Pays(0, 1));
        Assert.True(notAway.Pays(1, 1));
        Assert.False(notAway.Pays(0, 1));

        for (var h = 0; h <= 3; h++)
            for (var a = 0; a <= 3; a++)
                Assert.Equal(1, new[] { home.Pays(h, a), draw.Pays(h, a), away.Pays(h, a) }.Count(pays => pays));
    }

    [Fact]
    public void TotalsAndBttsMasksResolveExpectedStates()
    {
        var over25 = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.MatchTotal,
            Selection = ContractSelection.Over,
            Line = 2.5m
        });
        var homeUnder05 = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.TeamTotal,
            Selection = ContractSelection.Under,
            Team = TeamSide.Home,
            Line = 0.5m
        });
        var bttsYes = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.BothTeamsToScore,
            Selection = ContractSelection.Yes
        });
        var bttsNo = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.BothTeamsToScore,
            Selection = ContractSelection.No
        });

        Assert.False(over25.Pays(1, 1));
        Assert.True(over25.Pays(2, 1));
        Assert.True(homeUnder05.Pays(0, 2));
        Assert.False(homeUnder05.Pays(1, 0));
        Assert.True(bttsYes.Pays(1, 1));
        Assert.False(bttsYes.Pays(1, 0));
        Assert.True(bttsNo.Pays(1, 0));
        Assert.False(bttsNo.Pays(2, 2));
    }

    [Fact]
    public void SpreadMasksResolveHalfPointHandicaps()
    {
        var awayMinus15 = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.Spread,
            Selection = ContractSelection.Yes,
            Team = TeamSide.Away,
            Line = -1.5m
        });
        var awayNotCover = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.Spread,
            Selection = ContractSelection.No,
            Team = TeamSide.Away,
            Line = -1.5m
        });

        Assert.True(awayMinus15.Pays(0, 2));
        Assert.False(awayMinus15.Pays(0, 1));
        Assert.True(awayNotCover.Pays(0, 1));
        Assert.False(awayNotCover.Pays(0, 2));
    }

    [Fact]
    public void ExactScorePaysOneCellAndRejectsUnsupportedTail()
    {
        var exact21 = Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.ExactScore,
            Selection = ContractSelection.Yes,
            ExactHomeGoals = 2,
            ExactAwayGoals = 1
        });

        Assert.True(exact21.Pays(2, 1));
        Assert.False(exact21.Pays(1, 2));
        Assert.False(exact21.Pays(2, 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.ExactScore,
            Selection = ContractSelection.Yes,
            ExactHomeGoals = 4,
            ExactAwayGoals = 0
        }));
    }

    [Fact]
    public void IntegerTotalLinesAreRejectedUntilPushRulesExist()
    {
        Assert.Throws<ArgumentException>(() => Mask(new FootballContract
        {
            FixtureId = "fixture-1",
            MarketType = FootballMarketType.MatchTotal,
            Selection = ContractSelection.Under,
            Line = 2m
        }));
    }

    private static ScorelinePayoffMask Mask(FootballContract contract) =>
        ScorelinePayoffMaskFactory.Build(contract, maxGoals: 3);
}
