using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.ComboLab.Candidates
{
    public enum FootballStrategyCandidateRejectReason
    {
        MissingQuote,
        MissingExecutableAsk,
        InsufficientAskSize,
        StaleQuote,
        DuplicateToken,
        EvaluationFailed
    }

    public sealed record FootballStrategyCandidateGeneratorOptions
    {
        public decimal SharesPerLeg { get; init; } = 1m;
        public decimal ExecutionBuffer { get; init; }
        public int MaxCandidates { get; init; } = 40;
        public int MaxLegs { get; init; } = 12;
        public TimeSpan? MaxQuoteAge { get; init; } = TimeSpan.FromMinutes(5);
        public DateTimeOffset? AsOfUtc { get; init; }
    }

    public sealed record FootballStrategyCandidateLeg(
        FootballContract Contract,
        decimal Shares,
        decimal ExecutableAsk,
        decimal ExecutableNotional,
        DateTimeOffset QuoteFetchedAtUtc)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Contract.Label)
            ? $"{Contract.MarketType}:{Contract.Selection}:{Contract.Line}"
            : Contract.Label;
    }

    public sealed record FootballStrategyCandidate(
        string CandidateId,
        string StrategyKind,
        string StructureLabel,
        IReadOnlyList<FootballStrategyCandidateLeg> Legs,
        FootballStrategyEvaluation Evaluation,
        double ScorelessThetaPerMinute,
        double GoalJumpExposure)
    {
        public FootballStrategyVerdict Verdict => Evaluation.Verdict;
        public decimal NetCost => Evaluation.NetCost;
        public decimal LockedProfit => Evaluation.MinNetPnl;
        public decimal ExpectedProfit => Evaluation.ExpectedPnl;
        public double GapProbability => Evaluation.GapProbability;
        public decimal WorstGapLoss => Evaluation.WorstGapLoss;
    }

    public sealed record FootballStrategyCandidateReject(
        FootballStrategyCandidateRejectReason Reason,
        string Detail,
        string? TokenId = null,
        string? OutcomeSide = null,
        string? CandidateId = null);

    public sealed record FootballStrategyCandidateGenerationResult(
        IReadOnlyList<FootballStrategyCandidate> Candidates,
        IReadOnlyList<FootballStrategyCandidateReject> Rejects);

    public sealed class FootballStrategyCandidateGenerator
    {
        private readonly FootballStrategyAnalyzer _analyzer;

        public FootballStrategyCandidateGenerator(FootballStrategyAnalyzer? analyzer = null) =>
            _analyzer = analyzer ?? new FootballStrategyAnalyzer();

        public FootballStrategyCandidateGenerationResult Generate(
            ScorelineDistribution distribution,
            IReadOnlyList<ContractQuote> quotedContracts,
            FootballStrategyCandidateGeneratorOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(distribution);
            ArgumentNullException.ThrowIfNull(quotedContracts);
            var effectiveOptions = options ?? new FootballStrategyCandidateGeneratorOptions();
            ValidateOptions(effectiveOptions);

            var rejects = new List<FootballStrategyCandidateReject>();
            var choices = quotedContracts
                .SelectMany(quoted => BuildBuyChoices(quoted, effectiveOptions, rejects))
                .DistinctBy(choice => TokenKey(choice.Leg), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidates = new List<FootballStrategyCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var impliedLambda = InferMarketImpliedTotalGoals(choices);

            foreach (var strategy in EnumerateStrategies(choices, effectiveOptions))
                TryAddCandidate(distribution, strategy, effectiveOptions, impliedLambda, candidates, rejects, seen);

            return new FootballStrategyCandidateGenerationResult(
                candidates
                    .OrderBy(candidate => VerdictRank(candidate.Verdict))
                    .ThenByDescending(candidate => candidate.LockedProfit)
                    .ThenBy(candidate => candidate.GapProbability)
                    .ThenByDescending(candidate => candidate.ExpectedProfit)
                    .ThenBy(candidate => Math.Abs(candidate.GoalJumpExposure))
                    .Take(effectiveOptions.MaxCandidates)
                    .ToList(),
                rejects);
        }

        private static void ValidateOptions(FootballStrategyCandidateGeneratorOptions options)
        {
            if (options.SharesPerLeg <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Shares per leg must be positive.");
            if (options.ExecutionBuffer < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Execution buffer cannot be negative.");
            if (options.MaxCandidates <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxCandidates must be positive.");
            if (options.MaxLegs <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxLegs must be positive.");
            if (options.MaxQuoteAge.HasValue && options.MaxQuoteAge.Value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxQuoteAge cannot be negative.");
        }

        private static IEnumerable<BuyLegChoice> BuildBuyChoices(
            ContractQuote quoted,
            FootballStrategyCandidateGeneratorOptions options,
            List<FootballStrategyCandidateReject> rejects)
        {
            if (quoted.Quote is null)
            {
                rejects.Add(new FootballStrategyCandidateReject(FootballStrategyCandidateRejectReason.MissingQuote, "Quote is missing.", quoted.Contract.Identity.TokenId, quoted.Contract.Identity.OutcomeSide));
                yield break;
            }

            if (options.MaxQuoteAge is { } maxAge)
            {
                var asOf = options.AsOfUtc ?? DateTimeOffset.UtcNow;
                if (asOf - quoted.Quote.FetchedAtUtc > maxAge)
                {
                    rejects.Add(new FootballStrategyCandidateReject(FootballStrategyCandidateRejectReason.StaleQuote, $"Quote is older than {maxAge}.", quoted.Contract.Identity.TokenId, quoted.Contract.Identity.OutcomeSide));
                    yield break;
                }
            }

            if (quoted.Quote.BestAsk is null)
            {
                rejects.Add(new FootballStrategyCandidateReject(FootballStrategyCandidateRejectReason.MissingExecutableAsk, "Strategy legs require executable asks.", quoted.Contract.Identity.TokenId, quoted.Contract.Identity.OutcomeSide));
                yield break;
            }

            if (quoted.Quote.AskSize.HasValue && quoted.Quote.AskSize.Value < options.SharesPerLeg)
            {
                rejects.Add(new FootballStrategyCandidateReject(FootballStrategyCandidateRejectReason.InsufficientAskSize, "Leg shares exceed executable ask size.", quoted.Contract.Identity.TokenId, quoted.Contract.Identity.OutcomeSide));
                yield break;
            }

            yield return new BuyLegChoice(ComboLeg.Buy(quoted.Contract, options.SharesPerLeg, quoted.Quote), quoted.Quote.FetchedAtUtc);
        }

        private static IEnumerable<StrategySpec> EnumerateStrategies(IReadOnlyList<BuyLegChoice> choices, FootballStrategyCandidateGeneratorOptions options)
        {
            foreach (var strategy in TotalBandStrategies(choices, FootballMarketType.MatchTotal, "total-band"))
                yield return strategy;
            foreach (var strategy in TotalBandStrategies(choices, FootballMarketType.TeamTotal, "team-total-band"))
                yield return strategy;
            foreach (var strategy in BttsTotalCoverStrategies(choices))
                yield return strategy;
            foreach (var strategy in MoneylineStrategies(choices))
                yield return strategy;
            foreach (var strategy in ExactScoreTotalCoverStrategies(choices, options.MaxLegs))
                yield return strategy;
        }

        private static IEnumerable<StrategySpec> TotalBandStrategies(IReadOnlyList<BuyLegChoice> choices, FootballMarketType marketType, string kind)
        {
            var groups = choices
                .Where(choice => choice.Leg.Contract.MarketType == marketType && choice.Leg.Contract.Line.HasValue)
                .GroupBy(choice => marketType == FootballMarketType.TeamTotal ? $"{choice.Leg.Contract.MarketType}:{choice.Leg.Contract.Team}" : choice.Leg.Contract.MarketType.ToString());

            foreach (var group in groups)
            {
                var overs = group.Where(choice => choice.Leg.Contract.Selection == ContractSelection.Over).ToList();
                var unders = group.Where(choice => choice.Leg.Contract.Selection == ContractSelection.Under).ToList();
                foreach (var over in overs)
                {
                    foreach (var under in unders)
                    {
                        if (over.Leg.Contract.Line!.Value <= under.Leg.Contract.Line!.Value)
                        {
                            var label = over.Leg.Contract.Line == under.Leg.Contract.Line
                                ? $"exact total complement {over.Leg.Contract.Line:0.0}"
                                : $"nested total band {over.Leg.Contract.Line:0.0}-{under.Leg.Contract.Line:0.0}";
                            yield return new StrategySpec(kind, label, [over, under]);
                        }
                    }
                }
            }
        }

        private static IEnumerable<StrategySpec> BttsTotalCoverStrategies(IReadOnlyList<BuyLegChoice> choices)
        {
            var bttsNo = choices.Where(choice => choice.Leg.Contract.MarketType == FootballMarketType.BothTeamsToScore && choice.Leg.Contract.Selection == ContractSelection.No).ToList();
            var safeOvers = choices.Where(choice =>
                choice.Leg.Contract.MarketType == FootballMarketType.MatchTotal &&
                choice.Leg.Contract.Selection == ContractSelection.Over &&
                choice.Leg.Contract.Line.HasValue &&
                choice.Leg.Contract.Line.Value <= 1.5m).ToList();

            foreach (var no in bttsNo)
                foreach (var over in safeOvers)
                    yield return new StrategySpec("btts-total-cover", $"BTTS No + Over {over.Leg.Contract.Line:0.0}", [no, over]);
        }

        private static IEnumerable<StrategySpec> MoneylineStrategies(IReadOnlyList<BuyLegChoice> choices)
        {
            var moneyline = choices.Where(choice => choice.Leg.Contract.MarketType == FootballMarketType.Moneyline).ToList();
            foreach (var group in moneyline.GroupBy(choice => choice.Leg.Contract.FixtureId))
            {
                BuyLegChoice? Find(ContractSelection selection) => group.FirstOrDefault(choice => choice.Leg.Contract.Selection == selection);
                var home = Find(ContractSelection.Home);
                var draw = Find(ContractSelection.Draw);
                var away = Find(ContractSelection.Away);
                if (home is not null && draw is not null && away is not null)
                    yield return new StrategySpec("moneyline-partition", "Home + Draw + Away partition", [home, draw, away]);

                foreach (var pair in new[]
                {
                    (ContractSelection.Home, ContractSelection.NotHome, "Home + NotHome"),
                    (ContractSelection.Draw, ContractSelection.NotDraw, "Draw + NotDraw"),
                    (ContractSelection.Away, ContractSelection.NotAway, "Away + NotAway"),
                    (ContractSelection.NotHome, ContractSelection.NotAway, "NotHome + NotAway draw middle"),
                    (ContractSelection.NotHome, ContractSelection.NotDraw, "NotHome + NotDraw away middle"),
                    (ContractSelection.NotAway, ContractSelection.NotDraw, "NotAway + NotDraw home middle")
                })
                {
                    var first = Find(pair.Item1);
                    var second = Find(pair.Item2);
                    if (first is not null && second is not null)
                        yield return new StrategySpec("moneyline-complement", pair.Item3, [first, second]);
                }
            }
        }

        private static IEnumerable<StrategySpec> ExactScoreTotalCoverStrategies(IReadOnlyList<BuyLegChoice> choices, int maxLegs)
        {
            var exactYes = choices
                .Where(choice => choice.Leg.Contract.MarketType == FootballMarketType.ExactScore && choice.Leg.Contract.Selection == ContractSelection.Yes)
                .ToDictionary(choice => (choice.Leg.Contract.ExactHomeGoals ?? -1, choice.Leg.Contract.ExactAwayGoals ?? -1), choice => choice);
            if (exactYes.Count == 0)
                yield break;

            var overTotals = choices.Where(choice =>
                choice.Leg.Contract.MarketType == FootballMarketType.MatchTotal &&
                choice.Leg.Contract.Selection == ContractSelection.Over &&
                choice.Leg.Contract.Line.HasValue).ToList();

            foreach (var over in overTotals)
            {
                var floor = (int)Math.Floor(over.Leg.Contract.Line!.Value);
                var required = new List<BuyLegChoice>();
                var complete = true;
                for (var home = 0; home <= floor; home++)
                {
                    for (var away = 0; away <= floor - home; away++)
                    {
                        if (exactYes.TryGetValue((home, away), out var exact))
                        {
                            required.Add(exact);
                            continue;
                        }
                        complete = false;
                    }
                }

                if (complete && required.Count + 1 <= maxLegs)
                    yield return new StrategySpec("exact-score-total-cover", $"Over {over.Leg.Contract.Line:0.0} + exact scores <= {floor}", [over, .. required]);
            }
        }

        private void TryAddCandidate(
            ScorelineDistribution distribution,
            StrategySpec strategy,
            FootballStrategyCandidateGeneratorOptions options,
            double? impliedLambda,
            List<FootballStrategyCandidate> candidates,
            List<FootballStrategyCandidateReject> rejects,
            HashSet<string> seen)
        {
            var candidateId = CandidateId(strategy.Legs.Select(choice => choice.Leg));
            if (!seen.Add(candidateId))
                return;
            if (strategy.Legs.Select(choice => TokenKey(choice.Leg)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != strategy.Legs.Count)
            {
                rejects.Add(new FootballStrategyCandidateReject(FootballStrategyCandidateRejectReason.DuplicateToken, "Strategy includes the same token twice.", CandidateId: candidateId));
                return;
            }

            try
            {
                var legs = strategy.Legs.Select(choice => choice.Leg).ToList();
                var evaluation = _analyzer.Evaluate(distribution, legs, options.ExecutionBuffer);
                var (theta, jump) = TotalExposure(legs, impliedLambda);
                candidates.Add(new FootballStrategyCandidate(
                    candidateId,
                    strategy.Kind,
                    strategy.Label,
                    strategy.Legs.Select(ToCandidateLeg).ToList(),
                    evaluation,
                    theta,
                    jump));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                rejects.Add(new FootballStrategyCandidateReject(FootballStrategyCandidateRejectReason.EvaluationFailed, ex.Message, CandidateId: candidateId));
            }
        }

        private static (double Theta, double Jump) TotalExposure(IReadOnlyList<ComboLeg> legs, double? impliedLambda)
        {
            if (!impliedLambda.HasValue)
                return (0, 0);
            var theta = 0.0;
            var jump = 0.0;
            foreach (var leg in legs.Where(leg => leg.Contract.MarketType == FootballMarketType.MatchTotal && leg.Contract.Line.HasValue))
            {
                var point = PoissonGoalDecayModel.Point(impliedLambda.Value, (double)leg.Contract.Line!.Value, minute: 0, currentGoals: 0);
                var shares = (double)leg.Shares;
                if (leg.Contract.Selection == ContractSelection.Over)
                {
                    theta -= shares * point.OverDecayPerScorelessMinute;
                    jump += shares * point.OverGoalJump;
                }
                else if (leg.Contract.Selection == ContractSelection.Under)
                {
                    theta += shares * point.UnderGainPerScorelessMinute;
                    jump += shares * point.UnderGoalJump;
                }
            }
            return (theta, jump);
        }

        private static double? InferMarketImpliedTotalGoals(IReadOnlyList<BuyLegChoice> choices)
        {
            var totalGroups = choices
                .Where(choice => choice.Leg.Contract.MarketType == FootballMarketType.MatchTotal && choice.Leg.Contract.Line.HasValue)
                .GroupBy(choice => choice.Leg.Contract.Line!.Value)
                .OrderBy(group => Math.Abs((double)(group.Key - 2.5m)))
                .ThenBy(group => group.Key);

            foreach (var group in totalGroups)
            {
                var over = group.FirstOrDefault(choice => choice.Leg.Contract.Selection == ContractSelection.Over);
                var under = group.FirstOrDefault(choice => choice.Leg.Contract.Selection == ContractSelection.Under);
                if (over is null || under is null)
                    continue;
                var overProbability = PoissonGoalDecayModel.NormalizeBinaryPrice(over.Leg.ExecutablePrice, under.Leg.ExecutablePrice);
                return PoissonGoalDecayModel.ImpliedLambdaForOver((double)group.Key, overProbability);
            }
            return null;
        }

        private static FootballStrategyCandidateLeg ToCandidateLeg(BuyLegChoice choice) => new(
            choice.Leg.Contract,
            choice.Leg.Shares,
            choice.Leg.ExecutablePrice,
            choice.Leg.Shares * choice.Leg.ExecutablePrice,
            choice.QuoteFetchedAtUtc);

        private static int VerdictRank(FootballStrategyVerdict verdict) => verdict switch
        {
            FootballStrategyVerdict.TruePositiveLock => 0,
            FootballStrategyVerdict.BreakEvenLock => 1,
            FootballStrategyVerdict.MiddleHedge => 2,
            FootballStrategyVerdict.GapHedge => 3,
            FootballStrategyVerdict.CorrelatedOnly => 4,
            _ => 9
        };

        private static string CandidateId(IEnumerable<ComboLeg> legs) =>
            string.Join("__", legs.Select(TokenKey).OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

        private static string TokenKey(ComboLeg leg) =>
            !string.IsNullOrWhiteSpace(leg.Contract.Identity.TokenId)
                ? leg.Contract.Identity.TokenId!
                : $"{leg.Contract.MarketType}:{leg.Contract.Selection}:{leg.Contract.Team}:{leg.Contract.Line}:{leg.Contract.ExactHomeGoals}:{leg.Contract.ExactAwayGoals}:{leg.Contract.Label}";

        private sealed record BuyLegChoice(ComboLeg Leg, DateTimeOffset QuoteFetchedAtUtc);

        private sealed record StrategySpec(string Kind, string Label, IReadOnlyList<BuyLegChoice> Legs);
    }
}
