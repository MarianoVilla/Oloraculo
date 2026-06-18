using Oloraculo.Web.ComboLab.Candidates;
using Oloraculo.Web.ComboLab.Mapping;
using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.ComboLab.Pricing;
using Oloraculo.Web.Helpers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oloraculo.Web.ComboLab.Monitor
{
    public enum LiveComplementaryLockRejectSource
    {
        Fixture,
        Event,
        Mapping,
        Book,
        Candidate
    }

    public sealed record LiveComplementaryLockReject(
        LiveComplementaryLockRejectSource Source,
        string Detail,
        string? MarketId = null,
        string? ConditionId = null,
        string? TokenId = null,
        string? Outcome = null);

    public sealed record LiveComplementaryRejectSourceDetailCount(
        LiveComplementaryLockRejectSource Source,
        string Detail,
        int Count);

    public sealed record LiveComplementaryCandidateBlockerCount(
        string Blocker,
        int Count);

    public sealed record LiveComplementaryFixtureStatusCount(
        string Status,
        int Count);

    public sealed record LiveComplementaryInputHash(
        string Source,
        string PayloadHash,
        DateTimeOffset FetchedAtUtc,
        string? EventSlug = null,
        string? EventId = null,
        string? ConditionId = null,
        string? TokenId = null);

    public sealed record LiveComplementaryInputFreshness(
        string Status,
        int InputCount,
        int GammaInputCount,
        int ClobBookInputCount,
        DateTimeOffset? OldestFetchedAtUtc,
        DateTimeOffset? NewestFetchedAtUtc,
        double MaxAgeSeconds,
        double SpanSeconds,
        string Detail);

    public sealed record LiveComplementaryEventState(
        string Phase,
        string TimeMode,
        bool AllowsCandidatePricing,
        bool IsInPlay,
        string? RawScore,
        int? HomeGoals,
        int? AwayGoals,
        string? RawElapsed,
        int? ElapsedMinute,
        DateTimeOffset? StartTimeUtc,
        string Blocker)
    {
        public static LiveComplementaryEventState FromEvent(PolymarketEventSnapshot polymarketEvent)
        {
            ArgumentNullException.ThrowIfNull(polymarketEvent);
            var score = ParseScore(polymarketEvent.Score);
            var elapsed = ParseElapsedMinute(polymarketEvent.Elapsed);
            var hasScoreOrElapsed = !string.IsNullOrWhiteSpace(polymarketEvent.Score) || !string.IsNullOrWhiteSpace(polymarketEvent.Elapsed);

            if (polymarketEvent.Closed || polymarketEvent.Ended)
            {
                return new LiveComplementaryEventState(
                    "CLOSED",
                    "BLOCKED_CLOSED",
                    AllowsCandidatePricing: false,
                    IsInPlay: false,
                    polymarketEvent.Score,
                    score.HomeGoals,
                    score.AwayGoals,
                    polymarketEvent.Elapsed,
                    elapsed,
                    polymarketEvent.StartTimeUtc,
                    "event closed/ended; no lock or hedge candidate should be trusted");
            }

            var startHasPassed = polymarketEvent.StartTimeUtc.HasValue && polymarketEvent.StartTimeUtc.Value <= polymarketEvent.FetchedAtUtc;
            var appearsInPlay = polymarketEvent.Live || hasScoreOrElapsed || startHasPassed;
            if (appearsInPlay)
            {
                if (score.HasValue && elapsed.HasValue)
                {
                    return new LiveComplementaryEventState(
                        "IN_PLAY",
                        "IN_PLAY_COMPATIBLE",
                        AllowsCandidatePricing: true,
                        IsInPlay: true,
                        polymarketEvent.Score,
                        score.HomeGoals,
                        score.AwayGoals,
                        polymarketEvent.Elapsed,
                        elapsed,
                        polymarketEvent.StartTimeUtc,
                        "score-state parsed at receive time");
                }

                return new LiveComplementaryEventState(
                    "IN_PLAY_STATE_MISSING",
                    "BLOCKED_IN_PLAY_STATE_MISSING",
                    AllowsCandidatePricing: false,
                    IsInPlay: true,
                    polymarketEvent.Score,
                    score.HomeGoals,
                    score.AwayGoals,
                    polymarketEvent.Elapsed,
                    elapsed,
                    polymarketEvent.StartTimeUtc,
                    "in-play or started event is missing parseable score and elapsed time");
            }

            return new LiveComplementaryEventState(
                "PRE_GAME",
                "PRE_GAME_ONLY",
                AllowsCandidatePricing: true,
                IsInPlay: false,
                polymarketEvent.Score,
                score.HomeGoals,
                score.AwayGoals,
                polymarketEvent.Elapsed,
                elapsed,
                polymarketEvent.StartTimeUtc,
                "pre-game only; no live score-state applied");
        }

        private static (int? HomeGoals, int? AwayGoals, bool HasValue) ParseScore(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return (null, null, false);

            var match = Regex.Match(value, @"(?<home>\d{1,2})\s*[-–—:]\s*(?<away>\d{1,2})", RegexOptions.CultureInvariant);
            if (!match.Success)
                return (null, null, false);

            return int.TryParse(match.Groups["home"].Value, out var home) &&
                   int.TryParse(match.Groups["away"].Value, out var away)
                ? (home, away, true)
                : (null, null, false);
        }

        private static int? ParseElapsedMinute(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var match = Regex.Match(value, @"(?<base>\d{1,3})(?:\s*\+\s*(?<extra>\d{1,2}))?", RegexOptions.CultureInvariant);
            if (!match.Success || !int.TryParse(match.Groups["base"].Value, out var minute))
                return null;

            if (match.Groups["extra"].Success && int.TryParse(match.Groups["extra"].Value, out var extra))
                minute += extra;

            return minute is >= 0 and <= 130 ? minute : null;
        }
    }

    public sealed record LiveComplementaryScoreConditioning(
        string Mode,
        bool Applied,
        int? CurrentHomeGoals,
        int? CurrentAwayGoals,
        int OriginalStateCount,
        int RetainedStateCount,
        int RemovedImpossibleStateCount,
        double RetainedProbabilityMass,
        string Detail);

    public sealed record LiveComplementaryGoalDecay(
        string Mode,
        bool Applied,
        double? Minute,
        double? RemainingFraction,
        double? KickoffHomeExpectedGoals,
        double? KickoffAwayExpectedGoals,
        double? RemainingHomeExpectedGoals,
        double? RemainingAwayExpectedGoals,
        double? RetainedGridMass,
        string Detail);

    public sealed record LiveComplementaryLockEventMatch(
        string EventSlug,
        string? EventId,
        string? EventTitle,
        int Score,
        string Evidence);

    public sealed record LiveComplementaryLockEventSuggestion(
        DateTimeOffset AsOfUtc,
        string FixtureId,
        string FixtureLabel,
        string? EventSlug,
        string? EventId,
        string? EventTitle,
        IReadOnlyList<LiveComplementaryLockEventMatch> Matches,
        IReadOnlyList<LiveComplementaryLockReject> Rejects)
    {
        public bool HasSuggestion => !string.IsNullOrWhiteSpace(EventSlug);
    }

    public sealed record LiveComplementaryLockCandidateRow(
        string Verdict,
        string Structure,
        string FirstLeg,
        string SecondLeg,
        decimal NetCost,
        decimal MaxSecondLegAsk,
        decimal LockedProfit,
        decimal ExpectedProfit,
        double ChanceToLock,
        double GapProbability,
        decimal WorstGapLoss,
        string CoverageScope,
        string TimeMode,
        string TimeGate,
        string Blocker)
    {
        public static LiveComplementaryLockCandidateRow FromCandidate(ComplementaryLockCandidate candidate, LiveComplementaryEventState eventState, LiveComplementaryGoalDecay goalDecay, LiveComplementaryScoreConditioning scoreConditioning) => new(
            candidate.Verdict.ToString(),
            candidate.StructureLabel,
            candidate.FirstLeg.DisplayName,
            candidate.SecondLeg.DisplayName,
            candidate.NetCost,
            candidate.MaxSecondLegAskForLock,
            candidate.LockedProfit,
            candidate.ExpectedProfit,
            candidate.ChanceToLock,
            candidate.GapProbability,
            candidate.Evaluation.WorstGapLoss,
            candidate.Evaluation.CoverageScope.ToString(),
            eventState.TimeMode,
            BuildTimeGate(eventState, goalDecay, scoreConditioning),
            BuildBlocker(candidate));

        private static string BuildBlocker(ComplementaryLockCandidate candidate)
        {
            if (candidate.Evaluation.HasCoverageGap)
            {
                var sample = candidate.GapStates.FirstOrDefault();
                return sample is null
                    ? "coverage gap"
                    : $"coverage gap e.g. {sample.HomeGoals}-{sample.AwayGoals}";
            }

            return candidate.LockedProfit > 0 ? "WATCH_ONLY: needs live depth/fill/fee proof" : "cost plus buffer removes lock profit";
        }

        private static string BuildTimeGate(LiveComplementaryEventState eventState, LiveComplementaryGoalDecay goalDecay, LiveComplementaryScoreConditioning scoreConditioning) =>
            $"{eventState.Blocker}; {goalDecay.Detail}; {scoreConditioning.Detail}";
    }

    public sealed record LiveFootballStrategyCandidateRow(
        string Verdict,
        string StrategyKind,
        string Structure,
        string Legs,
        decimal PackageAskCost,
        decimal MaxPackageCostBeforeBreak,
        decimal TotalCushion,
        decimal PerLegEqualPriceCushion,
        string BreakBasis,
        decimal NetCost,
        decimal LockedProfit,
        decimal ExpectedProfit,
        double GapProbability,
        decimal WorstGapLoss,
        double ScorelessThetaPerMinute,
        double GoalJumpExposure,
        string CoverageScope,
        string TimeMode,
        string TimeGate,
        string Blocker)
    {
        public static LiveFootballStrategyCandidateRow FromCandidate(FootballStrategyCandidate candidate, LiveComplementaryEventState eventState, LiveComplementaryGoalDecay goalDecay, LiveComplementaryScoreConditioning scoreConditioning) => new(
            candidate.Verdict.ToString(),
            candidate.StrategyKind,
            candidate.StructureLabel,
            string.Join(" + ", candidate.Legs.Select(leg => leg.DisplayName)),
            ComputePackageAskCost(candidate),
            ComputeMaxPackageCostBeforeBreak(candidate),
            ComputeTotalCushion(candidate),
            ComputePerLegEqualPriceCushion(candidate),
            ComputeBreakBasis(candidate),
            candidate.NetCost,
            candidate.LockedProfit,
            candidate.ExpectedProfit,
            candidate.GapProbability,
            candidate.WorstGapLoss,
            candidate.ScorelessThetaPerMinute,
            candidate.GoalJumpExposure,
            candidate.Evaluation.CoverageScope.ToString(),
            eventState.TimeMode,
            BuildTimeGate(eventState, goalDecay, scoreConditioning),
            BuildBlocker(candidate));

        private static string BuildBlocker(FootballStrategyCandidate candidate)
        {
            if (candidate.Evaluation.HasCoverageGap)
                return "coverage gap remains";
            if (candidate.Verdict == FootballStrategyVerdict.TruePositiveLock)
                return "WATCH_ONLY: needs depth/fill/fee proof";
            if (candidate.Verdict == FootballStrategyVerdict.BreakEvenLock)
                return "break-even before full cost proof";
            if (candidate.Verdict == FootballStrategyVerdict.MiddleHedge)
                return "middle hedge: guaranteed floor below cost";
            if (!candidate.Evaluation.HasCoverageGap && candidate.Evaluation.MinGrossPayout > 0)
                return "covered package: cost above payout floor";
            return "not structurally covered";
        }

        private static string BuildTimeGate(LiveComplementaryEventState eventState, LiveComplementaryGoalDecay goalDecay, LiveComplementaryScoreConditioning scoreConditioning) =>
            $"{eventState.Blocker}; {goalDecay.Detail}; {scoreConditioning.Detail}";

        private static decimal ComputePackageAskCost(FootballStrategyCandidate candidate) =>
            candidate.Legs.Sum(leg => leg.ExecutableNotional);

        private static decimal ComputeMaxPackageCostBeforeBreak(FootballStrategyCandidate candidate) =>
            ComputeBreakBasis(candidate) switch
            {
                "LOCK_FLOOR" => candidate.Evaluation.MinGrossPayout - candidate.Evaluation.ExecutionBuffer,
                "UPSIDE_HEDGE" => candidate.Evaluation.MaxGrossPayout - candidate.Evaluation.ExecutionBuffer,
                "GAP_UPSIDE_ONLY" => candidate.Evaluation.MaxGrossPayout - candidate.Evaluation.ExecutionBuffer,
                _ => candidate.Evaluation.MinGrossPayout - candidate.Evaluation.ExecutionBuffer
            };

        private static decimal ComputeTotalCushion(FootballStrategyCandidate candidate) =>
            ComputeMaxPackageCostBeforeBreak(candidate) - ComputePackageAskCost(candidate);

        private static decimal ComputePerLegEqualPriceCushion(FootballStrategyCandidate candidate)
        {
            var totalShares = candidate.Legs.Sum(leg => leg.Shares);
            return totalShares <= 0 ? 0m : ComputeTotalCushion(candidate) / totalShares;
        }

        private static string ComputeBreakBasis(FootballStrategyCandidate candidate) => candidate.Verdict switch
        {
            FootballStrategyVerdict.TruePositiveLock => "LOCK_FLOOR",
            FootballStrategyVerdict.BreakEvenLock => "LOCK_FLOOR",
            FootballStrategyVerdict.MiddleHedge => "UPSIDE_HEDGE",
            FootballStrategyVerdict.GapHedge => "GAP_UPSIDE_ONLY",
            _ => "NO_STRUCTURAL_COVER"
        };
    }

    public sealed record LiveComplementaryLockSnapshot(
        DateTimeOffset AsOfUtc,
        string FixtureId,
        string FixtureLabel,
        string EventSlug,
        string? EventId,
        string? EventTitle,
        LiveComplementaryEventState EventState,
        LiveComplementaryGoalDecay GoalDecay,
        LiveComplementaryScoreConditioning ScoreConditioning,
        string DistributionSource,
        int EventMarkets,
        int MappedContracts,
        int BooksFetched,
        IReadOnlyList<LiveComplementaryInputHash> InputHashes,
        IReadOnlyList<LiveComplementaryLockCandidateRow> Candidates,
        IReadOnlyList<LiveFootballStrategyCandidateRow> GeneralizedCandidates,
        IReadOnlyList<LiveComplementaryLockReject> Rejects)
    {
        public bool HasCandidates => Candidates.Count > 0;
        public int PositiveLocks => Candidates.Count(candidate => string.Equals(candidate.Verdict, "PositiveLock", StringComparison.Ordinal));
        public int GeneralizedPositiveLocks => GeneralizedCandidates.Count(candidate => string.Equals(candidate.Verdict, "TruePositiveLock", StringComparison.Ordinal));
        public LiveComplementaryInputFreshness InputFreshness => LiveComplementaryFreshness.Build(AsOfUtc, InputHashes);
        public IReadOnlyList<LiveComplementaryRejectSourceDetailCount> RejectSourceDetailCounts => LiveComplementaryRollups.RollupRejects(Rejects);
        public IReadOnlyList<LiveComplementaryCandidateBlockerCount> CandidateBlockerCounts => LiveComplementaryRollups.RollupCandidateBlockers(
            Candidates.Select(candidate => candidate.Blocker).Concat(GeneralizedCandidates.Select(candidate => candidate.Blocker)));
    }

    public sealed record LiveComplementaryBatchScanOptions
    {
        public int MaxFixtures { get; init; } = 48;
        public int MaxCandidatesPerFixture { get; init; } = 8;
        public int MaxTotalCandidates { get; init; } = 80;
        public decimal SharesPerLeg { get; init; } = 5m;
        public decimal ExecutionBuffer { get; init; } = .02m;
    }

    public sealed record LiveComplementaryBatchFixtureRow(
        string FixtureId,
        string FixtureLabel,
        string Status,
        string? EventSlug,
        string? EventId,
        string? EventTitle,
        int EventMatchScore,
        int EventMarkets,
        int MappedContracts,
        int BooksFetched,
        int TwoLegCandidates,
        int GeneralizedCandidates,
        int PositiveLocks,
        int GeneralizedPositiveLocks,
        int Rejects,
        string Blocker);

    public sealed record LiveFootballStrategyBatchCandidateRow(
        string FixtureId,
        string FixtureLabel,
        string EventSlug,
        string? EventTitle,
        string Verdict,
        string StrategyKind,
        string Structure,
        string Legs,
        decimal PackageAskCost,
        decimal MaxPackageCostBeforeBreak,
        decimal TotalCushion,
        decimal PerLegEqualPriceCushion,
        string BreakBasis,
        decimal NetCost,
        decimal LockedProfit,
        decimal ExpectedProfit,
        double GapProbability,
        decimal WorstGapLoss,
        double ScorelessThetaPerMinute,
        double GoalJumpExposure,
        string CoverageScope,
        string TimeMode,
        string TimeGate,
        string Blocker)
    {
        public static LiveFootballStrategyBatchCandidateRow FromCandidate(LiveComplementaryLockSnapshot snapshot, LiveFootballStrategyCandidateRow candidate) => new(
            snapshot.FixtureId,
            snapshot.FixtureLabel,
            snapshot.EventSlug,
            snapshot.EventTitle,
            candidate.Verdict,
            candidate.StrategyKind,
            candidate.Structure,
            candidate.Legs,
            candidate.PackageAskCost,
            candidate.MaxPackageCostBeforeBreak,
            candidate.TotalCushion,
            candidate.PerLegEqualPriceCushion,
            candidate.BreakBasis,
            candidate.NetCost,
            candidate.LockedProfit,
            candidate.ExpectedProfit,
            candidate.GapProbability,
            candidate.WorstGapLoss,
            candidate.ScorelessThetaPerMinute,
            candidate.GoalJumpExposure,
            candidate.CoverageScope,
            candidate.TimeMode,
            candidate.TimeGate,
            candidate.Blocker);
    }

    public sealed record LiveComplementaryBatchScanSnapshot(
        DateTimeOffset AsOfUtc,
        string Mode,
        int RequestedMaxFixtures,
        int FixturesSeen,
        int FixturesScanned,
        int EventMatches,
        int EventlessFixtures,
        int AmbiguousEvents,
        int TotalEventMarkets,
        int TotalMappedContracts,
        int TotalBooksFetched,
        IReadOnlyList<LiveComplementaryInputHash> InputHashes,
        IReadOnlyList<LiveComplementaryBatchFixtureRow> Fixtures,
        IReadOnlyList<LiveFootballStrategyBatchCandidateRow> Candidates,
        IReadOnlyList<LiveComplementaryLockReject> Rejects)
    {
        public int TruePositiveLocks => Candidates.Count(candidate => string.Equals(candidate.Verdict, "TruePositiveLock", StringComparison.Ordinal));
        public bool HasCandidates => Candidates.Count > 0;
        public LiveComplementaryInputFreshness InputFreshness => LiveComplementaryFreshness.Build(AsOfUtc, InputHashes);
        public IReadOnlyList<LiveComplementaryRejectSourceDetailCount> RejectSourceDetailCounts => LiveComplementaryRollups.RollupRejects(Rejects);
        public IReadOnlyList<LiveComplementaryCandidateBlockerCount> CandidateBlockerCounts => LiveComplementaryRollups.RollupCandidateBlockers(Candidates.Select(candidate => candidate.Blocker));
        public IReadOnlyList<LiveComplementaryFixtureStatusCount> FixtureScanStatusCounts => Fixtures
            .GroupBy(fixture => LiveComplementaryRollups.CleanRollupText(fixture.Status), StringComparer.OrdinalIgnoreCase)
            .Select(group => new LiveComplementaryFixtureStatusCount(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Status, StringComparer.Ordinal)
            .ToList();
    }

    internal static class LiveComplementaryRollups
    {
        public static IReadOnlyList<LiveComplementaryRejectSourceDetailCount> RollupRejects(IEnumerable<LiveComplementaryLockReject> rejects) => rejects
            .GroupBy(reject => new { reject.Source, Detail = CleanRollupText(reject.Detail) })
            .Select(group => new LiveComplementaryRejectSourceDetailCount(group.Key.Source, group.Key.Detail, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Source.ToString(), StringComparer.Ordinal)
            .ThenBy(item => item.Detail, StringComparer.Ordinal)
            .ToList();

        public static IReadOnlyList<LiveComplementaryCandidateBlockerCount> RollupCandidateBlockers(IEnumerable<string> blockers) => blockers
            .Select(CleanRollupText)
            .Where(blocker => !string.IsNullOrWhiteSpace(blocker))
            .GroupBy(blocker => blocker, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LiveComplementaryCandidateBlockerCount(group.Key, group.Count()))
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Blocker, StringComparer.Ordinal)
            .ToList();

        public static string CleanRollupText(string? value) => string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim();
    }

    internal static class LiveComplementaryFreshness
    {
        private const double FreshSeconds = 120.0;

        public static LiveComplementaryInputFreshness Build(DateTimeOffset asOfUtc, IReadOnlyList<LiveComplementaryInputHash> inputs)
        {
            if (inputs.Count == 0)
            {
                return new LiveComplementaryInputFreshness(
                    "NO_INPUTS",
                    InputCount: 0,
                    GammaInputCount: 0,
                    ClobBookInputCount: 0,
                    OldestFetchedAtUtc: null,
                    NewestFetchedAtUtc: null,
                    MaxAgeSeconds: 0,
                    SpanSeconds: 0,
                    "no public input hashes captured");
            }

            var oldest = inputs.Min(input => input.FetchedAtUtc);
            var newest = inputs.Max(input => input.FetchedAtUtc);
            var maxAge = Math.Max(0, (asOfUtc - oldest).TotalSeconds);
            var span = Math.Max(0, (newest - oldest).TotalSeconds);
            var gamma = inputs.Count(input => input.Source.StartsWith("gamma", StringComparison.OrdinalIgnoreCase));
            var clob = inputs.Count(input => string.Equals(input.Source, "clob-book", StringComparison.OrdinalIgnoreCase));
            var status = maxAge <= FreshSeconds ? "FRESH" : "STALE";
            return new LiveComplementaryInputFreshness(
                status,
                inputs.Count,
                gamma,
                clob,
                oldest,
                newest,
                maxAge,
                span,
                $"{inputs.Count} public input hashes; oldest age {maxAge:0.0}s; capture span {span:0.0}s; gamma {gamma}; clob books {clob}");
        }
    }

    public sealed record LiveComplementaryLockCheckpoint(
        DateTimeOffset GeneratedAtUtc,
        string Verdict,
        string SnapshotHash,
        string PayloadJson,
        IReadOnlyList<string> RequiredValidationGates)
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public static LiveComplementaryLockCheckpoint Create(LiveComplementaryLockSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var gates = RequiredGates();
            var payload = JsonSerializer.Serialize(new
            {
                Schema = "world-cup-edge-lab/live-lock-checkpoint/v1",
                Mode = "WATCH_ONLY_PUBLIC_READS",
                LiveOrderPath = "none",
                Verdict = "HOLD_WATCH_ONLY",
                snapshot.AsOfUtc,
                snapshot.FixtureId,
                snapshot.FixtureLabel,
                snapshot.EventSlug,
                snapshot.EventId,
                snapshot.EventTitle,
                snapshot.EventState,
                snapshot.GoalDecay,
                snapshot.ScoreConditioning,
                snapshot.DistributionSource,
                snapshot.EventMarkets,
                snapshot.MappedContracts,
                snapshot.BooksFetched,
                snapshot.InputFreshness,
                InputHashes = snapshot.InputHashes
                    .OrderBy(input => input.Source, StringComparer.Ordinal)
                    .ThenBy(input => input.EventSlug, StringComparer.Ordinal)
                    .ThenBy(input => input.TokenId, StringComparer.Ordinal)
                    .ThenBy(input => input.PayloadHash, StringComparer.Ordinal),
                RequiredValidationGates = gates,
                Candidates = snapshot.Candidates
                    .OrderBy(candidate => candidate.Verdict, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Structure, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.FirstLeg, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.SecondLeg, StringComparer.Ordinal),
                GeneralizedCandidates = snapshot.GeneralizedCandidates
                    .OrderBy(candidate => candidate.Verdict, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.StrategyKind, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Structure, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Legs, StringComparer.Ordinal),
                Rejects = snapshot.Rejects
                    .OrderBy(reject => reject.Source.ToString(), StringComparer.Ordinal)
                    .ThenBy(reject => reject.MarketId, StringComparer.Ordinal)
                    .ThenBy(reject => reject.TokenId, StringComparer.Ordinal)
                    .ThenBy(reject => reject.Detail, StringComparer.Ordinal)
            }, JsonOptions);

            return new LiveComplementaryLockCheckpoint(
                DateTimeOffset.UtcNow,
                "HOLD_WATCH_ONLY",
                CryptoUtil.GetSha256(payload),
                payload,
                gates);
        }

        private static IReadOnlyList<string> RequiredGates() =>
        [
            "exact fixture-to-Polymarket event identity review",
            "public CLOB /books depth captured at receive time",
            "fees, slippage, and partial-fill loss bounds missing",
            "authenticated fills and markouts missing",
            "Oloraculo live-order gates are not armed"
        ];
    }

    public sealed record LiveComplementaryBatchScanCheckpoint(
        DateTimeOffset GeneratedAtUtc,
        string Verdict,
        string SnapshotHash,
        string PayloadJson,
        IReadOnlyList<string> RequiredValidationGates)
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public static LiveComplementaryBatchScanCheckpoint Create(LiveComplementaryBatchScanSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var gates = RequiredGates();
            var payload = JsonSerializer.Serialize(new
            {
                Schema = "world-cup-edge-lab/batch-lock-checkpoint/v1",
                Mode = "WATCH_ONLY_PUBLIC_READS",
                LiveOrderPath = "none",
                Verdict = "HOLD_WATCH_ONLY",
                snapshot.AsOfUtc,
                snapshot.RequestedMaxFixtures,
                snapshot.FixturesSeen,
                snapshot.FixturesScanned,
                snapshot.EventMatches,
                snapshot.EventlessFixtures,
                snapshot.AmbiguousEvents,
                snapshot.TotalEventMarkets,
                snapshot.TotalMappedContracts,
                snapshot.TotalBooksFetched,
                snapshot.InputFreshness,
                InputHashes = snapshot.InputHashes
                    .OrderBy(input => input.Source, StringComparer.Ordinal)
                    .ThenBy(input => input.EventSlug, StringComparer.Ordinal)
                    .ThenBy(input => input.TokenId, StringComparer.Ordinal)
                    .ThenBy(input => input.PayloadHash, StringComparer.Ordinal),
                RequiredValidationGates = gates,
                Fixtures = snapshot.Fixtures
                    .OrderBy(fixture => fixture.FixtureId, StringComparer.Ordinal)
                    .ThenBy(fixture => fixture.EventSlug, StringComparer.Ordinal),
                Candidates = snapshot.Candidates
                    .OrderBy(candidate => candidate.Verdict, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.FixtureId, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.StrategyKind, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Structure, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Legs, StringComparer.Ordinal),
                Rejects = snapshot.Rejects
                    .OrderBy(reject => reject.Source.ToString(), StringComparer.Ordinal)
                    .ThenBy(reject => reject.MarketId, StringComparer.Ordinal)
                    .ThenBy(reject => reject.TokenId, StringComparer.Ordinal)
                    .ThenBy(reject => reject.Detail, StringComparer.Ordinal)
            }, JsonOptions);

            return new LiveComplementaryBatchScanCheckpoint(
                DateTimeOffset.UtcNow,
                "HOLD_WATCH_ONLY",
                CryptoUtil.GetSha256(payload),
                payload,
                gates);
        }

        private static IReadOnlyList<string> RequiredGates() =>
        [
            "batch event suggestions require human fixture-to-Polymarket identity review",
            "public CLOB /books depth captured at receive time for each visible candidate",
            "fees, slippage, and partial-fill loss bounds missing",
            "authenticated fills and markouts missing",
            "Oloraculo live-order gates are not armed"
        ];
    }
}
