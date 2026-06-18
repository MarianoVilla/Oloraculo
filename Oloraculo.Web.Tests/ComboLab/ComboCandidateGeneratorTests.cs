using Oloraculo.Web.ComboLab.Candidates;
using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Tests.ComboLab;

public class ComboCandidateGeneratorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-16T18:00:00Z");

    [Fact]
    public void GeneratesRankedBuyCandidatesWithExecutableMetricsAndBadHoles()
    {
        var distribution = Distribution(
            ((1, 0), .50),
            ((0, 0), .20),
            ((1, 1), .20),
            ((0, 1), .10));
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.Home, "home_ml", tokenId: "token-home"), ask: .60m),
            Quoted(Contract(FootballMarketType.TeamTotal, ContractSelection.Under, "home_u0_5", TeamSide.Home, .5m, tokenId: "token-home-u05"), ask: .14m),
            Quoted(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, "btts_yes", tokenId: "token-btts"), ask: .55m)
        };

        var result = new ComboCandidateGenerator().Generate(distribution, quoted, new ComboCandidateGeneratorOptions
        {
            AsOfUtc = Now,
            SharesPerLeg = 1m
        });

        Assert.Empty(result.Rejects);
        Assert.Equal(3, result.Candidates.Count);
        var best = result.Candidates[0];
        Assert.Equal("home_ml", best.FirstLeg.DisplayName);
        Assert.Equal("home_u0_5", best.SecondLeg.DisplayName);
        Assert.Equal(TradeAction.Buy, best.FirstLeg.Action);
        Assert.Equal(.60m, best.FirstLeg.ExecutablePrice);
        Assert.Equal(.60m, best.FirstLeg.ExecutableNotional);
        Assert.Equal(.06m, best.ExpectedPnl, 6);
        Assert.Equal(.74m, best.NetCost, 6);
        Assert.Equal(.74m, best.MaxLoss, 6);
        Assert.Equal(.20, best.ProbabilityOfLoss, 6);
        var worst = Assert.Single(best.BadHoleStates);
        Assert.Equal(1, worst.HomeGoals);
        Assert.Equal(1, worst.AwayGoals);
        Assert.Equal(-.74m, worst.NetPnl, 6);
    }

    [Fact]
    public void RejectsMissingAskInsufficientSizeAndStaleQuote()
    {
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.Home, "home_ml", tokenId: "token-no-ask"), ask: null),
            Quoted(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, "btts_yes", tokenId: "token-thin"), ask: .40m, askSize: 1m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "over_2_5", line: 2.5m, tokenId: "token-stale"), ask: .48m, fetchedAtUtc: Now.AddMinutes(-10))
        };

        var result = new ComboCandidateGenerator().Generate(Distribution(((0, 0), 1.0)), quoted, new ComboCandidateGeneratorOptions
        {
            AsOfUtc = Now,
            SharesPerLeg = 2m,
            MaxQuoteAge = TimeSpan.FromMinutes(5)
        });

        Assert.Empty(result.Candidates);
        AssertReject(result, ComboCandidateRejectReason.MissingExecutableAsk, "token-no-ask");
        AssertReject(result, ComboCandidateRejectReason.InsufficientAskSize, "token-thin");
        AssertReject(result, ComboCandidateRejectReason.StaleQuote, "token-stale");
    }

    [Fact]
    public void IncludeSellLegsUsesBidAndRejectsSameTokenPair()
    {
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.Home, "home_ml", tokenId: "token-home"), bid: .59m, ask: .61m)
        };

        var result = new ComboCandidateGenerator().Generate(Distribution(((1, 0), 1.0)), quoted, new ComboCandidateGeneratorOptions
        {
            AsOfUtc = Now,
            IncludeBuyLegs = true,
            IncludeSellLegs = true
        });

        Assert.Empty(result.Candidates);
        AssertReject(result, ComboCandidateRejectReason.SameTokenPair, "token-home");
    }

    [Fact]
    public void RecordsEvaluationRejectsForCrossFixturePairs()
    {
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.Home, "home_ml", fixtureId: "fixture-1", tokenId: "token-home"), ask: .60m),
            Quoted(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, "btts_yes", fixtureId: "fixture-2", tokenId: "token-btts"), ask: .40m)
        };

        var result = new ComboCandidateGenerator().Generate(Distribution(((1, 1), 1.0)), quoted, new ComboCandidateGeneratorOptions
        {
            AsOfUtc = Now
        });

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Rejects, reject => reject.Reason == ComboCandidateRejectReason.EvaluationFailed && reject.Detail.Contains("same fixture", StringComparison.OrdinalIgnoreCase));
    }

    private static ContractQuote Quoted(
        FootballContract contract,
        decimal? bid = .20m,
        decimal? ask = .50m,
        decimal bidSize = 10m,
        decimal askSize = 10m,
        DateTimeOffset? fetchedAtUtc = null) => new(
        contract,
        new MarketQuote(bid, ask, bidSize, askSize, fetchedAtUtc ?? Now));

    private static FootballContract Contract(
        FootballMarketType marketType,
        ContractSelection selection,
        string label,
        TeamSide? team = null,
        decimal? line = null,
        string fixtureId = "fixture-1",
        string tokenId = "token-1") => new()
        {
            FixtureId = fixtureId,
            Identity = new PolymarketContractIdentity(
                EventSlug: "france-senegal",
                EventId: "event-1",
                MarketId: "market-1",
                ConditionId: $"condition-{tokenId}",
                TokenId: tokenId,
                OutcomeSide: label),
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

    private static void AssertReject(ComboCandidateGenerationResult result, ComboCandidateRejectReason reason, string tokenId) =>
        Assert.Contains(result.Rejects, reject => reject.Reason == reason && reject.TokenId == tokenId);
}
