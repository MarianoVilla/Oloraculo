using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.ComboLab.Pricing
{
    public enum FootballStrategyVerdict
    {
        TruePositiveLock,
        BreakEvenLock,
        MiddleHedge,
        GapHedge,
        CorrelatedOnly
    }

    public enum FootballStrategyCoverageScope
    {
        TerminalScoreline,
        ModeledScorelineGrid
    }

    public sealed record FootballStrategyState(
        int HomeGoals,
        int AwayGoals,
        double Probability,
        decimal GrossPayout,
        decimal NetPnl,
        IReadOnlyList<string> LegResults);

    public sealed record FootballStrategyEvaluation(
        int LegCount,
        decimal NetCost,
        decimal ExecutionBuffer,
        decimal ExpectedGrossPayout,
        decimal ExpectedPnl,
        decimal MinGrossPayout,
        decimal MaxGrossPayout,
        decimal MinNetPnl,
        decimal MaxNetPnl,
        double GapProbability,
        double ProbabilityNonNegative,
        double ProbabilityPositive,
        double OverlapProbability,
        decimal WorstGapLoss,
        FootballStrategyCoverageScope CoverageScope,
        FootballStrategyVerdict Verdict,
        IReadOnlyList<FootballStrategyState> States,
        IReadOnlyList<FootballStrategyState> GapStates,
        IReadOnlyList<FootballStrategyState> BestStates)
    {
        public bool HasCoverageGap => GapProbability > 0 || GapStates.Count > 0;
        public bool IsLock => Verdict is FootballStrategyVerdict.TruePositiveLock or FootballStrategyVerdict.BreakEvenLock;
    }

    public sealed class FootballStrategyAnalyzer
    {
        public FootballStrategyEvaluation Evaluate(
            ScorelineDistribution distribution,
            IReadOnlyList<ComboLeg> legs,
            decimal executionBuffer = 0m)
        {
            ArgumentNullException.ThrowIfNull(distribution);
            ArgumentNullException.ThrowIfNull(legs);
            if (legs.Count == 0)
                throw new ArgumentException("At least one strategy leg is required.", nameof(legs));
            if (executionBuffer < 0)
                throw new ArgumentOutOfRangeException(nameof(executionBuffer), "Execution buffer cannot be negative.");
            ValidateLegs(legs);

            var masks = legs.Select(leg => ScorelinePayoffMaskFactory.Build(leg.Contract, distribution.MaxGoals)).ToArray();
            var states = new List<FootballStrategyState>();
            var gaps = new List<FootballStrategyState>();
            var expectedGrossPayout = 0m;
            var expectedPnl = 0m;
            var gapProbability = 0.0;
            var probabilityNonNegative = 0.0;
            var probabilityPositive = 0.0;
            var overlapProbability = 0.0;
            var minGrossPayout = decimal.MaxValue;
            var maxGrossPayout = decimal.MinValue;
            var minNetPnl = decimal.MaxValue;
            var maxNetPnl = decimal.MinValue;
            var smallestPositivePayout = legs.Where(leg => leg.Action == TradeAction.Buy).Select(leg => leg.Shares).DefaultIfEmpty(0m).Min();

            for (var home = 0; home <= distribution.MaxGoals; home++)
            {
                for (var away = 0; away <= distribution.MaxGoals; away++)
                {
                    var probability = distribution.Probability(home, away);
                    if (distribution.IgnoreZeroProbabilityStatesForEvaluation && probability <= 1e-12)
                        continue;

                    var grossPayout = 0m;
                    var netPnl = -executionBuffer;
                    var legResults = new List<string>(legs.Count);

                    for (var i = 0; i < legs.Count; i++)
                    {
                        var leg = legs[i];
                        var pays = masks[i].Pays(home, away);
                        var payoffPerShare = pays ? 1m : 0m;
                        if (leg.Action == TradeAction.Buy)
                            grossPayout += leg.Shares * payoffPerShare;

                        netPnl += leg.Action switch
                        {
                            TradeAction.Buy => leg.Shares * (payoffPerShare - leg.ExecutablePrice),
                            TradeAction.Sell => leg.Shares * (leg.ExecutablePrice - payoffPerShare),
                            _ => throw new ArgumentOutOfRangeException(nameof(leg.Action), "Unsupported trade action.")
                        };
                        legResults.Add($"{Describe(leg.Contract)}:{(pays ? "pays" : "loses")}");
                    }

                    var state = new FootballStrategyState(home, away, probability, grossPayout, netPnl, legResults);
                    states.Add(state);
                    expectedGrossPayout += (decimal)probability * grossPayout;
                    expectedPnl += (decimal)probability * netPnl;
                    minGrossPayout = Math.Min(minGrossPayout, grossPayout);
                    maxGrossPayout = Math.Max(maxGrossPayout, grossPayout);
                    minNetPnl = Math.Min(minNetPnl, netPnl);
                    maxNetPnl = Math.Max(maxNetPnl, netPnl);

                    if (grossPayout <= 0)
                    {
                        gapProbability += probability;
                        gaps.Add(state);
                    }
                    if (netPnl >= 0)
                        probabilityNonNegative += probability;
                    if (netPnl > 0)
                        probabilityPositive += probability;
                    if (smallestPositivePayout > 0 && grossPayout > smallestPositivePayout)
                        overlapProbability += probability;
                }
            }

            if (states.Count == 0)
            {
                minGrossPayout = maxGrossPayout = minNetPnl = maxNetPnl = 0m;
            }

            var netCost = legs.Sum(leg => leg.Action == TradeAction.Buy
                ? leg.Shares * leg.ExecutablePrice
                : -leg.Shares * leg.ExecutablePrice) + executionBuffer;
            var verdict = Classify(minGrossPayout, minNetPnl, maxNetPnl, gapProbability);
            var scope = legs.Any(leg => leg.Contract.MarketType == FootballMarketType.ExactScore)
                ? FootballStrategyCoverageScope.ModeledScorelineGrid
                : FootballStrategyCoverageScope.TerminalScoreline;

            return new FootballStrategyEvaluation(
                legs.Count,
                netCost,
                executionBuffer,
                expectedGrossPayout,
                expectedPnl,
                minGrossPayout,
                maxGrossPayout,
                minNetPnl,
                maxNetPnl,
                gapProbability,
                probabilityNonNegative,
                probabilityPositive,
                overlapProbability,
                Math.Max(0m, -minNetPnl),
                scope,
                verdict,
                states,
                gaps.OrderByDescending(state => state.Probability).ThenBy(state => state.HomeGoals + state.AwayGoals).ToList(),
                states.OrderByDescending(state => state.NetPnl).ThenByDescending(state => state.Probability).Take(8).ToList());
        }

        private static FootballStrategyVerdict Classify(decimal minGrossPayout, decimal minNetPnl, decimal maxNetPnl, double gapProbability)
        {
            if (gapProbability <= 1e-12 && minGrossPayout > 0 && minNetPnl > 0)
                return FootballStrategyVerdict.TruePositiveLock;
            if (gapProbability <= 1e-12 && minGrossPayout > 0 && minNetPnl == 0)
                return FootballStrategyVerdict.BreakEvenLock;
            if (gapProbability <= 1e-12 && minGrossPayout > 0 && maxNetPnl > 0)
                return FootballStrategyVerdict.MiddleHedge;
            if (gapProbability > 0 && maxNetPnl > 0)
                return FootballStrategyVerdict.GapHedge;
            return FootballStrategyVerdict.CorrelatedOnly;
        }

        private static void ValidateLegs(IReadOnlyList<ComboLeg> legs)
        {
            var fixtureId = legs[0].Contract.FixtureId;
            var tokenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var leg in legs)
            {
                ArgumentNullException.ThrowIfNull(leg.Contract);
                if (leg.Contract.FixtureId != fixtureId)
                    throw new ArgumentException("Generalized football strategies require every leg to resolve from the same fixture.", nameof(legs));
                if (leg.Shares <= 0)
                    throw new ArgumentOutOfRangeException(nameof(legs), "Leg shares must be positive.");
                if (leg.ExecutablePrice < 0 || leg.ExecutablePrice > 1)
                    throw new ArgumentOutOfRangeException(nameof(legs), "Executable price must be between 0 and 1.");
                var tokenId = leg.Contract.Identity.TokenId;
                if (!string.IsNullOrWhiteSpace(tokenId) && !tokenIds.Add(tokenId))
                    throw new ArgumentException("A generalized strategy cannot include the same Polymarket token twice.", nameof(legs));
            }
        }

        private static string Describe(FootballContract contract) =>
            string.IsNullOrWhiteSpace(contract.Label)
                ? $"{contract.MarketType}:{contract.Selection}:{contract.Line}"
                : contract.Label;
    }
}
