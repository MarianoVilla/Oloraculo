using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.ComboLab.Pricing
{
    public enum ComplementaryLockCoverageScope
    {
        TerminalScoreline,
        ModeledScorelineGrid
    }

    public enum ComplementaryLockVerdict
    {
        PositiveLock,
        LockNoProfit,
        CorrelatedOnly
    }

    public sealed record ComplementaryLockState(
        int HomeGoals,
        int AwayGoals,
        double Probability,
        decimal GrossPayout,
        decimal NetPnl,
        IReadOnlyList<string> LegResults);

    public sealed record ComplementaryLockEvaluation(
        decimal Shares,
        decimal NetCost,
        decimal ExecutionBuffer,
        decimal MinTerminalPayout,
        decimal MaxTerminalPayout,
        decimal LockedProfit,
        decimal ExpectedPayout,
        decimal ExpectedProfit,
        double ChanceToLock,
        double GapProbability,
        double OverlapProbability,
        decimal WorstGapLoss,
        ComplementaryLockCoverageScope CoverageScope,
        ComplementaryLockVerdict Verdict,
        IReadOnlyList<ComplementaryLockState> GapStates,
        IReadOnlyList<ComplementaryLockState> OverlapStates)
    {
        public bool HasCoverageGap => GapStates.Count > 0;
        public bool IsTerminalLock => !HasCoverageGap && CoverageScope == ComplementaryLockCoverageScope.TerminalScoreline;
        public bool IsPositive => LockedProfit > 0;
    }

    public sealed class ComplementaryLockAnalyzer
    {
        public ComplementaryLockEvaluation Evaluate(
            ScorelineDistribution distribution,
            IReadOnlyList<ComboLeg> legs,
            decimal executionBuffer = 0m)
        {
            ArgumentNullException.ThrowIfNull(distribution);
            ArgumentNullException.ThrowIfNull(legs);
            if (legs.Count != 2)
                throw new ArgumentException("Complementary lock analysis requires exactly two legs.", nameof(legs));
            if (executionBuffer < 0)
                throw new ArgumentOutOfRangeException(nameof(executionBuffer), "Execution buffer cannot be negative.");
            ValidateBuyLockLegs(legs);

            var shares = legs[0].Shares;
            var masks = legs
                .Select(leg => ScorelinePayoffMaskFactory.Build(leg.Contract, distribution.MaxGoals))
                .ToArray();
            var netCost = legs.Sum(leg => leg.Shares * leg.ExecutablePrice);
            var costWithBuffer = netCost + executionBuffer;
            var expectedPayout = 0m;
            var minPayout = decimal.MaxValue;
            var maxPayout = decimal.MinValue;
            var chanceToLock = 0.0;
            var gapProbability = 0.0;
            var overlapProbability = 0.0;
            var gaps = new List<ComplementaryLockState>();
            var overlaps = new List<ComplementaryLockState>();

            for (var home = 0; home <= distribution.MaxGoals; home++)
            {
                for (var away = 0; away <= distribution.MaxGoals; away++)
                {
                    var probability = distribution.Probability(home, away);
                    if (distribution.IgnoreZeroProbabilityStatesForEvaluation && probability <= 1e-12)
                        continue;

                    var grossPayout = 0m;
                    var legResults = new List<string>(legs.Count);

                    for (var i = 0; i < legs.Count; i++)
                    {
                        var pays = masks[i].Pays(home, away);
                        if (pays)
                            grossPayout += legs[i].Shares;
                        legResults.Add($"{Describe(legs[i].Contract)}:{(pays ? "pays" : "loses")}");
                    }

                    var netPnl = grossPayout - costWithBuffer;
                    expectedPayout += (decimal)probability * grossPayout;
                    minPayout = Math.Min(minPayout, grossPayout);
                    maxPayout = Math.Max(maxPayout, grossPayout);

                    var state = new ComplementaryLockState(home, away, probability, grossPayout, netPnl, legResults);
                    if (grossPayout < shares)
                    {
                        gapProbability += probability;
                        gaps.Add(state);
                    }
                    else
                    {
                        chanceToLock += probability;
                    }

                    if (grossPayout > shares)
                    {
                        overlapProbability += probability;
                        overlaps.Add(state);
                    }
                }
            }

            if (minPayout == decimal.MaxValue)
                minPayout = 0m;
            if (maxPayout == decimal.MinValue)
                maxPayout = 0m;

            var lockedProfit = minPayout - costWithBuffer;
            var expectedProfit = expectedPayout - costWithBuffer;
            var scope = legs.Any(leg => leg.Contract.MarketType == FootballMarketType.ExactScore)
                ? ComplementaryLockCoverageScope.ModeledScorelineGrid
                : ComplementaryLockCoverageScope.TerminalScoreline;
            var verdict = gaps.Count > 0
                ? ComplementaryLockVerdict.CorrelatedOnly
                : lockedProfit > 0
                    ? ComplementaryLockVerdict.PositiveLock
                    : ComplementaryLockVerdict.LockNoProfit;

            return new ComplementaryLockEvaluation(
                shares,
                netCost,
                executionBuffer,
                minPayout,
                maxPayout,
                lockedProfit,
                expectedPayout,
                expectedProfit,
                chanceToLock,
                gapProbability,
                overlapProbability,
                Math.Max(0m, costWithBuffer - minPayout),
                scope,
                verdict,
                gaps.OrderByDescending(state => state.Probability).ThenBy(state => state.HomeGoals + state.AwayGoals).ToList(),
                overlaps.OrderByDescending(state => state.Probability).ThenBy(state => state.HomeGoals + state.AwayGoals).ToList());
        }

        private static void ValidateBuyLockLegs(IReadOnlyList<ComboLeg> legs)
        {
            foreach (var leg in legs)
            {
                ArgumentNullException.ThrowIfNull(leg.Contract);
                if (leg.Action != TradeAction.Buy)
                    throw new ArgumentException("Complementary locks are buy-buy structures; sell legs are not locks.", nameof(legs));
                if (leg.Shares <= 0)
                    throw new ArgumentOutOfRangeException(nameof(legs), "Leg shares must be positive.");
                if (leg.ExecutablePrice < 0 || leg.ExecutablePrice > 1)
                    throw new ArgumentOutOfRangeException(nameof(legs), "Executable price must be between 0 and 1.");
            }

            if (legs[0].Contract.FixtureId != legs[1].Contract.FixtureId)
                throw new ArgumentException("Complementary locks require both legs to resolve from the same fixture.", nameof(legs));
            if (legs[0].Shares != legs[1].Shares)
                throw new ArgumentException("Complementary lock v1 requires equal shares on both legs.", nameof(legs));
            if (!string.IsNullOrWhiteSpace(legs[0].Contract.Identity.TokenId) &&
                legs[0].Contract.Identity.TokenId == legs[1].Contract.Identity.TokenId)
            {
                throw new ArgumentException("Complementary locks cannot use the same Polymarket token twice.", nameof(legs));
            }
        }

        private static string Describe(FootballContract contract) =>
            string.IsNullOrWhiteSpace(contract.Label)
                ? $"{contract.MarketType}:{contract.Selection}"
                : contract.Label;
    }
}
