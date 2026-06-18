using Microsoft.Extensions.Options;
using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.ComboLab.Scalp;

namespace Oloraculo.Web.Feeds.Adapters
{
    public sealed class PolymarketClobFeedStatusAdapter : IFeedStatusAdapter
    {
        private readonly PolymarketMarketDataService _markets;
        private readonly OloraculoConfig _config;

        public PolymarketClobFeedStatusAdapter(PolymarketMarketDataService markets, IOptions<OloraculoConfig> options)
        {
            _markets = markets;
            _config = options.Value;
        }

        public string SourceId => "polymarket_clob";

        public FeedAdapterReport Probe(FeedStatusProbeContext context)
        {
            if (!context.AllowNetwork)
            {
                return FeedStatusAdapterReports.Report(
                    SourceId,
                    FeedAdapterState.Planned,
                    configPresent: true,
                    authPresent: false,
                    detail: "public CLOB probe is cache-backed for inline status reads",
                    blockers: ["LIVE_COLLECTOR_PENDING"]);
            }

            var tokenIds = _config.PolymarketClobStatusTokenIds
                .Where(tokenId => !string.IsNullOrWhiteSpace(tokenId))
                .Select(tokenId => tokenId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (tokenIds.Length == 0)
                return PolymarketClobStatusEvaluator.Evaluate([], context.AsOfUtc, context.StaleAfter, _config.PolymarketClobStatusMinimumDepthUsd);

            try
            {
                var books = _markets.FetchBooksAsync(tokenIds).GetAwaiter().GetResult();
                var samples = books
                    .Select(book => Sample(book, context.AsOfUtc))
                    .ToArray();
                return PolymarketClobStatusEvaluator.Evaluate(
                    samples,
                    context.AsOfUtc,
                    context.StaleAfter,
                    _config.PolymarketClobStatusMinimumDepthUsd);
            }
            catch (Exception ex)
            {
                return PolymarketClobStatusEvaluator.FetchFailure(context.AsOfUtc, ex.Message);
            }
        }

        private static PolymarketClobStatusSample Sample(PolymarketOrderBookSnapshot book, DateTimeOffset receivedAtUtc)
        {
            var bestBid = book.Bids.OrderByDescending(level => level.Price).FirstOrDefault();
            var bestAsk = book.Asks.OrderBy(level => level.Price).FirstOrDefault();
            var totalAskDepthNearTarget = bestAsk is null
                ? 0m
                : book.Asks
                    .Where(level => level.Price >= bestAsk.Price && level.Price <= bestAsk.Price + .02m)
                    .Sum(level => level.Price * level.Size);
            var totalBidDepthNearTarget = bestBid is null
                ? 0m
                : book.Bids
                    .Where(level => level.Price <= bestBid.Price && level.Price >= bestBid.Price - .02m)
                    .Sum(level => level.Price * level.Size);

            return new PolymarketClobStatusSample(
                book.TokenId,
                receivedAtUtc,
                book.Bids,
                book.Asks,
                book.BestBid,
                book.BestAsk,
                totalAskDepthNearTarget,
                totalBidDepthNearTarget,
                HasBook: book.Bids.Count > 0 || book.Asks.Count > 0);
        }
    }
}
