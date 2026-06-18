using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.ComboLab.Candidates
{
    public enum ComboCandidateRejectReason
    {
        MissingContract,
        MissingQuote,
        MissingExecutableAsk,
        MissingExecutableBid,
        InsufficientAskSize,
        InsufficientBidSize,
        StaleQuote,
        SameTokenPair,
        EvaluationFailed
    }

    public sealed record ContractQuote(FootballContract Contract, MarketQuote Quote);

    public sealed record ComboCandidateGeneratorOptions
    {
        public decimal SharesPerLeg { get; init; } = 1m;
        public bool IncludeBuyLegs { get; init; } = true;
        public bool IncludeSellLegs { get; init; }
        public int MaxCandidates { get; init; } = 25;
        public int MaxBadHoleStates { get; init; } = 5;
        public TimeSpan? MaxQuoteAge { get; init; } = TimeSpan.FromMinutes(5);
        public DateTimeOffset? AsOfUtc { get; init; }
    }

    public sealed record ComboCandidateLeg(
        FootballContract Contract,
        TradeAction Action,
        decimal Shares,
        decimal ExecutablePrice,
        decimal ExecutableNotional,
        DateTimeOffset QuoteFetchedAtUtc)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(Contract.Label)
            ? $"{Contract.MarketType}:{Contract.Selection}"
            : Contract.Label;
    }

    public sealed record ComboBadHoleState(
        int HomeGoals,
        int AwayGoals,
        double Probability,
        decimal NetPnl,
        IReadOnlyList<string> LegResults);

    public sealed record ComboCandidate(
        string CandidateId,
        ComboCandidateLeg FirstLeg,
        ComboCandidateLeg SecondLeg,
        ComboEvaluation Evaluation,
        IReadOnlyList<ComboBadHoleState> BadHoleStates)
    {
        public decimal ExpectedPnl => Evaluation.ExpectedPnl;
        public decimal Roi => Evaluation.Roi;
        public decimal MaxLoss => Evaluation.MaxLoss;
        public decimal MaxProfit => Evaluation.MaxProfit;
        public double ProbabilityOfLoss => Evaluation.ProbabilityOfLoss;
        public decimal NetCost => Evaluation.NetCost;
    }

    public sealed record ComboCandidateReject(
        ComboCandidateRejectReason Reason,
        string Detail,
        string? TokenId = null,
        string? OutcomeSide = null,
        string? CandidateId = null);

    public sealed record ComboCandidateGenerationResult(
        IReadOnlyList<ComboCandidate> Candidates,
        IReadOnlyList<ComboCandidateReject> Rejects);

    public sealed class ComboCandidateGenerator
    {
        private readonly ComboEvaluator _evaluator;

        public ComboCandidateGenerator(ComboEvaluator? evaluator = null)
        {
            _evaluator = evaluator ?? new ComboEvaluator();
        }

        public ComboCandidateGenerationResult Generate(
            ScorelineDistribution distribution,
            IReadOnlyList<ContractQuote> quotedContracts,
            ComboCandidateGeneratorOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(distribution);
            ArgumentNullException.ThrowIfNull(quotedContracts);

            var effectiveOptions = options ?? new ComboCandidateGeneratorOptions();
            ValidateOptions(effectiveOptions);

            var rejects = new List<ComboCandidateReject>();
            var legChoices = quotedContracts
                .SelectMany(quoted => BuildLegChoices(quoted, effectiveOptions, rejects))
                .ToList();

            var candidates = new List<ComboCandidate>();
            for (var first = 0; first < legChoices.Count; first++)
            {
                for (var second = first + 1; second < legChoices.Count; second++)
                {
                    var firstChoice = legChoices[first];
                    var secondChoice = legChoices[second];
                    var candidateId = CandidateId(firstChoice.Leg, secondChoice.Leg);
                    if (SameToken(firstChoice.Leg, secondChoice.Leg))
                    {
                        rejects.Add(new ComboCandidateReject(
                            ComboCandidateRejectReason.SameTokenPair,
                            "A combo cannot use the same Polymarket token twice.",
                            firstChoice.Leg.Contract.Identity.TokenId,
                            firstChoice.Leg.Contract.Identity.OutcomeSide,
                            candidateId));
                        continue;
                    }

                    try
                    {
                        var evaluation = _evaluator.Evaluate(distribution, [firstChoice.Leg, secondChoice.Leg]);
                        candidates.Add(new ComboCandidate(
                            candidateId,
                            ToCandidateLeg(firstChoice.Leg, firstChoice.QuoteFetchedAtUtc),
                            ToCandidateLeg(secondChoice.Leg, secondChoice.QuoteFetchedAtUtc),
                            evaluation,
                            BadHoleStates(evaluation, effectiveOptions.MaxBadHoleStates)));
                    }
                    catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
                    {
                        rejects.Add(new ComboCandidateReject(
                            ComboCandidateRejectReason.EvaluationFailed,
                            ex.Message,
                            CandidateId: candidateId));
                    }
                }
            }

            return new ComboCandidateGenerationResult(
                candidates
                    .OrderByDescending(candidate => candidate.ExpectedPnl)
                    .ThenByDescending(candidate => candidate.Roi)
                    .ThenBy(candidate => candidate.ProbabilityOfLoss)
                    .Take(effectiveOptions.MaxCandidates)
                    .ToList(),
                rejects);
        }

        private static void ValidateOptions(ComboCandidateGeneratorOptions options)
        {
            if (options.SharesPerLeg <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "Shares per leg must be positive.");
            if (!options.IncludeBuyLegs && !options.IncludeSellLegs)
                throw new ArgumentException("At least one executable side must be enabled.", nameof(options));
            if (options.MaxCandidates <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxCandidates must be positive.");
            if (options.MaxBadHoleStates < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxBadHoleStates cannot be negative.");
            if (options.MaxQuoteAge.HasValue && options.MaxQuoteAge.Value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxQuoteAge cannot be negative.");
        }

        private static IEnumerable<LegChoice> BuildLegChoices(
            ContractQuote quoted,
            ComboCandidateGeneratorOptions options,
            List<ComboCandidateReject> rejects)
        {
            if (quoted.Contract is null)
            {
                rejects.Add(new ComboCandidateReject(ComboCandidateRejectReason.MissingContract, "Quoted contract is missing."));
                yield break;
            }
            if (quoted.Quote is null)
            {
                rejects.Add(new ComboCandidateReject(
                    ComboCandidateRejectReason.MissingQuote,
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
                    rejects.Add(new ComboCandidateReject(
                        ComboCandidateRejectReason.StaleQuote,
                        $"Quote is older than the configured max age of {maxAge}.",
                        quoted.Contract.Identity.TokenId,
                        quoted.Contract.Identity.OutcomeSide));
                    yield break;
                }
            }

            if (options.IncludeBuyLegs)
            {
                if (quoted.Quote.BestAsk is null)
                {
                    rejects.Add(new ComboCandidateReject(
                        ComboCandidateRejectReason.MissingExecutableAsk,
                        "Buy leg requires a best ask.",
                        quoted.Contract.Identity.TokenId,
                        quoted.Contract.Identity.OutcomeSide));
                }
                else if (quoted.Quote.AskSize.HasValue && quoted.Quote.AskSize.Value < options.SharesPerLeg)
                {
                    rejects.Add(new ComboCandidateReject(
                        ComboCandidateRejectReason.InsufficientAskSize,
                        "Buy leg shares exceed executable ask size.",
                        quoted.Contract.Identity.TokenId,
                        quoted.Contract.Identity.OutcomeSide));
                }
                else
                {
                    yield return new LegChoice(ComboLeg.Buy(quoted.Contract, options.SharesPerLeg, quoted.Quote), quoted.Quote.FetchedAtUtc);
                }
            }

            if (options.IncludeSellLegs)
            {
                if (quoted.Quote.BestBid is null)
                {
                    rejects.Add(new ComboCandidateReject(
                        ComboCandidateRejectReason.MissingExecutableBid,
                        "Sell leg requires a best bid.",
                        quoted.Contract.Identity.TokenId,
                        quoted.Contract.Identity.OutcomeSide));
                }
                else if (quoted.Quote.BidSize.HasValue && quoted.Quote.BidSize.Value < options.SharesPerLeg)
                {
                    rejects.Add(new ComboCandidateReject(
                        ComboCandidateRejectReason.InsufficientBidSize,
                        "Sell leg shares exceed executable bid size.",
                        quoted.Contract.Identity.TokenId,
                        quoted.Contract.Identity.OutcomeSide));
                }
                else
                {
                    yield return new LegChoice(ComboLeg.Sell(quoted.Contract, options.SharesPerLeg, quoted.Quote), quoted.Quote.FetchedAtUtc);
                }
            }
        }

        private static ComboCandidateLeg ToCandidateLeg(ComboLeg leg, DateTimeOffset quoteFetchedAtUtc) => new(
            leg.Contract,
            leg.Action,
            leg.Shares,
            leg.ExecutablePrice,
            leg.Shares * leg.ExecutablePrice,
            quoteFetchedAtUtc);

        private static IReadOnlyList<ComboBadHoleState> BadHoleStates(ComboEvaluation evaluation, int maxStates) =>
            evaluation.StatePnL
                .Where(state => state.NetPnl < 0)
                .OrderBy(state => state.NetPnl)
                .ThenByDescending(state => state.Probability)
                .Take(maxStates)
                .Select(state => new ComboBadHoleState(state.HomeGoals, state.AwayGoals, state.Probability, state.NetPnl, state.LegResults))
                .ToList();

        private static bool SameToken(ComboLeg first, ComboLeg second)
        {
            var firstToken = first.Contract.Identity.TokenId;
            var secondToken = second.Contract.Identity.TokenId;
            return !string.IsNullOrWhiteSpace(firstToken) &&
                   !string.IsNullOrWhiteSpace(secondToken) &&
                   string.Equals(firstToken, secondToken, StringComparison.Ordinal);
        }

        private static string CandidateId(ComboLeg first, ComboLeg second) =>
            $"{LegId(first)}|{LegId(second)}";

        private static string LegId(ComboLeg leg) =>
            $"{leg.Action}:{leg.Contract.Identity.ConditionId ?? leg.Contract.FixtureId}:{leg.Contract.Identity.TokenId ?? leg.Contract.Label}";

        private sealed record LegChoice(ComboLeg Leg, DateTimeOffset QuoteFetchedAtUtc);
    }
}
