using Oloraculo.Web.ComboLab.Candidates;
using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.ComboLab.Monitor
{
    public sealed record ComplementaryLockSandboxRow(
        string Verdict,
        string Structure,
        string FirstLeg,
        string SecondLeg,
        decimal NetCost,
        decimal MaxSecondLegAsk,
        decimal LockedProfit,
        double ChanceToLock,
        double GapProbability,
        decimal WorstGapLoss,
        string Blocker);

    public static class ComplementaryLockSandbox
    {
        private static readonly DateTimeOffset AsOfUtc = DateTimeOffset.Parse("2026-06-16T18:00:00Z");

        public static IReadOnlyList<ComplementaryLockSandboxRow> BuildRows()
        {
            var distribution = DistributionWithMaxGoals(4,
                ((0, 0), .10),
                ((1, 0), .14),
                ((0, 1), .10),
                ((1, 1), .24),
                ((2, 1), .18),
                ((1, 2), .10),
                ((2, 2), .06),
                ((3, 1), .05),
                ((0, 2), .03));

            var quoted = new[]
            {
                Quoted(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.Yes, "BTTS yes", tokenId: "btts-yes"), .46m),
                Quoted(Contract(FootballMarketType.BothTeamsToScore, ContractSelection.No, "BTTS no", tokenId: "btts-no"), .47m),
                Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 1.5", line: 1.5m, tokenId: "over-15"), .51m),
                Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "Under 3.5", line: 3.5m, tokenId: "under-35"), .36m),
                Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Under, "Under 1.5", line: 1.5m, tokenId: "under-15"), .34m),
                Quoted(Contract(FootballMarketType.MatchTotal, ContractSelection.Over, "Over 3.5", line: 3.5m, tokenId: "over-35"), .23m)
            };

            var result = new ComplementaryLockCandidateGenerator().Generate(distribution, quoted, new ComplementaryLockCandidateGeneratorOptions
            {
                AsOfUtc = AsOfUtc,
                SharesPerLeg = 10m,
                ExecutionBuffer = .05m,
                MaxCandidates = 8,
                MaxQuoteAge = TimeSpan.FromMinutes(10)
            });

            return result.Candidates
                .Select(candidate => new ComplementaryLockSandboxRow(
                    candidate.Verdict.ToString(),
                    candidate.StructureLabel,
                    candidate.FirstLeg.DisplayName,
                    candidate.SecondLeg.DisplayName,
                    candidate.NetCost,
                    candidate.MaxSecondLegAskForLock,
                    candidate.LockedProfit,
                    candidate.ChanceToLock,
                    candidate.GapProbability,
                    candidate.Evaluation.WorstGapLoss,
                    Blocker(candidate)))
                .ToList();
        }

        private static string Blocker(ComplementaryLockCandidate candidate)
        {
            if (candidate.Verdict == ComplementaryLockVerdict.PositiveLock)
                return "none in deterministic example";
            if (candidate.Evaluation.HasCoverageGap)
            {
                var sample = candidate.GapStates.FirstOrDefault();
                return sample is null
                    ? "terminal coverage gap outside modeled probability mass"
                    : $"coverage gap e.g. {sample.HomeGoals}-{sample.AwayGoals}";
            }
            return "cost plus buffer removes lock profit";
        }

        private static ContractQuote Quoted(FootballContract contract, decimal ask) => new(
            contract,
            new MarketQuote(BestBid: Math.Max(0m, ask - .02m), BestAsk: ask, BidSize: 100m, AskSize: 100m, FetchedAtUtc: AsOfUtc));

        private static FootballContract Contract(
            FootballMarketType marketType,
            ContractSelection selection,
            string label,
            decimal? line = null,
            string tokenId = "token") => new()
            {
                FixtureId = "sandbox-fixture",
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
}
