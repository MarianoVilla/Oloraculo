using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Tests.ComboLab;

public class ComboEvaluatorTests
{
    [Fact]
    public void EvaluatesTwoLegComboOverSharedScorelineGrid()
    {
        var distribution = Distribution(
            ((1, 0), .40),
            ((1, 1), .30),
            ((0, 1), .20),
            ((2, 1), .10));
        var homeMoneyline = Contract(FootballMarketType.Moneyline, ContractSelection.Home, label: "home_ml");
        var bttsYes = Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, label: "btts_yes");
        var legs = new[]
        {
            new ComboLeg(homeMoneyline, TradeAction.Buy, 1m, .55m),
            new ComboLeg(bttsYes, TradeAction.Buy, 1m, .40m)
        };

        var result = new ComboEvaluator().Evaluate(distribution, legs);

        Assert.Equal(-.05m, result.ExpectedPnl, 6);
        Assert.Equal(.95m, result.NetCost, 6);
        Assert.Equal(0.20, result.ProbabilityOfLoss, 6);
        Assert.Equal(0.80, result.ProbabilityNonNegative, 6);
        Assert.Equal(.95m, result.MaxLoss, 6);
        Assert.Equal(1.05m, result.MaxProfit, 6);
        Assert.Equal(.475m, result.BreakEvenAveragePayoff, 6);

        AssertState(result, 1, 0, .05m);
        AssertState(result, 1, 1, .05m);
        AssertState(result, 0, 1, -.95m);
        AssertState(result, 2, 1, 1.05m);
    }

    [Fact]
    public void BuyAndSellFactoryMethodsUseExecutableSideAndSize()
    {
        var contract = Contract(FootballMarketType.Moneyline, ContractSelection.Home);
        var quote = new MarketQuote(BestBid: .58m, BestAsk: .62m, BidSize: 9m, AskSize: 11m, DateTimeOffset.UtcNow);

        var buy = ComboLeg.Buy(contract, 10m, quote);
        var sell = ComboLeg.Sell(contract, 9m, quote);

        Assert.Equal(TradeAction.Buy, buy.Action);
        Assert.Equal(.62m, buy.ExecutablePrice);
        Assert.Equal(TradeAction.Sell, sell.Action);
        Assert.Equal(.58m, sell.ExecutablePrice);
        Assert.Throws<ArgumentException>(() => ComboLeg.Buy(contract, 12m, quote));
        Assert.Throws<ArgumentException>(() => ComboLeg.Sell(contract, 10m, quote));
    }

    [Fact]
    public void EvaluatorRejectsInvalidCombos()
    {
        var evaluator = new ComboEvaluator();
        var distribution = Distribution(((1, 0), 1.0));
        var leg = new ComboLeg(Contract(FootballMarketType.Moneyline, ContractSelection.Home), TradeAction.Buy, 1m, .50m);

        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(distribution, new[] { leg }));
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(distribution, new[]
        {
            leg,
            leg with { Shares = 0m }
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(distribution, new[]
        {
            leg,
            leg with { ExecutablePrice = 1.01m }
        }));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(distribution, new[]
        {
            leg,
            leg with { Contract = Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, fixtureId: "fixture-2") }
        }));
    }

    [Fact]
    public void FranceStyleWinOrNoGoalComboHasExplicitBadHole()
    {
        var distribution = Distribution(
            ((1, 0), .50),
            ((0, 0), .20),
            ((1, 1), .25),
            ((0, 1), .05));
        var homeMoneyline = Contract(FootballMarketType.Moneyline, ContractSelection.Home, label: "home_ml");
        var homeUnder05 = Contract(
            FootballMarketType.TeamTotal,
            ContractSelection.Under,
            team: TeamSide.Home,
            line: .5m,
            label: "home_u0_5");
        var result = new ComboEvaluator().Evaluate(distribution, new[]
        {
            new ComboLeg(homeMoneyline, TradeAction.Buy, 1m, .60m),
            new ComboLeg(homeUnder05, TradeAction.Buy, 1m, .14m)
        });

        Assert.Equal(.26m, State(result, 1, 0).NetPnl, 6);
        Assert.Equal(.26m, State(result, 0, 0).NetPnl, 6);
        Assert.Equal(-.74m, State(result, 1, 1).NetPnl, 6);
        Assert.Equal(.25, result.ProbabilityOfLoss, 6);
        Assert.Equal(.01m, result.ExpectedPnl, 6);
    }

    private static FootballContract Contract(
        FootballMarketType marketType,
        ContractSelection selection,
        string fixtureId = "fixture-1",
        TeamSide? team = null,
        decimal? line = null,
        string label = "") => new()
        {
            FixtureId = fixtureId,
            MarketType = marketType,
            Selection = selection,
            Team = team,
            Line = line,
            Label = label
        };

    private static ScorelineDistribution Distribution(params ((int Home, int Away) Score, double Probability)[] states)
    {
        var maxGoals = states.Max(state => Math.Max(state.Score.Home, state.Score.Away));
        var matrix = new double[maxGoals + 1, maxGoals + 1];
        foreach (var state in states)
            matrix[state.Score.Home, state.Score.Away] = state.Probability;

        return new ScorelineDistribution { MaxGoals = maxGoals, Matrix = matrix };
    }

    private static void AssertState(ComboEvaluation result, int home, int away, decimal expectedPnl) =>
        Assert.Equal(expectedPnl, State(result, home, away).NetPnl, 6);

    private static ComboStatePnl State(ComboEvaluation result, int home, int away) =>
        result.StatePnL.Single(state => state.HomeGoals == home && state.AwayGoals == away);
}
