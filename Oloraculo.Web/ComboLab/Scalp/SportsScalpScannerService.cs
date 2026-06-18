using Oloraculo.Web.ComboLab.Markets;
using System.Diagnostics;

namespace Oloraculo.Web.ComboLab.Scalp
{
    public sealed class SportsScalpScannerService
    {
        private const int GammaPageSize = 100;
        private const int MaxGammaPages = 10;
        private readonly PolymarketMarketDataService _markets;

        public SportsScalpScannerService(PolymarketMarketDataService markets) => _markets = markets;

        public async Task<SportsScalpSnapshot> ScanAsync(SportsScalpScanOptions? options = null, CancellationToken ct = default)
        {
            options ??= new SportsScalpScanOptions();
            var started = Stopwatch.StartNew();
            var asOf = (options.NowUtc ?? UtcClock.Now()).ToUniversalTime();
            var nearWindow = SportsScalpNearWindow.Create(asOf);
            var apiErrors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string? lastError = null;

            var universeStartedUtc = asOf;
            var events = await FetchActiveGammaEventsAsync(options, apiErrors, error => lastError = error, ct);
            var universeFinishedUtc = options.NowUtc?.ToUniversalTime() ?? UtcClock.Now();

            var eventRows = new List<SportsScalpEventRow>();
            var excludedRows = new List<SportsScalpExcludedEventDiagnostic>();
            var candidates = new List<SportsScalpCandidateRow>();
            var hydratedBooks = new Dictionary<string, HydratedBook>(StringComparer.OrdinalIgnoreCase);
            var tokenBudget = Math.Max(0, options.MaxTokenBooks);

            var decisions = events
                .Select(ev => (Event: ev, Decision: nearWindow.Evaluate(ev, options.IncludeDebugEvents)))
                .ToList();

            foreach (var item in decisions.Where(item => !item.Decision.IsNear))
            {
                excludedRows.Add(new SportsScalpExcludedEventDiagnostic(
                    item.Event.EventId ?? string.Empty,
                    item.Event.Slug ?? string.Empty,
                    item.Event.Title ?? string.Empty,
                    item.Decision.ExcludeReason ?? string.Empty,
                    item.Decision.DiagnosticBucket,
                    item.Decision.UtcStartTime,
                    item.Event.Active,
                    item.Event.Closed,
                    item.Event.Archived,
                    item.Event.Markets.Count,
                    item.Event.Markets.Sum(market => market.ClobTokenIds.Count)));
            }

            var nearEvents = decisions
                .Where(item => item.Decision.IsNear)
                .OrderByDescending(item => item.Event.Live)
                .ThenByDescending(item => Priority(item.Event) == "WORLD_CUP")
                .ThenBy(item => item.Event.StartTimeUtc ?? DateTimeOffset.MaxValue)
                .ThenByDescending(item => EventVolume24h(item.Event))
                .Take(options.MaxEvents)
                .ToList();

            foreach (var (ev, decision) in nearEvents)
            {
                ct.ThrowIfCancellationRequested();
                var eventCandidates = new List<SportsScalpCandidateRow>();
                var eventTokenIds = ev.Markets
                    .SelectMany(market => market.Tokens.Select(token => token.TokenId))
                    .Where(tokenId => !string.IsNullOrWhiteSpace(tokenId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                await FetchBooksWithinBudgetAsync(eventTokenIds, hydratedBooks, apiErrors, error => lastError = error, count => tokenBudget -= count, tokenBudget, ct);

                foreach (var market in ev.Markets)
                {
                    var classification = PolymarketFootballMarketClassifier.Classify(market);
                    if (!IsSupportedScalpFamily(classification.Family))
                        continue;

                    if (market.Tokens.Count < 2 || market.RejectReasons.Count > 0)
                    {
                        eventCandidates.Add(BlockedCandidate(ev, market, classification.Family, options.TargetShares, string.Join(", ", market.RejectReasons.DefaultIfEmpty(PolymarketRejectReason.UnclassifiedRules))));
                        continue;
                    }

                    var tokenA = market.Tokens[0];
                    var tokenB = market.Tokens[1];
                    var bookA = HydratedFor(hydratedBooks, tokenA.TokenId);
                    var bookB = HydratedFor(hydratedBooks, tokenB.TokenId);
                    eventCandidates.Add(BuildCandidate(ev, market, classification.Family, tokenA, tokenB, bookA, bookB, options.TargetShares));
                    eventCandidates.Add(BuildCandidate(ev, market, classification.Family, tokenB, tokenA, bookB, bookA, options.TargetShares));
                }

                eventCandidates = eventCandidates
                    .OrderBy(candidate => candidate.Verdict)
                    .ThenBy(candidate => candidate.HedgeTargets.Roi8 ?? 0m)
                    .Take(12)
                    .ToList();
                candidates.AddRange(eventCandidates);

                var eventBookTops = eventTokenIds
                    .Select(tokenId => hydratedBooks.TryGetValue(tokenId, out var hydrated) ? hydrated.Top : null)
                    .Where(top => top is not null)
                    .Select(top => top!)
                    .ToList();
                var liquidity = BuildLiquidity(ev, eventBookTops);
                var dataQualityFlags = ev.Markets
                    .SelectMany(market => market.DataQuality.Flags)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(flag => flag, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                eventRows.Add(new SportsScalpEventRow(
                    ev.EventId ?? string.Empty,
                    ev.Slug ?? string.Empty,
                    ev.Title ?? string.Empty,
                    Priority(ev),
                    ev.Active,
                    ev.Live,
                    ev.Closed,
                    decision.UtcStartTime,
                    ev.Score,
                    ev.Elapsed,
                    ev.Markets.Count,
                    eventCandidates.Count,
                    ev.Closed ? "CLOSED" : ev.Live ? "LIVE" : decision.UtcStartTime.HasValue ? "SCHEDULED" : "NO_TIMING",
                    IsNear: decision.IsNear,
                    IncludeReason: decision.IncludeReason ?? string.Empty,
                    ExcludeReason: decision.ExcludeReason ?? string.Empty,
                    Source: "polymarket_gamma",
                    TokenIdCount: eventTokenIds.Count,
                    LiveOrderBookCount: eventBookTops.Count(top => top.RawBookStatus == TokenBookRawStatus.Ok),
                    GammaLiquidityUsd: liquidity.GammaLiquidityUsd,
                    GammaVolume24hUsd: liquidity.GammaVolume24hUsd,
                    ClobTopDepthUsd: liquidity.ClobTopDepthUsd,
                    ClobDepth2cUsd: liquidity.ClobDepth2cUsd,
                    ClobDepth5cUsd: liquidity.ClobDepth5cUsd,
                    LiquiditySource: liquidity.LiquiditySource,
                    LiquidityQuality: liquidity.LiquidityQuality,
                    DataAgeSeconds: Math.Max(0, (asOf - ev.FetchedAtUtc).TotalSeconds),
                    ExternalMatchStatus: "unmatched",
                    DataQualityFlags: dataQualityFlags));
            }

            started.Stop();
            var blockerCounts = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Blocker))
                .GroupBy(candidate => candidate.Blocker)
                .Select(group => new SportsScalpBlockerCount(group.Key, group.Count()))
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Blocker, StringComparer.Ordinal)
                .ToList();

            var bookTops = hydratedBooks.Values.Select(value => value.Top).ToList();
            var lastOrderbookRefreshUtc = bookTops.Count == 0 ? (DateTimeOffset?)null : bookTops.Max(top => top.LastUpdatedUtc);
            var diagnostics = new SportsScalpDiagnostics
            {
                NowUtc = asOf,
                UniverseRefreshStartedUtc = universeStartedUtc,
                UniverseRefreshFinishedUtc = universeFinishedUtc,
                ActiveEventsFetched = events.Count,
                ActiveMarketsFetched = events.Sum(ev => ev.Markets.Count),
                NearEventsCount = eventRows.Count,
                OldEventsExcludedCount = excludedRows.Count(row => string.Equals(row.ExcludeReason, "outside_near_window", StringComparison.OrdinalIgnoreCase)),
                ExternalOnlyEventsExcludedCount = 0,
                MarketsMissingTokenIdsCount = nearEvents.Sum(item => item.Event.Markets.Count(market => market.DataQuality.MissingTokenIds)),
                TokenBooksRequestedCount = bookTops.Count(top => top.RawBookStatus is not TokenBookRawStatus.MissingToken and not TokenBookRawStatus.Stale),
                TokenBooksOkCount = bookTops.Count(top => top.RawBookStatus == TokenBookRawStatus.Ok),
                TokenBooksFailedCount = bookTops.Count(top => top.RawBookStatus == TokenBookRawStatus.FetchFailed),
                TokenBooksEmptyCount = bookTops.Count(top => top.RawBookStatus == TokenBookRawStatus.EmptyBook),
                LiquidityUnknownCount = eventRows.Count(row => row.LiquidityQuality == "unknown"),
                LiquidityZeroConfirmedCount = nearEvents.Sum(item => item.Event.Markets.Count(market => market.DataQuality.LiquidityZeroConfirmed)),
                LiquidityNonZeroCount = eventRows.Count(row => (row.ClobDepth2cUsd ?? row.GammaLiquidityUsd) > 0),
                ApiErrorsByEndpoint = apiErrors,
                LastError = lastError,
                WorldCupEventsIncluded = eventRows.Count(row => row.Priority == "WORLD_CUP"),
                ClobBooksHydrated = bookTops.Count(top => top.RawBookStatus == TokenBookRawStatus.Ok)
            };

            return new SportsScalpSnapshot
            {
                AsOfUtc = asOf,
                NearWindowStartUtc = nearWindow.WindowStartUtc,
                NearWindowEndUtc = nearWindow.WindowEndUtc,
                LastUniverseRefreshUtc = universeFinishedUtc,
                LastOrderbookRefreshUtc = lastOrderbookRefreshUtc,
                ScanDuration = started.Elapsed,
                Options = options,
                Events = eventRows
                    .OrderByDescending(row => row.Live)
                    .ThenBy(row => row.StartTimeUtc ?? DateTimeOffset.MaxValue)
                    .ThenByDescending(row => row.ClobDepth2cUsd ?? row.GammaLiquidityUsd ?? 0m)
                    .ToList(),
                ExcludedEvents = excludedRows,
                Candidates = candidates
                    .OrderBy(candidate => candidate.Verdict)
                    .ThenByDescending(candidate => candidate.LockedProfitNow ?? decimal.MinValue)
                    .ThenBy(candidate => candidate.StartTimeUtc ?? DateTimeOffset.MaxValue)
                    .Take(80)
                    .ToList(),
                BlockerCounts = blockerCounts,
                Diagnostics = diagnostics
            };
        }

        public static SportsScalpHedgeTargets BuildTargets(decimal? entryPrice)
        {
            if (!entryPrice.HasValue || entryPrice <= 0 || entryPrice >= 1)
                return SportsScalpHedgeTargets.Empty;

            decimal Target(decimal roi) => Math.Round((1m / (1m + roi)) - entryPrice.Value, 4, MidpointRounding.AwayFromZero);
            return new SportsScalpHedgeTargets(
                Math.Round(1m - entryPrice.Value, 4, MidpointRounding.AwayFromZero),
                Target(.02m),
                Target(.05m),
                Target(.08m),
                Target(.10m),
                Target(.12m));
        }

        public static IReadOnlyList<SportsScalpLadderLevel> BuildDefaultLadder(decimal? entryPrice, decimal shares = 400m)
        {
            var targets = BuildTargets(entryPrice);
            if (!targets.Roi8.HasValue || shares <= 0)
                return [];

            var anchor = targets.Roi8.Value;
            var levels = new[]
            {
                new SportsScalpLadderLevel(Math.Round(shares * .1875m, 4), Math.Round(anchor + .03m, 4), Math.Round(shares * .1875m * (anchor + .03m), 4)),
                new SportsScalpLadderLevel(Math.Round(shares * .3125m, 4), Math.Round(anchor + .01m, 4), Math.Round(shares * .3125m * (anchor + .01m), 4)),
                new SportsScalpLadderLevel(Math.Round(shares * .375m, 4), Math.Round(anchor - .01m, 4), Math.Round(shares * .375m * (anchor - .01m), 4)),
                new SportsScalpLadderLevel(Math.Round(shares * .125m, 4), Math.Round(anchor - .03m, 4), Math.Round(shares * .125m * (anchor - .03m), 4))
            };
            return levels.Where(level => level.Price is > 0m and < 1m).ToList();
        }

        private async Task<IReadOnlyList<PolymarketEventSnapshot>> FetchActiveGammaEventsAsync(
            SportsScalpScanOptions options,
            Dictionary<string, int> apiErrors,
            Action<string> recordLastError,
            CancellationToken ct)
        {
            if (!options.IncludeGenericActiveEvents && !options.IncludeWorldCupPriorityEvents)
                return [];

            var combined = new List<PolymarketEventSnapshot>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (options.IncludeWorldCupPriorityEvents)
            {
                try
                {
                    var worldCupEvents = await _markets.FetchWorldCupEventsPagedAsync(maxPages: MaxGammaPages, limit: GammaPageSize, ct: ct);
                    AddDistinctEvents(combined, seen, worldCupEvents);
                }
                catch (Exception ex)
                {
                    Increment(apiErrors, "gamma-events-world-cup");
                    recordLastError($"Gamma World Cup series keyset fetch failed: {ex.Message}");
                }
            }

            if (!options.IncludeGenericActiveEvents)
                return combined;

            string? cursor = null;
            for (var page = 0; page < MaxGammaPages; page++)
            {
                PolymarketEventPage eventPage;
                try
                {
                    eventPage = await _markets.FetchEventsPageAsync(limit: GammaPageSize, cursor: cursor, active: true, closed: false, ct: ct);
                }
                catch (Exception ex)
                {
                    Increment(apiErrors, "gamma-events-active");
                    recordLastError($"Gamma active event keyset fetch failed at cursor {cursor ?? "<first>"}: {ex.Message}");
                    break;
                }

                AddDistinctEvents(combined, seen, eventPage.Events);

                if (string.IsNullOrWhiteSpace(eventPage.NextCursor))
                    break;
                cursor = eventPage.NextCursor;
            }

            return combined;
        }

        private async Task FetchBooksWithinBudgetAsync(
            IReadOnlyList<string> eventTokenIds,
            Dictionary<string, HydratedBook> hydratedBooks,
            Dictionary<string, int> apiErrors,
            Action<string> recordLastError,
            Action<int> decrementBudget,
            int remainingBudget,
            CancellationToken ct)
        {
            var nowUtc = UtcClock.Now();
            var toFetch = new List<string>();
            foreach (var tokenId in eventTokenIds)
            {
                if (string.IsNullOrWhiteSpace(tokenId) || hydratedBooks.ContainsKey(tokenId))
                    continue;

                if (remainingBudget <= toFetch.Count)
                {
                    hydratedBooks[tokenId] = new HydratedBook(null, new TokenBookTop
                    {
                        TokenId = tokenId,
                        RawBookStatus = TokenBookRawStatus.Stale,
                        LastUpdatedUtc = nowUtc,
                        Error = "token book budget exhausted"
                    });
                    continue;
                }

                toFetch.Add(tokenId);
            }

            if (toFetch.Count == 0)
                return;

            decrementBudget(toFetch.Count);
            IReadOnlyList<PolymarketOrderBookSnapshot> batch;
            try
            {
                batch = await _markets.FetchBooksAsync(toFetch, ct);
            }
            catch (Exception ex)
            {
                Increment(apiErrors, "clob-books-batch");
                var message = $"CLOB batch book fetch failed: {ex.Message}";
                recordLastError(message);
                foreach (var tokenId in toFetch)
                    hydratedBooks[tokenId] = new HydratedBook(null, TokenBookTop.FetchFailed(tokenId, nowUtc, message));
                return;
            }

            var byToken = batch
                .Where(book => !string.IsNullOrWhiteSpace(book.TokenId))
                .GroupBy(book => book.TokenId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var tokenId in toFetch)
            {
                if (byToken.TryGetValue(tokenId, out var book))
                {
                    hydratedBooks[tokenId] = new HydratedBook(book, TokenBookTop.FromBook(book));
                    continue;
                }

                hydratedBooks[tokenId] = new HydratedBook(null, TokenBookTop.FetchFailed(tokenId, nowUtc, "book missing from /books batch response"));
            }
        }

        private static HydratedBook? HydratedFor(Dictionary<string, HydratedBook> books, string tokenId) =>
            !string.IsNullOrWhiteSpace(tokenId) && books.TryGetValue(tokenId, out var hydrated)
                ? hydrated
                : null;

        private static SportsScalpCandidateRow BuildCandidate(
            PolymarketEventSnapshot ev,
            PolymarketMarketSnapshot market,
            PolymarketFootballMarketFamily family,
            PolymarketOutcomeToken entry,
            PolymarketOutcomeToken hedge,
            HydratedBook? entryHydrated,
            HydratedBook? hedgeHydrated,
            decimal targetShares)
        {
            var entryBook = entryHydrated?.Book;
            var hedgeBook = hedgeHydrated?.Book;
            var entryFill = entryBook?.Buy(targetShares);
            var hedgeFill = hedgeBook?.Buy(targetShares);
            var entryAsk = entryBook?.BestAsk;
            var hedgeAsk = hedgeBook?.BestAsk;
            var entryVwap = entryFill?.IsComplete == true ? entryFill.Vwap : null;
            var hedgeVwap = hedgeFill?.IsComplete == true ? hedgeFill.Vwap : null;
            var targets = BuildTargets(entryVwap ?? entryAsk);
            var blockers = new List<string>();
            if (entryHydrated is null || entryHydrated.Top.RawBookStatus != TokenBookRawStatus.Ok)
                blockers.Add($"ENTRY_BOOK_{StatusLabel(entryHydrated?.Top.RawBookStatus ?? TokenBookRawStatus.MissingToken)}");
            if (hedgeHydrated is null || hedgeHydrated.Top.RawBookStatus != TokenBookRawStatus.Ok)
                blockers.Add($"HEDGE_BOOK_{StatusLabel(hedgeHydrated?.Top.RawBookStatus ?? TokenBookRawStatus.MissingToken)}");
            if (entryAsk is null)
                blockers.Add("NO_ENTRY_ASK");
            if (hedgeAsk is null)
                blockers.Add("NO_HEDGE_ASK");
            if (entryBook is not null && entryFill?.IsComplete != true)
                blockers.Add($"INSUFFICIENT_ENTRY_DEPTH_{targetShares:0}");
            if (hedgeBook is not null && hedgeFill?.IsComplete != true)
                blockers.Add($"INSUFFICIENT_HEDGE_DEPTH_{targetShares:0}");

            var pairCost = entryVwap.HasValue && hedgeVwap.HasValue ? entryVwap.Value + hedgeVwap.Value : (decimal?)null;
            var verdict = blockers.Count > 0
                ? SportsScalpVerdict.Blocked
                : pairCost < 1m
                    ? SportsScalpVerdict.TradeNow
                    : SportsScalpVerdict.Watch;

            return new SportsScalpCandidateRow
            {
                Verdict = verdict,
                EventId = ev.EventId ?? string.Empty,
                EventSlug = ev.Slug ?? string.Empty,
                EventTitle = ev.Title ?? string.Empty,
                EventPriority = Priority(ev),
                EventLive = ev.Live,
                StartTimeUtc = ev.StartTimeUtc,
                Score = ev.Score,
                Elapsed = ev.Elapsed,
                MarketId = market.MarketId ?? string.Empty,
                ConditionId = market.ConditionId ?? string.Empty,
                MarketQuestion = market.GroupItemTitle ?? market.Question ?? market.Slug ?? string.Empty,
                MarketFamily = family.ToString(),
                Line = market.Line,
                EntryOutcome = entry.Outcome,
                EntryTokenId = entry.TokenId,
                EntryAsk = entryAsk,
                EntryAskSize = entryBook?.AskSize,
                EntryVwap = entryVwap,
                EntryWorstAsk = entryFill?.WorstPrice,
                EntryFillableShares = entryFill?.FilledShares,
                EntryFetchedAtUtc = entryBook?.FetchedAtUtc,
                EntryBookStatus = StatusLabel(entryHydrated?.Top.RawBookStatus ?? TokenBookRawStatus.MissingToken),
                HedgeOutcome = hedge.Outcome,
                HedgeTokenId = hedge.TokenId,
                HedgeAskNow = hedgeAsk,
                HedgeAskSize = hedgeBook?.AskSize,
                HedgeVwapNow = hedgeVwap,
                HedgeWorstAskNow = hedgeFill?.WorstPrice,
                HedgeFillableShares = hedgeFill?.FilledShares,
                HedgeFetchedAtUtc = hedgeBook?.FetchedAtUtc,
                HedgeBookStatus = StatusLabel(hedgeHydrated?.Top.RawBookStatus ?? TokenBookRawStatus.MissingToken),
                TargetShares = targetShares,
                HedgeTargets = targets,
                SuggestedHedgeLadder = BuildDefaultLadder(entryVwap ?? entryAsk, targetShares),
                Trigger = TriggerFor(family, entry.Outcome),
                FailurePlan = FailurePlanFor(family, entry.Outcome),
                Blocker = blockers.Count == 0 ? string.Empty : string.Join(" | ", blockers.Distinct(StringComparer.OrdinalIgnoreCase)),
                SourceDetail = "Gamma keyset event + CLOB /books; WATCH_ONLY_PUBLIC_READS; no execution path"
            };
        }

        private static SportsScalpCandidateRow BlockedCandidate(PolymarketEventSnapshot ev, PolymarketMarketSnapshot market, PolymarketFootballMarketFamily family, decimal targetShares, string blocker) =>
            new()
            {
                Verdict = SportsScalpVerdict.Blocked,
                EventId = ev.EventId ?? string.Empty,
                EventSlug = ev.Slug ?? string.Empty,
                EventTitle = ev.Title ?? string.Empty,
                EventPriority = Priority(ev),
                EventLive = ev.Live,
                StartTimeUtc = ev.StartTimeUtc,
                Score = ev.Score,
                Elapsed = ev.Elapsed,
                MarketId = market.MarketId ?? string.Empty,
                ConditionId = market.ConditionId ?? string.Empty,
                MarketQuestion = market.GroupItemTitle ?? market.Question ?? market.Slug ?? string.Empty,
                MarketFamily = family.ToString(),
                Line = market.Line,
                TargetShares = targetShares,
                EntryBookStatus = "missing_token",
                HedgeBookStatus = "missing_token",
                Trigger = TriggerFor(family, string.Empty),
                FailurePlan = FailurePlanFor(family, string.Empty),
                Blocker = string.IsNullOrWhiteSpace(blocker) ? "MARKET_REJECTED" : blocker,
                SourceDetail = "Gamma event rejected before CLOB book fetch"
            };

        private static EventLiquidity BuildLiquidity(PolymarketEventSnapshot ev, IReadOnlyList<TokenBookTop> tops)
        {
            var gammaLiquidity = ev.LiquidityGamma ?? SumNullable(ev.Markets.Select(market => market.LiquidityGamma));
            var gammaVolume24h = ev.Volume24h ?? SumNullable(ev.Markets.Select(market => market.Volume24h));
            var okTops = tops.Where(top => top.RawBookStatus == TokenBookRawStatus.Ok).ToList();
            var clobTopDepth = SumNullable(okTops.Select(top => top.BestAsk.HasValue && top.BestAskSize.HasValue ? top.BestAsk.Value * top.BestAskSize.Value : (decimal?)null));
            var clobDepth2c = SumNullable(okTops.Select(top => top.DepthAsk2c));
            var clobDepth5c = SumNullable(okTops.Select(top => top.DepthAsk5c));
            var anyFailure = tops.Any(top => top.RawBookStatus is TokenBookRawStatus.FetchFailed or TokenBookRawStatus.EmptyBook or TokenBookRawStatus.Stale);

            if (clobDepth2c is > 0m)
            {
                return new EventLiquidity(
                    gammaLiquidity,
                    gammaVolume24h,
                    clobTopDepth,
                    clobDepth2c,
                    clobDepth5c,
                    "clob_depth",
                    anyFailure ? "partial" : "ok");
            }

            if (gammaLiquidity.HasValue || gammaVolume24h.HasValue)
            {
                return new EventLiquidity(
                    gammaLiquidity,
                    gammaVolume24h,
                    clobTopDepth,
                    clobDepth2c,
                    clobDepth5c,
                    "gamma",
                    gammaLiquidity is > 0m || gammaVolume24h is > 0m ? "partial" : "unknown");
            }

            return new EventLiquidity(gammaLiquidity, gammaVolume24h, clobTopDepth, clobDepth2c, clobDepth5c, "unknown", "unknown");
        }

        private static decimal? SumNullable(IEnumerable<decimal?> values)
        {
            var materialized = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
            return materialized.Count == 0 ? null : materialized.Sum();
        }

        private static decimal EventVolume24h(PolymarketEventSnapshot ev) =>
            ev.Volume24h ?? SumNullable(ev.Markets.Select(market => market.Volume24h)) ?? 0m;

        private static void Increment(Dictionary<string, int> counts, string endpoint) =>
            counts[endpoint] = counts.TryGetValue(endpoint, out var count) ? count + 1 : 1;

        private static string StatusLabel(TokenBookRawStatus status) => status switch
        {
            TokenBookRawStatus.Ok => "ok",
            TokenBookRawStatus.MissingToken => "missing_token",
            TokenBookRawStatus.FetchFailed => "fetch_failed",
            TokenBookRawStatus.EmptyBook => "empty_book",
            TokenBookRawStatus.Stale => "stale",
            _ => status.ToString().ToLowerInvariant()
        };

        private static bool IsSupportedScalpFamily(PolymarketFootballMarketFamily family) => family is
            PolymarketFootballMarketFamily.MatchTotal or
            PolymarketFootballMarketFamily.BothTeamsToScore or
            PolymarketFootballMarketFamily.Spread or
            PolymarketFootballMarketFamily.TeamTotal or
            PolymarketFootballMarketFamily.Moneyline;

        private static string TriggerFor(PolymarketFootballMarketFamily family, string outcome) => family switch
        {
            PolymarketFootballMarketFamily.MatchTotal when LooksLikeUnder(outcome) => "Quiet 0-0 first 10-15m; hedge opposite Over at target grid.",
            PolymarketFootballMarketFamily.MatchTotal => "Needs goal/event or over repricing; do not treat as pure decay.",
            PolymarketFootballMarketFamily.BothTeamsToScore when LooksLikeNo(outcome) => "Scoreless/dead start or one-sided favorite lead; hedge BTTS Yes only at target.",
            PolymarketFootballMarketFamily.Spread => "Anti-blowout clock decay or favorite event path depending side; verify pressure.",
            PolymarketFootballMarketFamily.TeamTotal => "Team-specific tempo/lineup path required; use only with live pressure context.",
            _ => "Needs explicit opposite-side repricing path before entry."
        };

        private static string FailurePlanFor(PolymarketFootballMarketFamily family, string outcome) => family switch
        {
            PolymarketFootballMarketFamily.MatchTotal when LooksLikeUnder(outcome) => "Early goal: do not panic hedge loss; reassess tempo, wait for overreaction fade, cut only if pressure remains toxic.",
            PolymarketFootballMarketFamily.BothTeamsToScore when LooksLikeNo(outcome) => "Both teams threaten early: reduce expectation of decay; partial hedge if target nearly available, otherwise wait/cut.",
            PolymarketFootballMarketFamily.Spread => "Favorite early goal or red card: avoid blind loss-lock; decide between smaller cut and waiting for stabilization.",
            _ => "If bad event breaks hedge path, mark BLOCKED and avoid converting into directional exposure without explicit approval."
        };

        private static bool LooksLikeUnder(string outcome) => outcome.Contains("under", StringComparison.OrdinalIgnoreCase) || outcome.Equals("No", StringComparison.OrdinalIgnoreCase);
        private static bool LooksLikeNo(string outcome) => outcome.Equals("No", StringComparison.OrdinalIgnoreCase) || outcome.Contains("No", StringComparison.OrdinalIgnoreCase);

        private static void AddDistinctEvents(
            List<PolymarketEventSnapshot> target,
            HashSet<string> seen,
            IEnumerable<PolymarketEventSnapshot> events)
        {
            foreach (var ev in events)
            {
                var key = !string.IsNullOrWhiteSpace(ev.EventId) ? $"id:{ev.EventId}" : $"slug:{ev.Slug}";
                if (seen.Add(key))
                    target.Add(ev);
            }
        }

        private static string Priority(PolymarketEventSnapshot ev)
        {
            var text = $"{ev.Slug} {ev.Title}".ToLowerInvariant();
            return text.Contains("fifwc") || text.Contains("world cup") ? "WORLD_CUP" : "SPORTS";
        }

        private sealed record HydratedBook(PolymarketOrderBookSnapshot? Book, TokenBookTop Top);

        private sealed record EventLiquidity(
            decimal? GammaLiquidityUsd,
            decimal? GammaVolume24hUsd,
            decimal? ClobTopDepthUsd,
            decimal? ClobDepth2cUsd,
            decimal? ClobDepth5cUsd,
            string LiquiditySource,
            string LiquidityQuality);
    }
}
