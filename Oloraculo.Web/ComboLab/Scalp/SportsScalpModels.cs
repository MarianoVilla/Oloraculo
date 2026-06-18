using Oloraculo.Web.ComboLab.Markets;

namespace Oloraculo.Web.ComboLab.Scalp
{
    public sealed record SportsScalpScanOptions
    {
        public int MaxEvents { get; init; } = 24;
        public int MaxTokenBooks { get; init; } = 80;
        public decimal TargetShares { get; init; } = 400m;
        public bool IncludeGenericActiveEvents { get; init; } = true;
        public bool IncludeWorldCupPriorityEvents { get; init; } = true;
        public bool IncludeDebugEvents { get; init; }
        public DateTimeOffset? NowUtc { get; init; }
    }

    public sealed record SportsScalpSnapshot
    {
        public DateTimeOffset AsOfUtc { get; init; }
        public DateTimeOffset NearWindowStartUtc { get; init; }
        public DateTimeOffset NearWindowEndUtc { get; init; }
        public DateTimeOffset LastUniverseRefreshUtc { get; init; }
        public DateTimeOffset? LastOrderbookRefreshUtc { get; init; }
        public double DataAgeSeconds => Math.Max(0, (AsOfUtc - LastUniverseRefreshUtc).TotalSeconds);
        public TimeSpan ScanDuration { get; init; }
        public SportsScalpScanOptions Options { get; init; } = new();
        public IReadOnlyList<SportsScalpEventRow> Events { get; init; } = [];
        public IReadOnlyList<SportsScalpExcludedEventDiagnostic> ExcludedEvents { get; init; } = [];
        public IReadOnlyList<SportsScalpCandidateRow> Candidates { get; init; } = [];
        public IReadOnlyList<SportsScalpBlockerCount> BlockerCounts { get; init; } = [];
        public SportsScalpDiagnostics Diagnostics { get; init; } = new();
        public string Mode => "WATCH_ONLY_PUBLIC_READS";
        public int EventCount => Events.Count;
        public int CandidateCount => Candidates.Count;
        public int TradeNowCount => Candidates.Count(candidate => candidate.Verdict == SportsScalpVerdict.TradeNow);
        public int WatchCount => Candidates.Count(candidate => candidate.Verdict == SportsScalpVerdict.Watch);
        public int BlockedCount => Candidates.Count(candidate => candidate.Verdict == SportsScalpVerdict.Blocked);
        public DateTimeOffset? OldestQuoteUtc => Candidates.SelectMany(candidate => new[] { candidate.EntryFetchedAtUtc, candidate.HedgeFetchedAtUtc }).Where(value => value.HasValue).MinBy(value => value);
        public double OldestQuoteAgeSeconds => OldestQuoteUtc.HasValue ? Math.Max(0, (AsOfUtc - OldestQuoteUtc.Value).TotalSeconds) : 0;
    }

    public sealed record SportsScalpEventRow(
        string EventId,
        string Slug,
        string Title,
        string Priority,
        bool Active,
        bool Live,
        bool Closed,
        DateTimeOffset? StartTimeUtc,
        string? Score,
        string? Elapsed,
        int MarketCount,
        int CandidateCount,
        string Status,
        bool IsNear = false,
        string IncludeReason = "",
        string ExcludeReason = "",
        string Source = "polymarket_gamma",
        int TokenIdCount = 0,
        int LiveOrderBookCount = 0,
        decimal? GammaLiquidityUsd = null,
        decimal? GammaVolume24hUsd = null,
        decimal? ClobTopDepthUsd = null,
        decimal? ClobDepth2cUsd = null,
        decimal? ClobDepth5cUsd = null,
        string LiquiditySource = "unknown",
        string LiquidityQuality = "unknown",
        double DataAgeSeconds = 0,
        string ExternalMatchStatus = "unmatched",
        IReadOnlyList<string>? DataQualityFlags = null);

    public sealed record SportsScalpExcludedEventDiagnostic(
        string EventId,
        string Slug,
        string Title,
        string ExcludeReason,
        string? DiagnosticBucket,
        DateTimeOffset? StartTimeUtc,
        bool Active,
        bool Closed,
        bool Archived,
        int MarketCount,
        int TokenIdCount);

    public sealed record SportsScalpDiagnostics
    {
        public DateTimeOffset NowUtc { get; init; }
        public DateTimeOffset UniverseRefreshStartedUtc { get; init; }
        public DateTimeOffset UniverseRefreshFinishedUtc { get; init; }
        public int ActiveEventsFetched { get; init; }
        public int ActiveMarketsFetched { get; init; }
        public int NearEventsCount { get; init; }
        public int OldEventsExcludedCount { get; init; }
        public int ExternalOnlyEventsExcludedCount { get; init; }
        public int MarketsMissingTokenIdsCount { get; init; }
        public int TokenBooksRequestedCount { get; init; }
        public int TokenBooksOkCount { get; init; }
        public int TokenBooksFailedCount { get; init; }
        public int TokenBooksEmptyCount { get; init; }
        public int LiquidityUnknownCount { get; init; }
        public int LiquidityZeroConfirmedCount { get; init; }
        public int LiquidityNonZeroCount { get; init; }
        public IReadOnlyList<string> StaleCacheWarnings { get; init; } = [];
        public IReadOnlyDictionary<string, int> ApiErrorsByEndpoint { get; init; } = new Dictionary<string, int>();
        public string? LastError { get; init; }
        public int WorldCupEventsIncluded { get; init; }
        public int ClobBooksHydrated { get; init; }
    }

    public enum SportsScalpVerdict
    {
        TradeNow,
        Watch,
        Blocked,
        Reject
    }

    public sealed record SportsScalpCandidateRow
    {
        public SportsScalpVerdict Verdict { get; init; }
        public string EventId { get; init; } = string.Empty;
        public string EventSlug { get; init; } = string.Empty;
        public string EventTitle { get; init; } = string.Empty;
        public string EventPriority { get; init; } = "SPORTS";
        public bool EventLive { get; init; }
        public DateTimeOffset? StartTimeUtc { get; init; }
        public string? Score { get; init; }
        public string? Elapsed { get; init; }
        public string MarketId { get; init; } = string.Empty;
        public string ConditionId { get; init; } = string.Empty;
        public string MarketQuestion { get; init; } = string.Empty;
        public string MarketFamily { get; init; } = string.Empty;
        public decimal? Line { get; init; }
        public string EntryOutcome { get; init; } = string.Empty;
        public string EntryTokenId { get; init; } = string.Empty;
        public decimal? EntryAsk { get; init; }
        public decimal? EntryAskSize { get; init; }
        public decimal? EntryVwap { get; init; }
        public decimal? EntryWorstAsk { get; init; }
        public decimal? EntryFillableShares { get; init; }
        public DateTimeOffset? EntryFetchedAtUtc { get; init; }
        public string EntryBookStatus { get; init; } = "missing_token";
        public string HedgeOutcome { get; init; } = string.Empty;
        public string HedgeTokenId { get; init; } = string.Empty;
        public decimal? HedgeAskNow { get; init; }
        public decimal? HedgeAskSize { get; init; }
        public decimal? HedgeVwapNow { get; init; }
        public decimal? HedgeWorstAskNow { get; init; }
        public decimal? HedgeFillableShares { get; init; }
        public DateTimeOffset? HedgeFetchedAtUtc { get; init; }
        public string HedgeBookStatus { get; init; } = "missing_token";
        public decimal TargetShares { get; init; } = 400m;
        public decimal? PairCostNow => EntryVwap.HasValue && HedgeVwapNow.HasValue ? EntryVwap.Value + HedgeVwapNow.Value : null;
        public decimal? LockedProfitPerShareNow => PairCostNow.HasValue ? 1m - PairCostNow.Value : null;
        public decimal? LockedProfitNow => LockedProfitPerShareNow.HasValue ? LockedProfitPerShareNow.Value * TargetShares : null;
        public decimal? RoiNow => LockedProfitPerShareNow.HasValue && PairCostNow is > 0 ? LockedProfitPerShareNow.Value / PairCostNow.Value : null;
        public bool HasTargetDepthNow => EntryFillableShares >= TargetShares && HedgeFillableShares >= TargetShares;
        public SportsScalpHedgeTargets HedgeTargets { get; init; } = SportsScalpHedgeTargets.Empty;
        public IReadOnlyList<SportsScalpLadderLevel> SuggestedHedgeLadder { get; init; } = [];
        public string Trigger { get; init; } = string.Empty;
        public string FailurePlan { get; init; } = string.Empty;
        public string Blocker { get; init; } = string.Empty;
        public string SourceDetail { get; init; } = string.Empty;
    }

    public sealed record SportsScalpHedgeTargets(
        decimal? Breakeven,
        decimal? Roi2,
        decimal? Roi5,
        decimal? Roi8,
        decimal? Roi10,
        decimal? Roi12)
    {
        public static SportsScalpHedgeTargets Empty { get; } = new(null, null, null, null, null, null);
    }

    public sealed record SportsScalpLadderLevel(decimal Shares, decimal Price, decimal Cost);

    public sealed record SportsScalpBlockerCount(string Blocker, int Count);
}
