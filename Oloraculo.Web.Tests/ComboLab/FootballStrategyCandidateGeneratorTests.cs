using Oloraculo.Web.ComboLab.Candidates;
using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Monitor;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Tests.ComboLab;

public class FootballStrategyCandidateGeneratorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-16T18:00:00Z");

    [Fact]
    public void GeneratesTotalsBttsAndMoneylineStrategyFamilies()
    {
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 1.5", line: 1.5m, tokenId: "over15"), .74m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "Under 1.5", line: 1.5m, tokenId: "under15"), .26m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 2.5", line: 2.5m, tokenId: "over25"), .49m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "Under 3.5", line: 3.5m, tokenId: "under35"), .74m),
            Quoted(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.No, "BTTS No", tokenId: "btts-no"), .57m),
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.Home, "Home", tokenId: "home"), .65m),
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.Draw, "Draw", tokenId: "draw"), .22m),
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.Away, "Away", tokenId: "away"), .12m),
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.NotHome, "Not Home", tokenId: "not-home"), .36m),
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.NotAway, "Not Away", tokenId: "not-away"), .89m)
        };

        var result = new FootballStrategyCandidateGenerator().Generate(Distribution(), quoted, new FootballStrategyCandidateGeneratorOptions
        {
            AsOfUtc = Now,
            SharesPerLeg = 10m,
            MaxCandidates = 40
        });

        Assert.Empty(result.Rejects);
        Assert.Contains(result.Candidates, candidate => candidate.StrategyKind == "total-band" && candidate.StructureLabel.Contains("nested total band", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Candidates, candidate => candidate.StrategyKind == "btts-total-cover" && candidate.StructureLabel.Contains("BTTS No", StringComparison.Ordinal));
        Assert.Contains(result.Candidates, candidate => candidate.StrategyKind == "moneyline-partition" && candidate.Legs.Count == 3);
        Assert.Contains(result.Candidates, candidate => candidate.StrategyKind == "moneyline-complement" && candidate.StructureLabel.Contains("NotHome + NotAway", StringComparison.Ordinal));
        Assert.Contains(result.Candidates, candidate => candidate.ScorelessThetaPerMinute != 0 || candidate.GoalJumpExposure != 0);
    }

    [Fact]
    public void GeneratesExactScoreBasketOnlyWhenUnderBucketIsComplete()
    {
        var quoted = new List<ContractQuote>
        {
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 2.5", line: 2.5m, tokenId: "over25"), .49m)
        };
        var underTwoPointFiveScores = new[] { (0, 0), (1, 0), (0, 1), (1, 1), (2, 0), (0, 2) };
        foreach (var (home, away) in underTwoPointFiveScores)
            quoted.Add(Quoted(ExactScore(home, away), .08m));

        var result = new FootballStrategyCandidateGenerator().Generate(Distribution(), quoted, new FootballStrategyCandidateGeneratorOptions
        {
            AsOfUtc = Now,
            SharesPerLeg = 1m,
            MaxCandidates = 20
        });

        var basket = Assert.Single(result.Candidates, candidate => candidate.StrategyKind == "exact-score-total-cover");
        Assert.Equal(7, basket.Legs.Count);
        Assert.Equal(FootballStrategyCoverageScope.ModeledScorelineGrid, basket.Evaluation.CoverageScope);
    }

    [Fact]
    public void RejectsMissingAskThinAskAndStaleQuote()
    {
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "No ask", line: 1.5m, tokenId: "no-ask"), ask: null),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "Thin", line: 1.5m, tokenId: "thin"), ask: .30m, askSize: 1m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Stale", line: 2.5m, tokenId: "stale"), ask: .50m, fetchedAtUtc: Now.AddMinutes(-10))
        };

        var result = new FootballStrategyCandidateGenerator().Generate(Distribution(), quoted, new FootballStrategyCandidateGeneratorOptions
        {
            AsOfUtc = Now,
            SharesPerLeg = 5m,
            MaxQuoteAge = TimeSpan.FromMinutes(2)
        });

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Rejects, reject => reject.Reason == FootballStrategyCandidateRejectReason.MissingExecutableAsk && reject.TokenId == "no-ask");
        Assert.Contains(result.Rejects, reject => reject.Reason == FootballStrategyCandidateRejectReason.InsufficientAskSize && reject.TokenId == "thin");
        Assert.Contains(result.Rejects, reject => reject.Reason == FootballStrategyCandidateRejectReason.StaleQuote && reject.TokenId == "stale");
    }

    [Fact]
    public void LiveRowReportsLockFloorPackageCushionWithAskCostExcludingBuffer()
    {
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.Home, "Home", tokenId: "home"), .45m),
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.Draw, "Draw", tokenId: "draw"), .25m),
            Quoted(Contract(FootballMarketType.Moneyline, ContractSelection.Away, "Away", tokenId: "away"), .25m)
        };

        var result = new FootballStrategyCandidateGenerator().Generate(Distribution(), quoted, new FootballStrategyCandidateGeneratorOptions
        {
            AsOfUtc = Now,
            SharesPerLeg = 10m,
            ExecutionBuffer = .10m,
            MaxCandidates = 10
        });

        var candidate = Assert.Single(result.Candidates, candidate => candidate.StrategyKind == "moneyline-partition");
        var row = LiveFootballStrategyCandidateRow.FromCandidate(candidate, PreGameState(), PreGameDecay(), PreGameConditioning());

        Assert.Equal("LOCK_FLOOR", row.BreakBasis);
        Assert.Equal(9.50m, row.PackageAskCost, 6);
        Assert.Equal(9.60m, row.NetCost, 6);
        Assert.Equal(9.90m, row.MaxPackageCostBeforeBreak, 6);
        Assert.Equal(.40m, row.TotalCushion, 6);
        Assert.Equal(.0133333333333333333333333333m, row.PerLegEqualPriceCushion, 24);
    }

    [Fact]
    public void LiveRowReportsUpsideBasisForMiddleHedgeNotLockFloor()
    {
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 1.5", line: 1.5m, tokenId: "over15"), .70m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "Under 3.5", line: 3.5m, tokenId: "under35"), .35m)
        };

        var result = new FootballStrategyCandidateGenerator().Generate(Distribution(), quoted, new FootballStrategyCandidateGeneratorOptions
        {
            AsOfUtc = Now,
            SharesPerLeg = 1m,
            MaxCandidates = 10
        });

        var candidate = Assert.Single(result.Candidates, candidate => candidate.StrategyKind == "total-band");
        var row = LiveFootballStrategyCandidateRow.FromCandidate(candidate, PreGameState(), PreGameDecay(), PreGameConditioning());

        Assert.Equal("UPSIDE_HEDGE", row.BreakBasis);
        Assert.Equal(FootballStrategyVerdict.MiddleHedge.ToString(), row.Verdict);
        Assert.Equal(1.05m, row.PackageAskCost, 6);
        Assert.Equal(2.00m, row.MaxPackageCostBeforeBreak, 6);
        Assert.Equal(.95m, row.TotalCushion, 6);
        Assert.Contains("middle hedge", row.Blocker, StringComparison.OrdinalIgnoreCase);
    }

    private static ContractQuote Quoted(FootballContract contract, decimal? ask, decimal askSize = 100m, DateTimeOffset? fetchedAtUtc = null) => new(
        contract,
        new MarketQuote(BestBid: ask.HasValue ? Math.Max(0m, ask.Value - .02m) : null, BestAsk: ask, BidSize: 100m, AskSize: askSize, FetchedAtUtc: fetchedAtUtc ?? Now));

    private static FootballContract ExactScore(int home, int away) => new()
    {
        FixtureId = "fixture-1",
        Identity = new PolymarketContractIdentity(TokenId: $"exact-{home}-{away}", OutcomeSide: $"{home}-{away}"),
        MarketType = FootballMarketType.ExactScore,
        Selection = ContractSelection.Yes,
        ExactHomeGoals = home,
        ExactAwayGoals = away,
        Label = $"Exact Score {home}-{away}"
    };

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

    private static ScorelineDistribution Distribution() => ProbabilityHelper.PoissonScoreline(1.8, .8, maxGoals: 5);

    private static LiveComplementaryEventState PreGameState() => new(
        "PRE_GAME",
        "PRE_GAME_ONLY",
        AllowsCandidatePricing: true,
        IsInPlay: false,
        RawScore: null,
        HomeGoals: null,
        AwayGoals: null,
        RawElapsed: null,
        ElapsedMinute: null,
        StartTimeUtc: DateTimeOffset.Parse("2026-06-16T19:00:00Z"),
        "pre-game only; no live score-state applied");

    private static LiveComplementaryGoalDecay PreGameDecay() => new(
        "PRE_GAME_NO_DECAY",
        Applied: false,
        Minute: null,
        RemainingFraction: 1.0,
        KickoffHomeExpectedGoals: null,
        KickoffAwayExpectedGoals: null,
        RemainingHomeExpectedGoals: null,
        RemainingAwayExpectedGoals: null,
        RetainedGridMass: 1.0,
        "pre-game distribution retained; no elapsed-minute decay applied");

    private static LiveComplementaryScoreConditioning PreGameConditioning() => new(
        "PRE_GAME_UNCONDITIONED",
        Applied: false,
        CurrentHomeGoals: null,
        CurrentAwayGoals: null,
        OriginalStateCount: 36,
        RetainedStateCount: 36,
        RemovedImpossibleStateCount: 0,
        RetainedProbabilityMass: 1.0,
        "full pre-game score grid retained");
}
