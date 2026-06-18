using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.ComboLab.Candidates
{
    public enum ComplementaryLockCandidateRejectReason
    {
        MissingContract,
        MissingQuote,
        MissingExecutableAsk,
        InsufficientAskSize,
        StaleQuote,
        SameTokenPair,
        EvaluationFailed
    }

    public sealed record ComplementaryLockCandidateGeneratorOptions
    {
        public decimal SharesPerLeg { get; init; } = 1m;
        public decimal ExecutionBuffer { get; init; }
        public int MaxCandidates { get; init; } = 25;
        public int MaxGapStates { get; init; } = 5;
        public int MaxOverlapStates { get; init; } = 5;
        public TimeSpan? MaxQuoteAge { get; init; } = TimeSpan.FromMinutes(5);
        public DateTimeOffset? AsOfUtc { get; init; }
    }

    public sealed record ComplementaryLockCandidateLeg(
        FootballContract Contract,
        decimal Shares,
        decimal ExecutableAsk,
        decimal ExecutableNotional,
        DateTimeOffset QuoteFetchedAtUtc)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Contract.Label)
            ? $"{Contract.MarketType}:{Contract.Selection}"
            : Contract.Label;
    }

    public sealed record ComplementaryLockCandidate(
        string CandidateId,
        string StructureLabel,
        ComplementaryLockCandidateLeg FirstLeg,
        ComplementaryLockCandidateLeg SecondLeg,
        ComplementaryLockEvaluation Evaluation,
        decimal MaxSecondLegAskForLock,
        IReadOnlyList<ComplementaryLockState> GapStates,
        IReadOnlyList<ComplementaryLockState> OverlapStates)
    {
        public decimal NetCost => Evaluation.NetCost;
        public decimal LockedProfit => Evaluation.LockedProfit;
        public decimal ExpectedProfit => Evaluation.ExpectedProfit;
        public double ChanceToLock => Evaluation.ChanceToLock;
        public double GapProbability => Evaluation.GapProbability;
        public ComplementaryLockVerdict Verdict => Evaluation.Verdict;
    }

    public sealed record ComplementaryLockCandidateReject(
        ComplementaryLockCandidateRejectReason Reason,
        string Detail,
        string? TokenId = null,
        string? OutcomeSide = null,
        string? CandidateId = null);

    public sealed record ComplementaryLockCandidateGenerationResult(
        IReadOnlyList<ComplementaryLockCandidate> Candidates,
        IReadOnlyList<ComplementaryLockCandidateReject> Rejects);

    public sealed class ComplementaryLockCandidateGenerator
    {
        private readonly ComplementaryLockAnalyzer _analyzer;

        public ComplementaryLockCandidateGenerator(ComplementaryLockAnalyzer? analyzer = null) =>
            _analyzer = analyzer ?? new ComplementaryLockAnalyzer();

        public ComplementaryLockCandidateGenerationResult Generate(
            ScorelineDistribution distribution,
            IReadOnlyList<ContractQuote> quotedContracts,
            ComplementaryLockCandidateGeneratorOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(distribution);
            ArgumentNullException.ThrowIfNull(quotedContracts);
            var effectiveOptions = options ?? new ComplementaryLockCandidateGeneratorOptions();
            ValidateOptions(effectiveOptions);

            var rejects = new List<ComplementaryLockCandidateReject>();
            var legChoices = quotedContracts
                .SelectMany(quoted => BuildBuyChoices(quoted, effectiveOptions, rejects))
                .ToList();
            var candidates = new List<ComplementaryLockCandidate>();

            for (var first = 0; first < legChoices.Count; first++)
            {
                for (var second = first + 1; second < legChoices.Count; second++)
                {
                    var firstChoice = legChoices[first];
                    var secondChoice = legChoices[second];
                    var candidateId = CandidateId(firstChoice.Leg, secondChoice.Leg);
                    if (SameToken(firstChoice.Leg, secondChoice.Leg))
                    {
                        rejects.Add(new ComplementaryLockCandidateReject(
                            ComplementaryLockCandidateRejectReason.SameTokenPair,
                            "A lock candidate cannot use the same Polymarket token twice.",
                            firstChoice.Leg.Contract.Identity.TokenId,
                            firstChoice.Leg.Contract.Identity.OutcomeSide,
                            candidateId));
                        continue;
                    }

                    try
                    {
                        var evaluation = _analyzer.Evaluate(distribution, [firstChoice.Leg, secondChoice.Leg], effectiveOptions.ExecutionBuffer);
                        candidates.Add(new ComplementaryLockCandidate(
                            candidateId,
                            StructureLabel(firstChoice.Leg.Contract, secondChoice.Leg.Contract),
                            ToCandidateLeg(firstChoice.Leg, firstChoice.QuoteFetchedAtUtc),
                            ToCandidateLeg(secondChoice.Leg, secondChoice.QuoteFetchedAtUtc),
                            evaluation,
                            MaxSecondLegAskForLock(firstChoice.Leg, evaluation.Shares, effectiveOptions.ExecutionBuffer),
                            evaluation.GapStates.Take(effectiveOptions.MaxGapStates).ToList(),
                            evaluation.OverlapStates.Take(effectiveOptions.MaxOverlapStates).ToList()));
                    }
                    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
                    {
                        rejects.Add(new ComplementaryLockCandidateReject(
                            ComplementaryLockCandidateRejectReason.EvaluationFailed,
                            ex.Message,
                            CandidateId: candidateId));
                    }
                }
            }

            return new ComplementaryLockCandidateGenerationResult(
                candidates
                    .OrderBy(candidate => VerdictRank(candidate.Verdict))
                    .ThenByDescending(candidate => candidate.LockedProfit)
                    .ThenBy(candidate => candidate.GapProbability)
                    .ThenByDescending(candidate => candidate.ExpectedProfit)
                    .Take(effectiveOptions.MaxCandidates)
                    .ToList(),
                rejects);
        }

        private static void ValidateOptions(ComplementaryLockCandidateGeneratorOptions options)
        {
            if (options.SharesPerLeg <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Shares per leg must be positive.");
            if (options.ExecutionBuffer < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Execution buffer cannot be negative.");
            if (options.MaxCandidates <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxCandidates must be positive.");
            if (options.MaxGapStates < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxGapStates cannot be negative.");
            if (options.MaxOverlapStates < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxOverlapStates cannot be negative.");
            if (options.MaxQuoteAge.HasValue && options.MaxQuoteAge.Value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxQuoteAge cannot be negative.");
        }

        private static IEnumerable<LockLegChoice> BuildBuyChoices(
            ContractQuote quoted,
            ComplementaryLockCandidateGeneratorOptions options,
            List<ComplementaryLockCandidateReject> rejects)
        {
            if (quoted.Contract is null)
            {
                rejects.Add(new ComplementaryLockCandidateReject(ComplementaryLockCandidateRejectReason.MissingContract, "Quoted contract is missing."));
                yield break;
            }

            if (quoted.Quote is null)
            {
                rejects.Add(new ComplementaryLockCandidateReject(
                    ComplementaryLockCandidateRejectReason.MissingQuote,
                    "Quote is missing.",
                    quoted.Contract.Identity.TokenId,
                    quoted.Contract.Identity.OutcomeSide));
                yield break;
            }

            if (options.MaxQuoteAge is { } maxAge)
            {
                var asOf = options.AsOfUtc ?? DateTimeOffset.UtcNow;
                if (asOf - quoted.Quote.FetchedAtUtc > maxAge)
                {
                    rejects.Add(new ComplementaryLockCandidateReject(
                        ComplementaryLockCandidateRejectReason.StaleQuote,
                        $"Quote is older than the configured max age of {maxAge}.",
                        quoted.Contract.Identity.TokenId,
                        quoted.Contract.Identity.OutcomeSide));
                    yield break;
                }
            }

            if (quoted.Quote.BestAsk is null)
            {
                rejects.Add(new ComplementaryLockCandidateReject(
                    ComplementaryLockCandidateRejectReason.MissingExecutableAsk,
                    "Lock legs require an executable ask.",
                    quoted.Contract.Identity.TokenId,
                    quoted.Contract.Identity.OutcomeSide));
                yield break;
            }

            if (quoted.Quote.AskSize.HasValue && quoted.Quote.AskSize.Value < options.SharesPerLeg)
            {
                rejects.Add(new ComplementaryLockCandidateReject(
                    ComplementaryLockCandidateRejectReason.InsufficientAskSize,
                    "Lock leg shares exceed executable ask size.",
                    quoted.Contract.Identity.TokenId,
                    quoted.Contract.Identity.OutcomeSide));
                yield break;
            }

            yield return new LockLegChoice(ComboLeg.Buy(quoted.Contract, options.SharesPerLeg, quoted.Quote), quoted.Quote.FetchedAtUtc);
        }

        private static ComplementaryLockCandidateLeg ToCandidateLeg(ComboLeg leg, DateTimeOffset quoteFetchedAtUtc) => new(
            leg.Contract,
            leg.Shares,
            leg.ExecutablePrice,
            leg.Shares * leg.ExecutablePrice,
            quoteFetchedAtUtc);

        private static decimal MaxSecondLegAskForLock(ComboLeg firstLeg, decimal shares, decimal executionBuffer)
        {
            if (shares <= 0)
                return 0m;
            var maxAsk = 1m - firstLeg.ExecutablePrice - executionBuffer / shares;
            return Math.Max(0m, Math.Min(1m, maxAsk));
        }

        private static int VerdictRank(ComplementaryLockVerdict verdict) => verdict switch
        {
            ComplementaryLockVerdict.PositiveLock => 0,
            ComplementaryLockVerdict.LockNoProfit => 1,
            ComplementaryLockVerdict.CorrelatedOnly => 2,
            _ => 9
        };

        private static bool SameToken(ComboLeg first, ComboLeg second) =>
            !string.IsNullOrWhiteSpace(first.Contract.Identity.TokenId) &&
            string.Equals(first.Contract.Identity.TokenId, second.Contract.Identity.TokenId, StringComparison.OrdinalIgnoreCase);

        private static string CandidateId(ComboLeg first, ComboLeg second) =>
            string.Join("__", new[] { LegId(first), LegId(second) }.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

        private static string LegId(ComboLeg leg) =>
            !string.IsNullOrWhiteSpace(leg.Contract.Identity.TokenId)
                ? leg.Contract.Identity.TokenId!
                : $"{leg.Contract.MarketType}:{leg.Contract.Selection}:{leg.Contract.Team}:{leg.Contract.Line}:{leg.Contract.Label}";

        private static string StructureLabel(FootballContract first, FootballContract second)
        {
            if (IsExactComplement(first, second))
                return $"exact complement: {first.MarketType}";
            if (IsNestedTotalBand(first, second))
                return "nested total band";
            if (first.MarketType == FootballMarketType.ExactScore || second.MarketType == FootballMarketType.ExactScore)
                return "modeled-grid exact-score relation";
            return "correlated scoreline pair";
        }

        private static bool IsExactComplement(FootballContract first, FootballContract second)
        {
            if (first.MarketType != second.MarketType)
                return false;

            return first.MarketType switch
            {
                FootballMarketType.BothTeamsToScore => IsOpposite(first.Selection, second.Selection, ContractSelection.Yes, ContractSelection.No),
                FootballMarketType.MatchTotal => first.Line == second.Line && IsOpposite(first.Selection, second.Selection, ContractSelection.Over, ContractSelection.Under),
                FootballMarketType.TeamTotal => first.Team == second.Team && first.Line == second.Line && IsOpposite(first.Selection, second.Selection, ContractSelection.Over, ContractSelection.Under),
                FootballMarketType.Spread => first.Team == second.Team && first.Line == second.Line && IsOpposite(first.Selection, second.Selection, ContractSelection.Yes, ContractSelection.No),
                FootballMarketType.Moneyline => IsMoneylineComplement(first.Selection, second.Selection),
                _ => false
            };
        }

        private static bool IsNestedTotalBand(FootballContract first, FootballContract second)
        {
            if (first.MarketType != second.MarketType || first.Line is null || second.Line is null)
                return false;
            if (first.MarketType is not FootballMarketType.MatchTotal and not FootballMarketType.TeamTotal)
                return false;
            if (first.MarketType == FootballMarketType.TeamTotal && first.Team != second.Team)
                return false;

            var overLine = first.Selection == ContractSelection.Over ? first.Line : second.Selection == ContractSelection.Over ? second.Line : null;
            var underLine = first.Selection == ContractSelection.Under ? first.Line : second.Selection == ContractSelection.Under ? second.Line : null;
            return overLine.HasValue && underLine.HasValue && overLine.Value < underLine.Value;
        }

        private static bool IsOpposite(ContractSelection first, ContractSelection second, ContractSelection yes, ContractSelection no) =>
            (first == yes && second == no) || (first == no && second == yes);

        private static bool IsMoneylineComplement(ContractSelection first, ContractSelection second) =>
            IsOpposite(first, second, ContractSelection.Home, ContractSelection.NotHome) ||
            IsOpposite(first, second, ContractSelection.Draw, ContractSelection.NotDraw) ||
            IsOpposite(first, second, ContractSelection.Away, ContractSelection.NotAway);

        private sealed record LockLegChoice(ComboLeg Leg, DateTimeOffset QuoteFetchedAtUtc);
    }
}
