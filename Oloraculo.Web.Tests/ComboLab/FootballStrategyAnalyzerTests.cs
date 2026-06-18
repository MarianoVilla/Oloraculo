using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Tests.ComboLab;

public class FootballStrategyAnalyzerTests
{
    [Fact]
    public void ThreeWayMoneylineBasketIsTrueLockWhenCostBelowOneSet()
    {
        var distribution = DistributionWithMaxGoals(3,
            ((1, 0), .30),
            ((1, 1), .25),
            ((0, 1), .25),
            ((2, 1), .20));
        var legs = new[]
        {
            Buy(Contract(FootballMarketType.Moneyline, ContractSelection.Home, "home", tokenId: "home"), price: .45m),
            Buy(Contract(FootballMarketType.Moneyline, ContractSelection.Draw, "draw", tokenId: "draw"), price: .25m),
            Buy(Contract(FootballMarketType.Moneyline, ContractSelection.Away, "away", tokenId: "away"), price: .25m)
        };

        var result = new FootballStrategyAnalyzer().Evaluate(distribution, legs, executionBuffer: .01m);

        Assert.Equal(3, result.LegCount);
        Assert.Equal(FootballStrategyVerdict.TruePositiveLock, result.Verdict);
        Assert.Equal(1m, result.MinGrossPayout);
        Assert.Equal(1m, result.MaxGrossPayout);
        Assert.Equal(.04m, result.MinNetPnl, 6);
        Assert.Equal(0.0, result.GapProbability, 6);
        Assert.True(result.IsLock);
    }

    [Fact]
    public void NestedTotalBandIsMiddleHedgeWhenCostAboveGuaranteedFloor()
    {
        var distribution = DistributionWithMaxGoals(4,
            ((0, 0), .10),
            ((1, 0), .10),
            ((1, 1), .45),
            ((2, 1), .20),
            ((4, 0), .15));
        var legs = new[]
        {
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 1.5", line: 1.5m, tokenId: "over15"), price: .70m),
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "Under 3.5", line: 3.5m, tokenId: "under35"), price: .35m)
        };

        var result = new FootballStrategyAnalyzer().Evaluate(distribution, legs);

        Assert.Equal(FootballStrategyVerdict.MiddleHedge, result.Verdict);
        Assert.Equal(1m, result.MinGrossPayout);
        Assert.Equal(2m, result.MaxGrossPayout);
        Assert.Equal(-.05m, result.MinNetPnl, 6);
        Assert.True(result.OverlapProbability > 0);
        Assert.Equal(0.0, result.GapProbability, 6);
    }

    [Fact]
    public void OppositeNestedTotalsLeaveGapStates()
    {
        var distribution = DistributionWithMaxGoals(4,
            ((0, 0), .25),
            ((1, 1), .25),
            ((2, 1), .25),
            ((4, 0), .25));
        var legs = new[]
        {
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "Under 1.5", line: 1.5m, tokenId: "under15"), price: .20m),
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 3.5", line: 3.5m, tokenId: "over35"), price: .20m)
        };

        var result = new FootballStrategyAnalyzer().Evaluate(distribution, legs);

        Assert.Equal(FootballStrategyVerdict.GapHedge, result.Verdict);
        Assert.True(result.HasCoverageGap);
        Assert.Contains(result.GapStates, state => state.HomeGoals == 1 && state.AwayGoals == 1);
        Assert.Contains(result.GapStates, state => state.HomeGoals == 2 && state.AwayGoals == 1);
    }

    [Fact]
    public void CurrentScoreConditioningRemovesImpossibleStrategyGapStates()
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
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 1.5", line: 1.5m, tokenId: "over15"), price: .40m),
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 2.5", line: 2.5m, tokenId: "over25"), price: .10m)
        };

        var preGameResult = new FootballStrategyAnalyzer().Evaluate(preGame, legs);
        var inPlayResult = new FootballStrategyAnalyzer().Evaluate(inPlay, legs);

        Assert.Equal(FootballStrategyVerdict.GapHedge, preGameResult.Verdict);
        Assert.Contains(preGameResult.GapStates, state => state.HomeGoals == 0 && state.AwayGoals == 0);
        Assert.Equal(FootballStrategyVerdict.TruePositiveLock, inPlayResult.Verdict);
        Assert.True(inPlay.IgnoreZeroProbabilityStatesForEvaluation);
        Assert.Empty(inPlayResult.GapStates);
        Assert.Equal(1m, inPlayResult.MinGrossPayout);
        Assert.All(inPlayResult.States, state => Assert.True(state.HomeGoals >= 2 && state.Probability > 0));
    }

    [Fact]
    public void BttsNoPlusOverOnePointFiveIsStructuralMiddlePattern()
    {
        var distribution = DistributionWithMaxGoals(3,
            ((0, 0), .10),
            ((1, 0), .10),
            ((2, 0), .30),
            ((1, 1), .25),
            ((2, 1), .25));
        var legs = new[]
        {
            Buy(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.No, "BTTS No", tokenId: "btts-no"), price: .57m),
            Buy(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 1.5", line: 1.5m, tokenId: "over15"), price: .45m)
        };

        var result = new FootballStrategyAnalyzer().Evaluate(distribution, legs);

        Assert.Equal(FootballStrategyVerdict.MiddleHedge, result.Verdict);
        Assert.Equal(1m, result.MinGrossPayout);
        Assert.Equal(2m, result.MaxGrossPayout);
        Assert.Contains(result.BestStates, state => state.HomeGoals == 2 && state.AwayGoals == 0 && state.GrossPayout == 2m);
    }

    [Fact]
    public void RejectsDuplicateTokensAndCrossFixtureLegs()
    {
        var analyzer = new FootballStrategyAnalyzer();
        var distribution = DistributionWithMaxGoals(1, ((0, 0), 1.0));
        var first = Buy(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, "yes", tokenId: "same"));
        var duplicate = Buy(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.No, "no", tokenId: "same"));
        var otherFixture = Buy(duplicate.Contract with { FixtureId = "fixture-2", Identity = duplicate.Contract.Identity with { TokenId = "other" } });

        Assert.Throws<ArgumentException>(() => analyzer.Evaluate(distribution, [first, duplicate]));
        Assert.Throws<ArgumentException>(() => analyzer.Evaluate(distribution, [first, otherFixture]));
    }

    private static ComboLeg Buy(FootballContract contract, decimal shares = 1m, decimal price = .50m) =>
        new(contract, TradeAction.Buy, shares, price);

    private static FootballContract Contract(
        FootballMarketType marketType,
        ContractSelection selection,
        string label,
        decimal? line = null,
        string tokenId = "token") => new()
        {
            FixtureId = "fixture-1",
            Identity = new PolymarketContractIdentity(TokenId: tokenId, OutcomeSide: label),
            MarketType = marketType,
            Selection = selection,
            Line = line,
            Label = label
        };

    private static ScorelineDistribution DistributionWithMaxGoals(int maxGoals, params ((int Home, int Away) Score, double Probability)[] states)
    {
        var matrix = new double[maxGoals + 1, maxGoals + 1];
        foreach (var state in states)
            matrix[state.Score.Home, state.Score.Away] = state.Probability;

        return new ScorelineDistribution { MaxGoals = maxGoals, Matrix = matrix }.Normalize();
    }
}
