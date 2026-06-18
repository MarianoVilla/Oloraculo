using Oloraculo.Web.ComboLab.Candidates;
using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Tests.ComboLab;

public class ComplementaryLockCandidateGeneratorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-16T18:00:00Z");

    [Fact]
    public void GeneratesRankedWatchOnlyLockCandidatesWithMaxHedgePrice()
    {
        var distribution = DistributionWithMaxGoals(4,
            ((0, 0), .20),
            ((1, 0), .20),
            ((1, 1), .30),
            ((2, 1), .20),
            ((3, 1), .10));
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, "btts_yes", tokenId: "btts-yes"), ask: .46m),
            Quoted(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.No, "btts_no", tokenId: "btts-no"), ask: .47m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "under_1_5", line: 1.5m, tokenId: "under15"), ask: .30m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "over_3_5", line: 3.5m, tokenId: "over35"), ask: .20m)
        };

        var result = new ComplementaryLockCandidateGenerator().Generate(distribution, quoted, new ComplementaryLockCandidateGeneratorOptions
        {
            AsOfUtc = Now,
            SharesPerLeg = 10m,
            ExecutionBuffer = .05m,
            MaxCandidates = 8
        });

        Assert.Empty(result.Rejects);
        Assert.Equal(ComplementaryLockVerdict.PositiveLock, result.Candidates[0].Verdict);
        Assert.Equal("exact complement: BothTeamsToScore", result.Candidates[0].StructureLabel);
        Assert.Equal(.535m, result.Candidates[0].MaxSecondLegAskForLock, 6);
        Assert.Equal(.65m, result.Candidates[0].LockedProfit, 6);
        Assert.Equal(1.0, result.Candidates[0].ChanceToLock, 6);
        Assert.Contains(result.Candidates, candidate => candidate.Verdict == ComplementaryLockVerdict.CorrelatedOnly && candidate.GapStates.Count > 0);
    }

    [Fact]
    public void NestedTotalsRankAsPositiveLockWhenTheyCoverEveryTerminalScoreline()
    {
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "over_1_5", line: 1.5m, tokenId: "over15"), ask: .50m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "under_3_5", line: 3.5m, tokenId: "under35"), ask: .35m)
        };

        var result = new ComplementaryLockCandidateGenerator().Generate(DistributionWithMaxGoals(4, ((0, 0), .1), ((1, 1), .5), ((4, 0), .4)), quoted, new ComplementaryLockCandidateGeneratorOptions
        {
            AsOfUtc = Now
        });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(ComplementaryLockVerdict.PositiveLock, candidate.Verdict);
        Assert.Equal("nested total band", candidate.StructureLabel);
        Assert.True(candidate.OverlapStates.Count > 0);
        Assert.Equal(.15m, candidate.LockedProfit, 6);
    }

    [Fact]
    public void RejectsMissingAskThinAskStaleQuoteAndSameTokenPairs()
    {
        var quoted = new[]
        {
            Quoted(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, "no_ask", tokenId: "no-ask"), ask: null),
            Quoted(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.No, "thin", tokenId: "thin"), ask: .50m, askSize: 1m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "stale", line: 1.5m, tokenId: "stale"), ask: .50m, fetchedAtUtc: Now.AddMinutes(-10)),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "same_a", line: 1.5m, tokenId: "same"), ask: .50m),
            Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "same_b", line: 1.5m, tokenId: "same"), ask: .50m)
        };

        var result = new ComplementaryLockCandidateGenerator().Generate(DistributionWithMaxGoals(2, ((0, 0), 1.0)), quoted, new ComplementaryLockCandidateGeneratorOptions
        {
            AsOfUtc = Now,
            SharesPerLeg = 2m,
            MaxQuoteAge = TimeSpan.FromMinutes(5)
        });

        Assert.Empty(result.Candidates);
        AssertReject(result, ComplementaryLockCandidateRejectReason.MissingExecutableAsk, "no-ask");
        AssertReject(result, ComplementaryLockCandidateRejectReason.InsufficientAskSize, "thin");
        AssertReject(result, ComplementaryLockCandidateRejectReason.StaleQuote, "stale");
        AssertReject(result, ComplementaryLockCandidateRejectReason.SameTokenPair, "same");
    }

    [Fact]
    public void SandboxRowsExposeWatchOnlyMetrics()
    {
        var rows = Oloraculo.Web.ComboLab.Monitor.ComplementaryLockSandbox.BuildRows();

        Assert.NotEmpty(rows);
        Assert.Contains(rows, row => row.Verdict == ComplementaryLockVerdict.PositiveLock.ToString() && row.LockedProfit > 0);
        Assert.Contains(rows, row => row.Blocker.Contains("coverage gap", StringComparison.OrdinalIgnoreCase));
        Assert.All(rows, row => Assert.InRange(row.MaxSecondLegAsk, 0m, 1m));
    }

    private static ContractQuote Quoted(
        FootballContract contract,
        decimal? ask,
        decimal bid = .20m,
        decimal askSize = 10m,
        DateTimeOffset? fetchedAtUtc = null) => new(
        contract,
        new MarketQuote(bid, ask, BidSize: 10m, AskSize: askSize, FetchedAtUtc: fetchedAtUtc ?? Now));

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

    private static void AssertReject(ComplementaryLockCandidateGenerationResult result, ComplementaryLockCandidateRejectReason reason, string tokenId) =>
        Assert.Contains(result.Rejects, reject => reject.Reason == reason && reject.TokenId == tokenId);
}
