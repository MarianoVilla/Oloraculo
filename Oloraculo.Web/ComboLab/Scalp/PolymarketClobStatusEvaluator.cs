using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.Feeds;

namespace Oloraculo.Web.ComboLab.Scalp
{
    public sealed record PolymarketClobStatusSample(
        string TokenId,
        DateTimeOffset ReceivedTsUtc,
        IReadOnlyList<PolymarketBookLevel> BidLevels,
        IReadOnlyList<PolymarketBookLevel> AskLevels,
        decimal? BestBid,
        decimal? BestAsk,
        decimal TotalAskDepthNearTarget,
        decimal TotalBidDepthNearTarget,
        bool HasBook);

    public static class PolymarketClobStatusEvaluator
    {
        private const string SourceId = "polymarket_clob";

        public static FeedAdapterReport FetchFailure(DateTimeOffset asOfUtc, string error) =>
            Report(
                FeedAdapterState.Down,
                asOfUtc,
                rows: null,
                detail: "public CLOB book probe failed",
                lastError: error,
                blockers: ["CLOB_FETCH_FAILED"]);

        public static FeedAdapterReport Evaluate(
            IReadOnlyList<PolymarketClobStatusSample> samples,
            DateTimeOffset asOfUtc,
            TimeSpan staleAfter,
            decimal minimumDepthUsd = 1m)
        {
            if (samples.Count == 0)
            {
                return Report(
                    FeedAdapterState.Empty,
                    latestRecvTsUtc: null,
                    rows: 0,
                    detail: "no CLOB token ids are configured for status sampling",
                    blockers: ["NO_CLOB_TOKENS"]);
            }

            var latest = samples.Max(sample => sample.ReceivedTsUtc);
            if ((asOfUtc - latest) > staleAfter)
            {
                return Report(
                    FeedAdapterState.Stale,
                    latest,
                    samples.Count,
                    detail: "latest sampled CLOB book is stale",
                    blockers: ["STALE_CLOB"]);
            }

            var sample = samples
                .OrderByDescending(item => item.ReceivedTsUtc)
                .First();
            if (!sample.HasBook)
            {
                return Report(
                    FeedAdapterState.Empty,
                    latest,
                    samples.Count,
                    detail: "sampled CLOB token has no order book",
                    blockers: ["NO_ORDER_BOOK"]);
            }

            if (sample.BidLevels.Count == 0 || !sample.BestBid.HasValue)
            {
                return Report(
                    FeedAdapterState.Blocked,
                    latest,
                    samples.Count,
                    detail: "sampled CLOB book has no bid side",
                    blockers: ["NO_BID"]);
            }

            if (sample.AskLevels.Count == 0 || !sample.BestAsk.HasValue)
            {
                return Report(
                    FeedAdapterState.Blocked,
                    latest,
                    samples.Count,
                    detail: "sampled CLOB book has no ask side",
                    blockers: ["NO_ASK"]);
            }

            if (sample.BestBid.Value >= sample.BestAsk.Value)
            {
                return Report(
                    FeedAdapterState.Blocked,
                    latest,
                    samples.Count,
                    detail: "sampled CLOB book is crossed or locked",
                    blockers: ["CROSSED_BOOK"]);
            }

            if (sample.TotalAskDepthNearTarget < minimumDepthUsd || sample.TotalBidDepthNearTarget < minimumDepthUsd)
            {
                return Report(
                    FeedAdapterState.Blocked,
                    latest,
                    samples.Count,
                    detail: "sampled CLOB book has insufficient near-touch depth",
                    blockers: ["INSUFFICIENT_DEPTH"]);
            }

            return Report(
                FeedAdapterState.Ready,
                latest,
                samples.Count,
                detail: $"fresh public CLOB books sampled: {samples.Count}");
        }

        private static FeedAdapterReport Report(
            FeedAdapterState state,
            DateTimeOffset? latestRecvTsUtc,
            int? rows,
            string detail,
            string? lastError = null,
            IReadOnlyList<string>? blockers = null)
        {
            var definition = FeedStatusSourceCatalog.All.First(source => source.SourceId == SourceId);
            return new FeedAdapterReport(
                SourceId,
                definition.Source,
                definition.Role,
                state,
                ConfigPresent: true,
                AuthPresent: false,
                LatestRecvTsUtc: latestRecvTsUtc,
                RowsLastMinute: rows,
                LastError: FeedStatusRedactor.RedactError(lastError),
                Detail: detail,
                SecretPolicy: definition.SecretPolicy,
                Blockers: blockers);
        }
    }
}
