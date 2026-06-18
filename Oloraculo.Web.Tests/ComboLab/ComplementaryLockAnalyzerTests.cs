using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Tests.ComboLab;

public class ComplementaryLockAnalyzerTests
{
    [Fact]
    public void ExactComplementsProducePositiveTerminalLockWhenCostIsBelowOneSet()
    {
        var distribution = Distribution(
            ((0, 0), .20),
            ((1, 0), .25),
            ((1, 1), .35),
            ((2, 1), .20));
        var legs = new[]
        {
            Buy(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, "btts_yes", tokenId: "yes"), shares: 10m, price: .46m),
            Buy(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.No, "btts_no", tokenId: "no"), shares: 10m, price: .47m)
        };

        var result = new ComplementaryLockAnalyzer().Evaluate(distribution, legs, executionBuffer: .05m);

        Assert.True(result.IsTerminalLock);
        Assert.True(result.IsPositive);
        Assert.Equal(ComplementaryLockVerdict.PositiveLock, result.Verdict);
        Assert.Equal(10m, result.MinTerminalPayout);
        Assert.Equal(10m, result.MaxTerminalPayout);
        Assert.Equal(9.30m, result.NetCost);
        Assert.Equal(.65m, result.LockedProfit, 6);
        Assert.Equal(1.0, result.ChanceToLock, 6);
        Assert.Equal(0.0, result.GapProbability, 6);
        Assert.Empty(result.GapStates);
        Assert.Empty(result.OverlapStates);
    }

    [Fact]
    public void NestedTotalsCanOverlapAndStillCoverEveryTerminalScoreline()
    {
        var distribution = Distribution(
            ((0, 0), .10),
            ((1, 0), .10),
            ((1, 1), .35),
            ((2, 1), .25),
            ((3, 1), .20));
        var legs = new[]
        {
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "over_1_5", line: 1.5m, tokenId: "over15"), price: .50m),
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "under_3_5", line: 3.5m, tokenId: "under35"), price: .35m)
        };

        var result = new ComplementaryLockAnalyzer().Evaluate(distribution, legs);

        Assert.True(result.IsTerminalLock);
        Assert.Equal(ComplementaryLockVerdict.PositiveLock, result.Verdict);
        Assert.Equal(1m, result.MinTerminalPayout);
        Assert.Equal(2m, result.MaxTerminalPayout);
        Assert.Equal(.15m, result.LockedProfit, 6);
        Assert.Equal(.60, result.OverlapProbability, 6);
        Assert.Contains(result.OverlapStates, state => state.HomeGoals == 1 && state.AwayGoals == 1 && state.GrossPayout == 2m);
    }

    [Fact]
    public void GapPairIsCorrelatedOnlyEvenWhenGapStatesHaveZeroModelProbability()
    {
        var distribution = DistributionWithMaxGoals(4, ((0, 0), 1.0));
        var legs = new[]
        {
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "under_1_5", line: 1.5m, tokenId: "under15"), price: .40m),
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "over_3_5", line: 3.5m, tokenId: "over35"), price: .30m)
        };

        var result = new ComplementaryLockAnalyzer().Evaluate(distribution, legs);

        Assert.False(result.IsTerminalLock);
        Assert.True(result.HasCoverageGap);
        Assert.Equal(ComplementaryLockVerdict.CorrelatedOnly, result.Verdict);
        Assert.Equal(0m, result.MinTerminalPayout);
        Assert.Equal(.70m, result.WorstGapLoss, 6);
        Assert.Equal(0.0, result.GapProbability, 6);
        Assert.Contains(result.GapStates, state => state.HomeGoals == 1 && state.AwayGoals == 1);
    }

    [Fact]
    public void CurrentScoreConditioningRemovesImpossibleTerminalGapStates()
    {
        var preGame = DistributionWithMaxGoals(4,
            ((0, 0), .20),
            ((1, 0), .20),
            ((2, 0), .30),
            ((2, 1), .20),
            ((3, 1), .10));
        var inPlay = preGame.ConditionOnMinimumScore(homeGoals: 2, awayGoals: 0);
        var legs = new[]
        {
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "over_1_5", line: 1.5m, tokenId: "over15"), price: .40m),
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "over_2_5", line: 2.5m, tokenId: "over25"), price: .10m)
        };

        var preGameResult = new ComplementaryLockAnalyzer().Evaluate(preGame, legs);
        var inPlayResult = new ComplementaryLockAnalyzer().Evaluate(inPlay, legs);

        Assert.Equal(ComplementaryLockVerdict.CorrelatedOnly, preGameResult.Verdict);
        Assert.Contains(preGameResult.GapStates, state => state.HomeGoals == 0 && state.AwayGoals == 0);
        Assert.Equal(ComplementaryLockVerdict.PositiveLock, inPlayResult.Verdict);
        Assert.True(inPlay.IgnoreZeroProbabilityStatesForEvaluation);
        Assert.Empty(inPlayResult.GapStates);
        Assert.Equal(1m, inPlayResult.MinTerminalPayout);
        Assert.All(inPlayResult.OverlapStates.Concat(inPlayResult.GapStates), state => Assert.True(state.HomeGoals >= 2));
    }

    [Fact]
    public void ExactScorePairsAreMarkedModeledGridOnlyNotTerminalClaims()
    {
        var distribution = DistributionWithMaxGoals(3, ((2, 1), .30), ((0, 0), .70));
        var legs = new[]
        {
            Buy(ExactScore("exact_2_1", 2, 1, tokenId: "exact21"), price: .12m),
            Buy(Contract(FootballMarketType.Moneyline, ContractSelection.NotHome, "not_home", tokenId: "notHome"), price: .58m)
        };

        var result = new ComplementaryLockAnalyzer().Evaluate(distribution, legs);

        Assert.Equal(ComplementaryLockCoverageScope.ModeledScorelineGrid, result.CoverageScope);
        Assert.False(result.IsTerminalLock);
    }

    [Fact]
    public void RejectsSellLegsUnequalSharesCrossFixturePairsAndSameTokenPairs()
    {
        var analyzer = new ComplementaryLockAnalyzer();
        var distribution = Distribution(((0, 0), 1.0));
        var first = Buy(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, "yes", tokenId: "same"), price: .50m);
        var second = Buy(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.No, "no", tokenId: "no"), price: .50m);

        Assert.Throws<ArgumentException>(() => analyzer.Evaluate(distribution, [first with { Action = TradeAction.Sell }, second]));
        Assert.Throws<ArgumentException>(() => analyzer.Evaluate(distribution, [first, second with { Shares = 2m }]));
        Assert.Throws<ArgumentException>(() => analyzer.Evaluate(distribution, [first, second with { Contract = second.Contract with { FixtureId = "other-fixture" } }]));
        Assert.Throws<ArgumentException>(() => analyzer.Evaluate(distribution, [first, second with { Contract = second.Contract with { Identity = second.Contract.Identity with { TokenId = "same" } } }]));
    }

    private static ComboLeg Buy(FootballContract contract, decimal shares = 1m, decimal price = .50m) =>
        new(contract, TradeAction.Buy, shares, price);

    private static FootballContract ExactScore(string label, int home, int away, string tokenId) => new()
    {
        FixtureId = "fixture-1",
        Identity = new PolymarketContractIdentity(TokenId: tokenId, OutcomeSide: label),
        MarketType = FootballMarketType.ExactScore,
        Selection = ContractSelection.Yes,
        ExactHomeGoals = home,
        ExactAwayGoals = away,
        Label = label
    };

    private static FootballContract Contract(
        FootballMarketType marketType,
        ContractSelection selection,
        string label,
        decimal? line = null,
        TeamSide? team = null,
        string tokenId = "token-1") => new()
        {
            FixtureId = "fixture-1",
            Identity = new PolymarketContractIdentity(TokenId: tokenId, OutcomeSide: label),
            MarketType = marketType,
            Selection = selection,
            Team = team,
            Line = line,
            Label = label
        };

    private static ScorelineDistribution Distribution(params ((int Home, int Away) Score, double Probability)[] states)
    {
        var maxGoals = states.Max(state => Math.Max(state.Score.Home, state.Score.Away));
        return DistributionWithMaxGoals(maxGoals, states);
    }

    private static ScorelineDistribution DistributionWithMaxGoals(int maxGoals, params ((int Home, int Away) Score, double Probability)[] states)
    {
        var matrix = new double[maxGoals + 1, maxGoals + 1];
        foreach (var state in states)
            matrix[state.Score.Home, state.Score.Away] = state.Probability;

        return new ScorelineDistribution { MaxGoals = maxGoals, Matrix = matrix };
    }
}
