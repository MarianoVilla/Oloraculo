using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.ComboLab.Candidates;
using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Mapping;
using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;
using Oloraculo.Web.Services;

namespace Oloraculo.Web.ComboLab.Monitor
{
    public sealed class LiveComplementaryLockService
    {
        private readonly OloraculoDbContext _db;
        private readonly PolymarketMarketDataService _markets;
        private readonly PredictionService _prediction;
        private readonly PolymarketFootballContractMapper _mapper = new();
        private readonly ComplementaryLockCandidateGenerator _generator = new();
        private readonly FootballStrategyCandidateGenerator _strategyGenerator = new();

        public LiveComplementaryLockService(
            OloraculoDbContext db,
            PolymarketMarketDataService markets,
            PredictionService prediction)
        {
            _db = db;
            _markets = markets;
            _prediction = prediction;
        }

        public async Task<LiveComplementaryLockSnapshot> RefreshAsync(
            string fixtureId,
            string eventSlug,
            decimal sharesPerLeg = 5m,
            decimal executionBuffer = .02m,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(fixtureId))
                throw new ArgumentException("Fixture id is required.", nameof(fixtureId));
            if (string.IsNullOrWhiteSpace(eventSlug))
                throw new ArgumentException("Polymarket event slug is required.", nameof(eventSlug));
            if (sharesPerLeg <= 0)
                throw new ArgumentOutOfRangeException(nameof(sharesPerLeg), "Shares per leg must be positive.");
            if (executionBuffer < 0)
                throw new ArgumentOutOfRangeException(nameof(executionBuffer), "Execution buffer cannot be negative.");

            var rejects = new List<LiveComplementaryLockReject>();
            var fixture = await _db.Fixtures.AsNoTracking().SingleOrDefaultAsync(item => item.Id == fixtureId, ct);
            if (fixture is null)
            {
                rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Fixture, $"Fixture {fixtureId} was not found."));
                return EmptySnapshot(fixtureId, eventSlug, rejects);
            }

            var teamNames = await _db.Teams.AsNoTracking().ToDictionaryAsync(team => team.Id, team => team.Name, ct);
            var fixtureContext = new FootballFixtureMappingContext(
                fixture.Id,
                fixture.HomeTeamId,
                fixture.AwayTeamId,
                Name(teamNames, fixture.HomeTeamId),
                Name(teamNames, fixture.AwayTeamId));

            PolymarketEventSnapshot polymarketEvent;
            try
            {
                polymarketEvent = await _markets.FetchEventBySlugAsync(eventSlug.Trim(), ct);
            }
            catch (Exception ex)
            {
                rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Event, $"Gamma event fetch failed: {ex.Message}"));
                return EmptySnapshot(fixtureId, eventSlug, rejects, FixtureLabel(fixtureContext));
            }

            var inputHashes = new List<LiveComplementaryInputHash>();
            AddInputHash(inputHashes, "gamma-event-detail", polymarketEvent.RawPayloadHash, polymarketEvent.FetchedAtUtc, polymarketEvent.Slug ?? eventSlug, polymarketEvent.EventId);
            var eventState = LiveComplementaryEventState.FromEvent(polymarketEvent);
            var eventIdentity = new PolymarketEventIdentity(polymarketEvent.Slug ?? eventSlug, polymarketEvent.EventId, polymarketEvent.Title);
            if (!eventState.AllowsCandidatePricing)
            {
                rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Event, eventState.Blocker, Outcome: eventState.TimeMode));
                return new LiveComplementaryLockSnapshot(
                    DateTimeOffset.UtcNow,
                    fixture.Id,
                    FixtureLabel(fixtureContext),
                    eventSlug.Trim(),
                    polymarketEvent.EventId,
                    polymarketEvent.Title,
                    eventState,
                    new LiveComplementaryGoalDecay(
                        eventState.TimeMode,
                        Applied: false,
                        Minute: eventState.ElapsedMinute,
                        RemainingFraction: null,
                        KickoffHomeExpectedGoals: null,
                        KickoffAwayExpectedGoals: null,
                        RemainingHomeExpectedGoals: null,
                        RemainingAwayExpectedGoals: null,
                        RetainedGridMass: null,
                        eventState.Blocker),
                    new LiveComplementaryScoreConditioning(
                        eventState.TimeMode,
                        Applied: false,
                        eventState.HomeGoals,
                        eventState.AwayGoals,
                        OriginalStateCount: 0,
                        RetainedStateCount: 0,
                        RemovedImpossibleStateCount: 0,
                        RetainedProbabilityMass: 0,
                        eventState.Blocker),
                    DistributionSource: "blocked: unsafe event-time state",
                    EventMarkets: polymarketEvent.Markets.Count,
                    MappedContracts: 0,
                    BooksFetched: 0,
                    InputHashes: DistinctInputHashes(inputHashes),
                    Candidates: [],
                    GeneralizedCandidates: [],
                    Rejects: rejects);
            }

            var contracts = new List<FootballContract>();
            foreach (var market in polymarketEvent.Markets)
            {
                var mapping = _mapper.Map(market, fixtureContext, eventIdentity);
                foreach (var reject in mapping.MarketRejects)
                    rejects.Add(ToReject(reject, market));
                foreach (var token in mapping.Tokens)
                {
                    if (token.Contract is not null)
                    {
                        contracts.Add(token.Contract);
                        continue;
                    }

                    foreach (var reject in token.Rejects)
                        rejects.Add(ToReject(reject, market, token.TokenId, token.Outcome));
                }
            }

            var distinctContracts = contracts
                .DistinctBy(ContractKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var tokenIds = distinctContracts
                .Select(contract => contract.Identity.TokenId)
                .Where(tokenId => !string.IsNullOrWhiteSpace(tokenId))
                .Select(tokenId => tokenId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var booksByToken = new Dictionary<string, PolymarketOrderBookSnapshot>(StringComparer.OrdinalIgnoreCase);
            if (tokenIds.Count > 0)
            {
                try
                {
                    var books = await _markets.FetchBooksAsync(tokenIds, ct);
                    booksByToken = books
                        .Where(book => !string.IsNullOrWhiteSpace(book.TokenId))
                        .GroupBy(book => book.TokenId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    foreach (var contract in distinctContracts)
                    {
                        var tokenId = contract.Identity.TokenId;
                        if (string.IsNullOrWhiteSpace(tokenId))
                            continue;

                        rejects.Add(new LiveComplementaryLockReject(
                            LiveComplementaryLockRejectSource.Book,
                            $"Batch book fetch failed: {ex.Message}",
                            contract.Identity.MarketId,
                            contract.Identity.ConditionId,
                            tokenId,
                            contract.Identity.OutcomeSide));
                    }
                }
            }

            var quotedContracts = new List<ContractQuote>();
            foreach (var contract in distinctContracts)
            {
                var tokenId = contract.Identity.TokenId;
                if (string.IsNullOrWhiteSpace(tokenId))
                {
                    rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Mapping, "Mapped contract has no token id.", contract.Identity.MarketId, contract.Identity.ConditionId, tokenId, contract.Identity.OutcomeSide));
                    continue;
                }

                if (!booksByToken.TryGetValue(tokenId, out var book))
                {
                    rejects.Add(new LiveComplementaryLockReject(
                        LiveComplementaryLockRejectSource.Book,
                        "Book missing from /books batch response.",
                        contract.Identity.MarketId,
                        contract.Identity.ConditionId,
                        tokenId,
                        contract.Identity.OutcomeSide));
                    continue;
                }

                AddInputHash(inputHashes, "clob-book", book.RawPayloadHash, book.FetchedAtUtc, polymarketEvent.Slug ?? eventSlug, polymarketEvent.EventId, book.ConditionId ?? contract.Identity.ConditionId, tokenId);
                foreach (var reason in book.RejectReasons)
                {
                    rejects.Add(new LiveComplementaryLockReject(
                        LiveComplementaryLockRejectSource.Book,
                        reason.ToString(),
                        contract.Identity.MarketId,
                        contract.Identity.ConditionId ?? book.ConditionId,
                        tokenId,
                        contract.Identity.OutcomeSide));
                }

                quotedContracts.Add(new ContractQuote(
                    contract,
                    new MarketQuote(book.BestBid, book.BestAsk, book.BidSize, book.AskSize, book.FetchedAtUtc)));
            }

            var (distribution, distributionSource) = await DistributionAsync(fixture, ct);
            var decay = ApplyGoalDecay(distribution, distributionSource, eventState, quotedContracts);
            if (decay.Distribution is null)
            {
                rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Event, decay.Decay.Detail, Outcome: decay.Decay.Mode));
                return new LiveComplementaryLockSnapshot(
                    DateTimeOffset.UtcNow,
                    fixture.Id,
                    FixtureLabel(fixtureContext),
                    eventSlug.Trim(),
                    polymarketEvent.EventId,
                    polymarketEvent.Title,
                    eventState,
                    decay.Decay,
                    new LiveComplementaryScoreConditioning(
                        decay.Decay.Mode,
                        Applied: false,
                        eventState.HomeGoals,
                        eventState.AwayGoals,
                        OriginalStateCount: (distribution.MaxGoals + 1) * (distribution.MaxGoals + 1),
                        RetainedStateCount: 0,
                        RemovedImpossibleStateCount: (distribution.MaxGoals + 1) * (distribution.MaxGoals + 1),
                        RetainedProbabilityMass: 0,
                        decay.Decay.Detail),
                    DistributionSource: $"blocked: {decay.Source}",
                    EventMarkets: polymarketEvent.Markets.Count,
                    MappedContracts: contracts.Count,
                    BooksFetched: quotedContracts.Count,
                    InputHashes: DistinctInputHashes(inputHashes),
                    Candidates: [],
                    GeneralizedCandidates: [],
                    Rejects: rejects);
            }

            distribution = decay.Distribution;
            distributionSource = decay.Source;
            var conditioning = ApplyScoreConditioning(distribution, distributionSource, eventState);
            if (conditioning.Distribution is null)
            {
                rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Event, conditioning.Conditioning.Detail, Outcome: conditioning.Conditioning.Mode));
                return new LiveComplementaryLockSnapshot(
                    DateTimeOffset.UtcNow,
                    fixture.Id,
                    FixtureLabel(fixtureContext),
                    eventSlug.Trim(),
                    polymarketEvent.EventId,
                    polymarketEvent.Title,
                    eventState,
                    decay.Decay,
                    conditioning.Conditioning,
                    DistributionSource: $"blocked: {conditioning.Source}",
                    EventMarkets: polymarketEvent.Markets.Count,
                    MappedContracts: contracts.Count,
                    BooksFetched: quotedContracts.Count,
                    InputHashes: DistinctInputHashes(inputHashes),
                    Candidates: [],
                    GeneralizedCandidates: [],
                    Rejects: rejects);
            }

            distribution = conditioning.Distribution;
            distributionSource = conditioning.Source;
            var result = _generator.Generate(distribution, quotedContracts, new ComplementaryLockCandidateGeneratorOptions
            {
                AsOfUtc = DateTimeOffset.UtcNow,
                SharesPerLeg = sharesPerLeg,
                ExecutionBuffer = executionBuffer,
                MaxCandidates = 25,
                MaxQuoteAge = TimeSpan.FromMinutes(2)
            });
            var strategyResult = _strategyGenerator.Generate(distribution, quotedContracts, new FootballStrategyCandidateGeneratorOptions
            {
                AsOfUtc = DateTimeOffset.UtcNow,
                SharesPerLeg = sharesPerLeg,
                ExecutionBuffer = executionBuffer,
                MaxCandidates = 40,
                MaxQuoteAge = TimeSpan.FromMinutes(2)
            });

            rejects.AddRange(result.Rejects.Select(ToReject));
            rejects.AddRange(strategyResult.Rejects.Select(ToReject));

            return new LiveComplementaryLockSnapshot(
                DateTimeOffset.UtcNow,
                fixture.Id,
                FixtureLabel(fixtureContext),
                eventSlug.Trim(),
                polymarketEvent.EventId,
                polymarketEvent.Title,
                eventState,
                decay.Decay,
                conditioning.Conditioning,
                distributionSource,
                polymarketEvent.Markets.Count,
                contracts.Count,
                quotedContracts.Count,
                DistinctInputHashes(inputHashes),
                result.Candidates.Select(candidate => LiveComplementaryLockCandidateRow.FromCandidate(candidate, eventState, decay.Decay, conditioning.Conditioning)).ToList(),
                strategyResult.Candidates.Select(candidate => LiveFootballStrategyCandidateRow.FromCandidate(candidate, eventState, decay.Decay, conditioning.Conditioning)).ToList(),
                rejects);
        }

        private async Task<(ScorelineDistribution Distribution, string Source)> DistributionAsync(Fixture fixture, CancellationToken ct)
        {
            var prediction = await _prediction.PredictAsync(fixture, ct);
            if (prediction.BestPrediction.Scoreline is { } scoreline)
                return (scoreline.Normalize(), $"{prediction.BestPrediction.PredictorName} scoreline");

            return (UniformScoreline(maxGoals: 5), "uniform fallback: no scoreline prediction available");
        }

        private sealed record LiveTotalMarketAnchor(
            decimal Line,
            string? OverTokenId,
            string? UnderTokenId,
            decimal OverMid,
            decimal UnderMid,
            double NormalizedOverProbability,
            double RemainingExpectedGoals);

        private static (ScorelineDistribution? Distribution, string Source, LiveComplementaryGoalDecay Decay) ApplyGoalDecay(
            ScorelineDistribution distribution,
            string source,
            LiveComplementaryEventState eventState,
            IReadOnlyList<ContractQuote> quotedContracts)
        {
            if (!eventState.IsInPlay)
            {
                return (distribution, source, new LiveComplementaryGoalDecay(
                    "PRE_GAME_NO_DECAY",
                    Applied: false,
                    Minute: eventState.ElapsedMinute,
                    RemainingFraction: 1.0,
                    KickoffHomeExpectedGoals: null,
                    KickoffAwayExpectedGoals: null,
                    RemainingHomeExpectedGoals: null,
                    RemainingAwayExpectedGoals: null,
                    RetainedGridMass: 1.0,
                    "pre-game distribution retained; no elapsed-minute decay applied"));
            }

            if (!eventState.HomeGoals.HasValue || !eventState.AwayGoals.HasValue || !eventState.ElapsedMinute.HasValue)
            {
                return (null, source, new LiveComplementaryGoalDecay(
                    "BLOCKED_DECAY_STATE_MISSING",
                    Applied: false,
                    Minute: eventState.ElapsedMinute,
                    RemainingFraction: null,
                    KickoffHomeExpectedGoals: null,
                    KickoffAwayExpectedGoals: null,
                    RemainingHomeExpectedGoals: null,
                    RemainingAwayExpectedGoals: null,
                    RetainedGridMass: null,
                    "goal decay requires parseable score and elapsed minute"));
            }

            var modelDecay = PoissonGoalDecayModel.ScorelineDistributionFromCurrentState(
                distribution,
                eventState.HomeGoals.Value,
                eventState.AwayGoals.Value,
                eventState.ElapsedMinute.Value);

            var anchor = TryFindLiveTotalAnchor(quotedContracts, eventState.HomeGoals.Value + eventState.AwayGoals.Value, modelDecay.RemainingHomeExpectedGoals + modelDecay.RemainingAwayExpectedGoals);
            var result = anchor is null
                ? modelDecay
                : PoissonGoalDecayModel.ScorelineDistributionFromCurrentStateWithRemainingTotal(
                    distribution,
                    eventState.HomeGoals.Value,
                    eventState.AwayGoals.Value,
                    eventState.ElapsedMinute.Value,
                    anchor.RemainingExpectedGoals);

            var detail = anchor is null
                ? $"minute-decayed remaining goals at {result.Minute:0.#}' from score {result.CurrentHomeGoals}-{result.CurrentAwayGoals}; remaining xG {result.RemainingHomeExpectedGoals:0.00}-{result.RemainingAwayExpectedGoals:0.00}; retained grid mass {result.RetainedGridMass:P1}; no paired live O/U anchor found"
                : $"market-implied live total anchor line {anchor.Line:0.0} from paired O/U mids {anchor.OverMid:0.000}/{anchor.UnderMid:0.000}; normalized over {anchor.NormalizedOverProbability:P1}; implied remaining total {anchor.RemainingExpectedGoals:0.00}; split by model xG to {result.RemainingHomeExpectedGoals:0.00}-{result.RemainingAwayExpectedGoals:0.00}; retained grid mass {result.RetainedGridMass:P1}";

            return (result.Distribution, anchor is null
                ? $"{source}; minute-decayed score {result.CurrentHomeGoals}-{result.CurrentAwayGoals} at {result.Minute:0.#}'"
                : $"{source}; market-anchored live total {anchor.Line:0.0}; minute-decayed score {result.CurrentHomeGoals}-{result.CurrentAwayGoals} at {result.Minute:0.#}'",
                new LiveComplementaryGoalDecay(
                anchor is null ? "CURRENT_MINUTE_GOAL_DECAY" : "CURRENT_MINUTE_GOAL_DECAY_MARKET_ANCHORED",
                Applied: true,
                result.Minute,
                result.RemainingFraction,
                result.KickoffHomeExpectedGoals,
                result.KickoffAwayExpectedGoals,
                result.RemainingHomeExpectedGoals,
                result.RemainingAwayExpectedGoals,
                result.RetainedGridMass,
                detail));
        }

        private static LiveTotalMarketAnchor? TryFindLiveTotalAnchor(IReadOnlyList<ContractQuote> quotedContracts, int currentTotalGoals, double modelRemainingTotalGoals)
        {
            var anchors = new List<LiveTotalMarketAnchor>();
            foreach (var group in quotedContracts
                .Where(quoted => quoted.Contract.MarketType == FootballMarketType.MatchTotal && quoted.Contract.Line.HasValue)
                .GroupBy(quoted => quoted.Contract.Line!.Value))
            {
                var line = group.Key;
                if ((double)line <= currentTotalGoals)
                    continue;

                var over = group.FirstOrDefault(quoted => quoted.Contract.Selection == ContractSelection.Over);
                var under = group.FirstOrDefault(quoted => quoted.Contract.Selection == ContractSelection.Under);
                var overMid = over is null ? null : QuoteMid(over.Quote);
                var underMid = under is null ? null : QuoteMid(under.Quote);
                if (!overMid.HasValue || !underMid.HasValue)
                    continue;

                var normalizedOver = PoissonGoalDecayModel.NormalizeBinaryPrice(overMid.Value, underMid.Value);
                if (normalizedOver is <= .02 or >= .98)
                    continue;

                var remaining = PoissonGoalDecayModel.ImpliedRemainingLambdaForOver((double)line, currentTotalGoals, normalizedOver);
                if (!double.IsFinite(remaining) || remaining < 0)
                    continue;

                anchors.Add(new LiveTotalMarketAnchor(
                    line,
                    over!.Contract.Identity.TokenId,
                    under!.Contract.Identity.TokenId,
                    overMid.Value,
                    underMid.Value,
                    normalizedOver,
                    remaining));
            }

            var liveExpectedTotal = currentTotalGoals + modelRemainingTotalGoals;
            return anchors
                .OrderBy(anchor => Math.Abs((double)anchor.Line - liveExpectedTotal))
                .ThenBy(anchor => anchor.Line)
                .FirstOrDefault();
        }

        private static decimal? QuoteMid(MarketQuote quote)
        {
            if (!quote.BestBid.HasValue || !quote.BestAsk.HasValue)
                return null;
            if (quote.BestBid.Value < 0 || quote.BestAsk.Value > 1 || quote.BestBid.Value > quote.BestAsk.Value)
                return null;
            return (quote.BestBid.Value + quote.BestAsk.Value) / 2m;
        }

        private static (ScorelineDistribution? Distribution, string Source, LiveComplementaryScoreConditioning Conditioning) ApplyScoreConditioning(
            ScorelineDistribution distribution,
            string source,
            LiveComplementaryEventState eventState)
        {
            var originalStateCount = (distribution.MaxGoals + 1) * (distribution.MaxGoals + 1);
            if (!eventState.IsInPlay)
            {
                return (distribution, source, new LiveComplementaryScoreConditioning(
                    "PRE_GAME_UNCONDITIONED",
                    Applied: false,
                    CurrentHomeGoals: eventState.HomeGoals,
                    CurrentAwayGoals: eventState.AwayGoals,
                    OriginalStateCount: originalStateCount,
                    RetainedStateCount: originalStateCount,
                    RemovedImpossibleStateCount: 0,
                    RetainedProbabilityMass: 1.0,
                    "full pre-game score grid retained"));
            }

            if (!eventState.HomeGoals.HasValue || !eventState.AwayGoals.HasValue)
            {
                return (null, source, new LiveComplementaryScoreConditioning(
                    "BLOCKED_SCORE_UNAVAILABLE",
                    Applied: false,
                    CurrentHomeGoals: eventState.HomeGoals,
                    CurrentAwayGoals: eventState.AwayGoals,
                    OriginalStateCount: originalStateCount,
                    RetainedStateCount: 0,
                    RemovedImpossibleStateCount: originalStateCount,
                    RetainedProbabilityMass: 0,
                    "in-play score conditioning requires home and away goals"));
            }

            var currentHome = eventState.HomeGoals.Value;
            var currentAway = eventState.AwayGoals.Value;
            var retainedMass = 0.0;
            var retainedCount = 0;
            for (var home = 0; home <= distribution.MaxGoals; home++)
            {
                for (var away = 0; away <= distribution.MaxGoals; away++)
                {
                    if (home < currentHome || away < currentAway)
                        continue;
                    retainedCount++;
                    retainedMass += distribution.Probability(home, away);
                }
            }

            if (retainedMass <= 0 || !double.IsFinite(retainedMass) || retainedCount == 0)
            {
                return (null, source, new LiveComplementaryScoreConditioning(
                    "BLOCKED_NO_RETAINED_SCORE_STATES",
                    Applied: false,
                    currentHome,
                    currentAway,
                    originalStateCount,
                    RetainedStateCount: 0,
                    RemovedImpossibleStateCount: originalStateCount,
                    RetainedProbabilityMass: 0,
                    $"current score {currentHome}-{currentAway} retained no modeled terminal states"));
            }

            var conditioned = distribution.ConditionOnMinimumScore(currentHome, currentAway);
            var removedCount = originalStateCount - retainedCount;
            var detail = $"conditioned terminal states to scores >= {currentHome}-{currentAway}; retained {retainedCount}/{originalStateCount} states and {retainedMass:P1} prior mass";
            return (conditioned, $"{source}; current score conditioned {currentHome}-{currentAway}", new LiveComplementaryScoreConditioning(
                "CURRENT_SCORE_CONDITIONED",
                Applied: true,
                currentHome,
                currentAway,
                originalStateCount,
                retainedCount,
                removedCount,
                retainedMass,
                detail));
        }

        private static ScorelineDistribution UniformScoreline(int maxGoals)
        {
            var matrix = new double[maxGoals + 1, maxGoals + 1];
            var probability = 1.0 / ((maxGoals + 1) * (maxGoals + 1));
            for (var home = 0; home <= maxGoals; home++)
                for (var away = 0; away <= maxGoals; away++)
                    matrix[home, away] = probability;
            return new ScorelineDistribution { MaxGoals = maxGoals, Matrix = matrix };
        }

        private static LiveComplementaryLockReject ToReject(FootballContractMapReject reject, PolymarketMarketSnapshot market, string? tokenId = null, string? outcome = null) => new(
            LiveComplementaryLockRejectSource.Mapping,
            $"{reject.Reason}: {reject.Detail}",
            market.MarketId,
            market.ConditionId,
            tokenId ?? reject.TokenId,
            outcome ?? reject.Outcome);

        private static LiveComplementaryLockReject ToReject(ComplementaryLockCandidateReject reject) => new(
            LiveComplementaryLockRejectSource.Candidate,
            $"{reject.Reason}: {reject.Detail}",
            TokenId: reject.TokenId,
            Outcome: reject.OutcomeSide);

        private static LiveComplementaryLockReject ToReject(FootballStrategyCandidateReject reject) => new(
            LiveComplementaryLockRejectSource.Candidate,
            $"{reject.Reason}: {reject.Detail}",
            TokenId: reject.TokenId,
            Outcome: reject.OutcomeSide);

        private static LiveComplementaryLockSnapshot EmptySnapshot(
            string fixtureId,
            string eventSlug,
            IReadOnlyList<LiveComplementaryLockReject> rejects,
            string fixtureLabel = "fixture unavailable") => new(
            DateTimeOffset.UtcNow,
            fixtureId,
            fixtureLabel,
            eventSlug,
            EventId: null,
            EventTitle: null,
            EventState: new LiveComplementaryEventState(
                "UNAVAILABLE",
                "BLOCKED_EVENT_UNAVAILABLE",
                AllowsCandidatePricing: false,
                IsInPlay: false,
                RawScore: null,
                HomeGoals: null,
                AwayGoals: null,
                RawElapsed: null,
                ElapsedMinute: null,
                StartTimeUtc: null,
                "event unavailable"),
            GoalDecay: new LiveComplementaryGoalDecay(
                "BLOCKED_EVENT_UNAVAILABLE",
                Applied: false,
                Minute: null,
                RemainingFraction: null,
                KickoffHomeExpectedGoals: null,
                KickoffAwayExpectedGoals: null,
                RemainingHomeExpectedGoals: null,
                RemainingAwayExpectedGoals: null,
                RetainedGridMass: null,
                "event unavailable"),
            ScoreConditioning: new LiveComplementaryScoreConditioning(
                "BLOCKED_EVENT_UNAVAILABLE",
                Applied: false,
                CurrentHomeGoals: null,
                CurrentAwayGoals: null,
                OriginalStateCount: 0,
                RetainedStateCount: 0,
                RemovedImpossibleStateCount: 0,
                RetainedProbabilityMass: 0,
                "event unavailable"),
            DistributionSource: "none",
            EventMarkets: 0,
            MappedContracts: 0,
            BooksFetched: 0,
            InputHashes: [],
            Candidates: [],
            GeneralizedCandidates: [],
            Rejects: rejects);

        private static string FixtureLabel(FootballFixtureMappingContext fixture) => $"{fixture.HomeTeamName} vs {fixture.AwayTeamName}";

        private static string Name(IReadOnlyDictionary<string, string> names, string teamId) =>
            names.TryGetValue(teamId, out var name) ? name : teamId;

        private static string ContractKey(FootballContract contract) =>
            !string.IsNullOrWhiteSpace(contract.Identity.TokenId)
                ? contract.Identity.TokenId!
                : $"{contract.FixtureId}:{contract.MarketType}:{contract.Selection}:{contract.Team}:{contract.Line}:{contract.ExactHomeGoals}:{contract.ExactAwayGoals}";

        private static void AddInputHash(
            List<LiveComplementaryInputHash> inputHashes,
            string source,
            string? payloadHash,
            DateTimeOffset fetchedAtUtc,
            string? eventSlug = null,
            string? eventId = null,
            string? conditionId = null,
            string? tokenId = null)
        {
            if (string.IsNullOrWhiteSpace(payloadHash))
                return;

            inputHashes.Add(new LiveComplementaryInputHash(
                source,
                payloadHash,
                fetchedAtUtc,
                eventSlug,
                eventId,
                conditionId,
                tokenId));
        }

        private static IReadOnlyList<LiveComplementaryInputHash> EventListInputHashes(IReadOnlyList<PolymarketEventSnapshot> events) =>
            events
                .Where(item => !string.IsNullOrWhiteSpace(item.RawPayloadHash))
                .DistinctBy(item => item.RawPayloadHash, StringComparer.OrdinalIgnoreCase)
                .Select(item => new LiveComplementaryInputHash(
                    "gamma-events-list",
                    item.RawPayloadHash!,
                    item.FetchedAtUtc))
                .ToList();

        private static IReadOnlyList<LiveComplementaryInputHash> DistinctInputHashes(IEnumerable<LiveComplementaryInputHash> inputHashes) =>
            inputHashes
                .Where(input => !string.IsNullOrWhiteSpace(input.PayloadHash))
                .DistinctBy(InputHashKey, StringComparer.OrdinalIgnoreCase)
                .OrderBy(input => input.Source, StringComparer.Ordinal)
                .ThenBy(input => input.EventSlug, StringComparer.Ordinal)
                .ThenBy(input => input.TokenId, StringComparer.Ordinal)
                .ThenBy(input => input.PayloadHash, StringComparer.Ordinal)
                .ToList();

        private static string InputHashKey(LiveComplementaryInputHash input) =>
            $"{input.Source}|{input.PayloadHash}|{input.EventSlug}|{input.EventId}|{input.ConditionId}|{input.TokenId}";

        public async Task<LiveComplementaryLockEventSuggestion> SuggestEventAsync(string fixtureId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(fixtureId))
                throw new ArgumentException("Fixture id is required.", nameof(fixtureId));

            var rejects = new List<LiveComplementaryLockReject>();
            var fixtureContext = await FixtureContextAsync(fixtureId, rejects, ct);
            if (fixtureContext is null)
            {
                return new LiveComplementaryLockEventSuggestion(
                    DateTimeOffset.UtcNow,
                    fixtureId,
                    "fixture unavailable",
                    EventSlug: null,
                    EventId: null,
                    EventTitle: null,
                    Matches: [],
                    Rejects: rejects);
            }

            IReadOnlyList<PolymarketEventSnapshot> events;
            try
            {
                events = await _markets.FetchWorldCupEventsAsync(ct: ct);
            }
            catch (Exception ex)
            {
                rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Event, $"World Cup event discovery failed: {ex.Message}"));
                return new LiveComplementaryLockEventSuggestion(
                    DateTimeOffset.UtcNow,
                    fixtureId,
                    FixtureLabel(fixtureContext),
                    EventSlug: null,
                    EventId: null,
                    EventTitle: null,
                    Matches: [],
                    Rejects: rejects);
            }

            return BuildEventSuggestion(fixtureId, fixtureContext, events, rejects);
        }

        public async Task<LiveComplementaryBatchScanSnapshot> BatchScanAsync(
            LiveComplementaryBatchScanOptions? options = null,
            CancellationToken ct = default)
        {
            var effectiveOptions = options ?? new LiveComplementaryBatchScanOptions();
            ValidateBatchOptions(effectiveOptions);
            var asOfUtc = DateTimeOffset.UtcNow;
            var rejects = new List<LiveComplementaryLockReject>();

            IReadOnlyList<PolymarketEventSnapshot> events;
            try
            {
                events = await _markets.FetchWorldCupEventsAsync(ct: ct);
            }
            catch (Exception ex)
            {
                rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Event, $"World Cup event discovery failed: {ex.Message}"));
                return new LiveComplementaryBatchScanSnapshot(
                    asOfUtc,
                    "WATCH_ONLY_PUBLIC_READS",
                    effectiveOptions.MaxFixtures,
                    FixturesSeen: 0,
                    FixturesScanned: 0,
                    EventMatches: 0,
                    EventlessFixtures: 0,
                    AmbiguousEvents: 0,
                    TotalEventMarkets: 0,
                    TotalMappedContracts: 0,
                    TotalBooksFetched: 0,
                    InputHashes: [],
                    Fixtures: [],
                    Candidates: [],
                    Rejects: rejects);
            }

            var inputHashes = EventListInputHashes(events).ToList();

            var fixtures = (await _db.Fixtures.AsNoTracking().ToListAsync(ct))
                .OrderBy(fixture => fixture.KickoffUtc ?? DateTimeOffset.MaxValue)
                .ThenBy(fixture => fixture.Group)
                .ThenBy(fixture => fixture.HomeTeamId)
                .ThenBy(fixture => fixture.AwayTeamId)
                .Take(effectiveOptions.MaxFixtures)
                .ToList();
            var teamNames = await _db.Teams.AsNoTracking().ToDictionaryAsync(team => team.Id, team => team.Name, ct);

            var fixtureRows = new List<LiveComplementaryBatchFixtureRow>();
            var candidates = new List<LiveFootballStrategyBatchCandidateRow>();
            var eventMatches = 0;
            var eventless = 0;
            var ambiguous = 0;
            var scanned = 0;
            var totalEventMarkets = 0;
            var totalMappedContracts = 0;
            var totalBooksFetched = 0;

            foreach (var fixture in fixtures)
            {
                var fixtureContext = new FootballFixtureMappingContext(
                    fixture.Id,
                    fixture.HomeTeamId,
                    fixture.AwayTeamId,
                    Name(teamNames, fixture.HomeTeamId),
                    Name(teamNames, fixture.AwayTeamId));
                var suggestion = BuildEventSuggestion(fixture.Id, fixtureContext, events, []);

                if (!suggestion.HasSuggestion)
                {
                    var eventReject = suggestion.Rejects.FirstOrDefault(reject => reject.Source == LiveComplementaryLockRejectSource.Event);
                    foreach (var reject in suggestion.Rejects)
                        rejects.Add(reject);

                    var status = eventReject?.Detail.Contains("Ambiguous", StringComparison.OrdinalIgnoreCase) == true
                        ? "AMBIGUOUS_EVENT"
                        : "NO_EVENT_MATCH";
                    if (status == "AMBIGUOUS_EVENT")
                        ambiguous++;
                    else
                        eventless++;

                    fixtureRows.Add(new LiveComplementaryBatchFixtureRow(
                        fixture.Id,
                        FixtureLabel(fixtureContext),
                        status,
                        EventSlug: null,
                        EventId: null,
                        EventTitle: null,
                        EventMatchScore: suggestion.Matches.FirstOrDefault()?.Score ?? 0,
                        EventMarkets: 0,
                        MappedContracts: 0,
                        BooksFetched: 0,
                        TwoLegCandidates: 0,
                        GeneralizedCandidates: 0,
                        PositiveLocks: 0,
                        GeneralizedPositiveLocks: 0,
                        Rejects: suggestion.Rejects.Count,
                        Blocker: eventReject?.Detail ?? $"No Polymarket World Cup event matched {FixtureLabel(fixtureContext)}."));
                    continue;
                }

                eventMatches++;
                try
                {
                    var snapshot = await RefreshAsync(
                        fixture.Id,
                        suggestion.EventSlug!,
                        effectiveOptions.SharesPerLeg,
                        effectiveOptions.ExecutionBuffer,
                        ct);
                    scanned++;
                    totalEventMarkets += snapshot.EventMarkets;
                    totalMappedContracts += snapshot.MappedContracts;
                    totalBooksFetched += snapshot.BooksFetched;
                    foreach (var reject in snapshot.Rejects)
                        rejects.Add(reject);
                    inputHashes.AddRange(snapshot.InputHashes);

                    foreach (var candidate in snapshot.GeneralizedCandidates.Take(effectiveOptions.MaxCandidatesPerFixture))
                        candidates.Add(LiveFootballStrategyBatchCandidateRow.FromCandidate(snapshot, candidate));

                    var blocker = snapshot.GeneralizedCandidates.Count > 0
                        ? "WATCH_ONLY: cross-fixture ranking only; no fill/fee/markout proof"
                        : snapshot.Rejects.FirstOrDefault()?.Detail ?? "No generalized candidates survived mapping and book gates.";
                    fixtureRows.Add(new LiveComplementaryBatchFixtureRow(
                        fixture.Id,
                        snapshot.FixtureLabel,
                        snapshot.GeneralizedCandidates.Count > 0 ? "CANDIDATES" : "SCANNED_NO_CANDIDATES",
                        snapshot.EventSlug,
                        snapshot.EventId,
                        snapshot.EventTitle,
                        suggestion.Matches.FirstOrDefault()?.Score ?? 0,
                        snapshot.EventMarkets,
                        snapshot.MappedContracts,
                        snapshot.BooksFetched,
                        snapshot.Candidates.Count,
                        snapshot.GeneralizedCandidates.Count,
                        snapshot.PositiveLocks,
                        snapshot.GeneralizedPositiveLocks,
                        snapshot.Rejects.Count,
                        blocker));
                }
                catch (Exception ex)
                {
                    rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Event, $"{FixtureLabel(fixtureContext)} fetch failed: {ex.Message}", Outcome: suggestion.EventSlug));
                    fixtureRows.Add(new LiveComplementaryBatchFixtureRow(
                        fixture.Id,
                        FixtureLabel(fixtureContext),
                        "FETCH_FAILED",
                        suggestion.EventSlug,
                        suggestion.EventId,
                        suggestion.EventTitle,
                        suggestion.Matches.FirstOrDefault()?.Score ?? 0,
                        EventMarkets: 0,
                        MappedContracts: 0,
                        BooksFetched: 0,
                        TwoLegCandidates: 0,
                        GeneralizedCandidates: 0,
                        PositiveLocks: 0,
                        GeneralizedPositiveLocks: 0,
                        Rejects: 1,
                        Blocker: ex.Message));
                }
            }

            var rankedCandidates = candidates
                .OrderBy(candidate => StrategyVerdictRank(candidate.Verdict))
                .ThenByDescending(candidate => candidate.LockedProfit)
                .ThenBy(candidate => candidate.GapProbability)
                .ThenByDescending(candidate => candidate.ExpectedProfit)
                .ThenBy(candidate => Math.Abs(candidate.GoalJumpExposure))
                .ThenBy(candidate => candidate.FixtureLabel, StringComparer.OrdinalIgnoreCase)
                .Take(effectiveOptions.MaxTotalCandidates)
                .ToList();

            return new LiveComplementaryBatchScanSnapshot(
                asOfUtc,
                "WATCH_ONLY_PUBLIC_READS",
                effectiveOptions.MaxFixtures,
                fixtures.Count,
                scanned,
                eventMatches,
                eventless,
                ambiguous,
                totalEventMarkets,
                totalMappedContracts,
                totalBooksFetched,
                DistinctInputHashes(inputHashes),
                fixtureRows,
                rankedCandidates,
                rejects);
        }

        private static void ValidateBatchOptions(LiveComplementaryBatchScanOptions options)
        {
            if (options.MaxFixtures <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxFixtures must be positive.");
            if (options.MaxCandidatesPerFixture <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxCandidatesPerFixture must be positive.");
            if (options.MaxTotalCandidates <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "MaxTotalCandidates must be positive.");
            if (options.SharesPerLeg <= 0)
                throw new ArgumentOutOfRangeException(nameof(options), "SharesPerLeg must be positive.");
            if (options.ExecutionBuffer < 0)
                throw new ArgumentOutOfRangeException(nameof(options), "ExecutionBuffer cannot be negative.");
        }

        private static LiveComplementaryLockEventSuggestion BuildEventSuggestion(
            string fixtureId,
            FootballFixtureMappingContext fixtureContext,
            IReadOnlyList<PolymarketEventSnapshot> events,
            List<LiveComplementaryLockReject> rejects)
        {
            var matches = events
                .Select(item => ScoreEvent(item, fixtureContext))
                .Where(match => match.Score > 0)
                .OrderByDescending(match => match.Score)
                .ThenBy(match => match.EventSlug, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var top = matches.FirstOrDefault();
            var runnerUp = matches.Skip(1).FirstOrDefault();
            if (top is null)
            {
                rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Event, $"No Polymarket World Cup event matched {FixtureLabel(fixtureContext)}."));
            }
            else if (runnerUp is not null && runnerUp.Score == top.Score)
            {
                rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Event, $"Ambiguous event match for {FixtureLabel(fixtureContext)}; top score tied."));
                top = null;
            }

            return new LiveComplementaryLockEventSuggestion(
                DateTimeOffset.UtcNow,
                fixtureId,
                FixtureLabel(fixtureContext),
                top?.EventSlug,
                top?.EventId,
                top?.EventTitle,
                matches.Take(8).ToList(),
                rejects.ToList());
        }

        private static int StrategyVerdictRank(string verdict) => verdict switch
        {
            nameof(FootballStrategyVerdict.TruePositiveLock) => 0,
            nameof(FootballStrategyVerdict.BreakEvenLock) => 1,
            nameof(FootballStrategyVerdict.MiddleHedge) => 2,
            nameof(FootballStrategyVerdict.GapHedge) => 3,
            nameof(FootballStrategyVerdict.CorrelatedOnly) => 4,
            _ => 9
        };

        private async Task<FootballFixtureMappingContext?> FixtureContextAsync(string fixtureId, List<LiveComplementaryLockReject> rejects, CancellationToken ct)
        {
            var fixture = await _db.Fixtures.AsNoTracking().SingleOrDefaultAsync(item => item.Id == fixtureId, ct);
            if (fixture is null)
            {
                rejects.Add(new LiveComplementaryLockReject(LiveComplementaryLockRejectSource.Fixture, $"Fixture {fixtureId} was not found."));
                return null;
            }

            var teamNames = await _db.Teams.AsNoTracking().ToDictionaryAsync(team => team.Id, team => team.Name, ct);
            return new FootballFixtureMappingContext(
                fixture.Id,
                fixture.HomeTeamId,
                fixture.AwayTeamId,
                Name(teamNames, fixture.HomeTeamId),
                Name(teamNames, fixture.AwayTeamId));
        }

        private static LiveComplementaryLockEventMatch ScoreEvent(PolymarketEventSnapshot polymarketEvent, FootballFixtureMappingContext fixture)
        {
            var slug = polymarketEvent.Slug ?? string.Empty;
            var title = polymarketEvent.Title ?? string.Empty;
            var normalizedSlug = TeamNameNormalizer.ToId(slug);
            var normalizedTitle = TeamNameNormalizer.ToId(title);
            var haystack = $"{normalizedSlug} {normalizedTitle}";
            var homeTokens = TeamTokens(fixture.HomeTeamId, fixture.HomeTeamName);
            var awayTokens = TeamTokens(fixture.AwayTeamId, fixture.AwayTeamName);
            var homeHits = homeTokens.Where(token => ContainsToken(haystack, token)).ToList();
            var awayHits = awayTokens.Where(token => ContainsToken(haystack, token)).ToList();

            if (homeHits.Count == 0 || awayHits.Count == 0)
            {
                return new LiveComplementaryLockEventMatch(
                    slug,
                    polymarketEvent.EventId,
                    polymarketEvent.Title,
                    Score: 0,
                    Evidence: "missing one side");
            }

            var score = 10;
            if (ContainsToken(normalizedTitle, TeamNameNormalizer.ToId(fixture.HomeTeamName)))
                score += 5;
            if (ContainsToken(normalizedTitle, TeamNameNormalizer.ToId(fixture.AwayTeamName)))
                score += 5;
            if (ContainsToken(normalizedSlug, TeamNameNormalizer.ToId(fixture.HomeTeamName)) || ContainsToken(normalizedSlug, fixture.HomeTeamId))
                score += 2;
            if (ContainsToken(normalizedSlug, TeamNameNormalizer.ToId(fixture.AwayTeamName)) || ContainsToken(normalizedSlug, fixture.AwayTeamId))
                score += 2;

            return new LiveComplementaryLockEventMatch(
                slug,
                polymarketEvent.EventId,
                polymarketEvent.Title,
                score,
                $"home={string.Join('/', homeHits)} away={string.Join('/', awayHits)}");
        }

        private static IReadOnlyList<string> TeamTokens(string id, string name)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TeamNameNormalizer.ToId(id),
                TeamNameNormalizer.ToId(name)
            };
            foreach (var part in TeamNameNormalizer.ToId(name).Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Length >= 4)
                    tokens.Add(part);
            }
            var compact = TeamNameNormalizer.ToId(name).Replace("-", string.Empty, StringComparison.Ordinal);
            if (compact.Length >= 3)
                tokens.Add(compact[..3]);
            return tokens.Where(token => token.Length >= 3).ToList();
        }

        private static bool ContainsToken(string haystack, string token) =>
            !string.IsNullOrWhiteSpace(token) &&
            ($"-{haystack}-").Contains($"-{token}-", StringComparison.OrdinalIgnoreCase);
    }
}
