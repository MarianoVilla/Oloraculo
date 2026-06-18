namespace Oloraculo.Web.ComboLab.Markets
{
    public static class PolymarketApiEndpoints
    {
        public const string GammaEventsKeyset = "/events/keyset";
        public const string GammaMarketsKeyset = "/markets/keyset";
        public const string GammaEventBySlugPrefix = "/events/slug/";
        public const string GammaSportsMarketTypes = "/sports/market-types";
        public const string ClobBook = "/book";
        public const string ClobBooks = "/books";
        public const string DataPositions = "/positions";
        public const string ComboMarkets = "/v1/rfq/combo-markets";
    }

    public sealed record PolymarketMarketDataQuality
    {
        public bool MissingTokenIds { get; init; }
        public bool LiquidityMissing { get; init; }
        public bool LiquidityZeroConfirmed { get; init; }
        public IReadOnlyList<string> Flags { get; init; } = [];
    }

    public enum PolymarketRejectReason
    {
        MissingConditionId,
        MissingTokenId,
        ClosedOrNotAccepting,
        NoOrderBook,
        NoBid,
        NoAsk,
        MissingTickSize,
        MissingMinOrderSize,
        InvalidBookPrice,
        InvalidBookSize,
        UnclassifiedRules
    }

    public sealed record PolymarketOutcomeToken(
        string Outcome,
        string TokenId,
        decimal? OutcomePrice);

    public sealed record PolymarketSportsMarketType(
        string Slug,
        string? Name = null,
        string? SportSlug = null);

    public sealed record PolymarketMarketSnapshot
    {
        public string? MarketId { get; init; }
        public string? Slug { get; init; }
        public string? Question { get; init; }
        public string? GroupItemTitle { get; init; }
        public string? Description { get; init; }
        public string? ConditionId { get; init; }
        public bool Active { get; init; }
        public bool Closed { get; init; }
        public bool Archived { get; init; }
        public bool? AcceptingOrders { get; init; }
        public bool EnableOrderBook { get; init; }
        public decimal? OrderMinSize { get; init; }
        public decimal? OrderPriceMinTickSize { get; init; }
        public string? Category { get; init; }
        public string? MarketType { get; init; }
        public string? SportsMarketType { get; init; }
        public decimal? Line { get; init; }
        public string? GameStartTime { get; init; }
        public DateTimeOffset? StartTimeUtc { get; init; }
        public DateTimeOffset? EndTimeUtc { get; init; }
        public decimal? Volume { get; init; }
        public decimal? Volume24h { get; init; }
        public decimal? LiquidityGamma { get; init; }
        public IReadOnlyList<string> Outcomes { get; init; } = [];
        public IReadOnlyList<string> ClobTokenIds { get; init; } = [];
        public IReadOnlyList<PolymarketOutcomeToken> Tokens { get; init; } = [];
        public IReadOnlyList<PolymarketRejectReason> RejectReasons { get; init; } = [];
        public PolymarketMarketDataQuality DataQuality { get; init; } = new();
        public object? Raw { get; init; }
    }

    public sealed record PolymarketEventSnapshot
    {
        public string? EventId { get; init; }
        public string? Slug { get; init; }
        public string? Title { get; init; }
        public bool Active { get; init; }
        public bool Closed { get; init; }
        public bool Archived { get; init; }
        public bool Live { get; init; }
        public bool Ended { get; init; }
        public string? Score { get; init; }
        public string? Elapsed { get; init; }
        public DateTimeOffset? StartTimeUtc { get; init; }
        public DateTimeOffset? EndTimeUtc { get; init; }
        public decimal? Volume { get; init; }
        public decimal? Volume24h { get; init; }
        public decimal? LiquidityGamma { get; init; }
        public DateTimeOffset FetchedAtUtc { get; init; }
        public string? RawPayloadHash { get; init; }
        public IReadOnlyList<PolymarketMarketSnapshot> Markets { get; init; } = [];
    }

    public sealed record PolymarketEventPage(
        IReadOnlyList<PolymarketEventSnapshot> Events,
        string? NextCursor);

    public sealed record PolymarketMarketPage(
        IReadOnlyList<PolymarketMarketSnapshot> Markets,
        string? NextCursor);

    public sealed record PolymarketBookLevel(decimal Price, decimal Size);

    public sealed record PolymarketDepthFill(
        decimal RequestedShares,
        decimal FilledShares,
        decimal Cost,
        decimal? Vwap,
        decimal? WorstPrice,
        IReadOnlyList<PolymarketBookLevel> ConsumedLevels)
    {
        public bool IsComplete => FilledShares >= RequestedShares;
    }

    public sealed record PolymarketOrderBookSnapshot
    {
        public string TokenId { get; init; } = string.Empty;
        public string? ConditionId { get; init; }
        public decimal? BestBid { get; init; }
        public decimal? BidSize { get; init; }
        public decimal? BestAsk { get; init; }
        public decimal? AskSize { get; init; }
        public decimal? MinOrderSize { get; init; }
        public decimal? TickSize { get; init; }
        public bool NegRisk { get; init; }
        public decimal? LastTradePrice { get; init; }
        public DateTimeOffset FetchedAtUtc { get; init; }
        public string? RawPayloadHash { get; init; }
        public IReadOnlyList<PolymarketBookLevel> Bids { get; init; } = [];
        public IReadOnlyList<PolymarketBookLevel> Asks { get; init; } = [];
        public IReadOnlyList<PolymarketRejectReason> RejectReasons { get; init; } = [];

        public PolymarketDepthFill Buy(decimal shares) => Consume(Asks, shares, maxPrice: null);

        public PolymarketDepthFill Sell(decimal shares) => Consume(Bids, shares, maxPrice: null);

        public PolymarketDepthFill BuyUpTo(decimal shares, decimal maxPrice) => Consume(Asks.Where(level => level.Price <= maxPrice), shares, maxPrice);

        private static PolymarketDepthFill Consume(IEnumerable<PolymarketBookLevel> sourceLevels, decimal requestedShares, decimal? maxPrice)
        {
            if (requestedShares <= 0)
                return new PolymarketDepthFill(requestedShares, 0, 0, null, null, []);

            var remaining = requestedShares;
            var filled = 0m;
            var cost = 0m;
            var consumed = new List<PolymarketBookLevel>();
            decimal? worst = null;

            foreach (var level in sourceLevels)
            {
                if (maxPrice.HasValue && level.Price > maxPrice.Value)
                    continue;
                if (level.Size <= 0 || level.Price is < 0 or > 1)
                    continue;

                var take = Math.Min(remaining, level.Size);
                if (take <= 0)
                    continue;

                filled += take;
                cost += take * level.Price;
                worst = level.Price;
                consumed.Add(new PolymarketBookLevel(level.Price, take));
                remaining -= take;
                if (remaining <= 0)
                    break;
            }

            var vwap = filled > 0 ? cost / filled : (decimal?)null;
            return new PolymarketDepthFill(requestedShares, filled, cost, vwap, worst, consumed);
        }
    }

    public enum TokenBookRawStatus
    {
        Ok,
        MissingToken,
        FetchFailed,
        EmptyBook,
        Stale
    }

    public sealed record TokenBookTop
    {
        public string TokenId { get; init; } = string.Empty;
        public decimal? BestBid { get; init; }
        public decimal? BestBidSize { get; init; }
        public decimal? BestAsk { get; init; }
        public decimal? BestAskSize { get; init; }
        public decimal? Mid { get; init; }
        public decimal? Spread { get; init; }
        public decimal? LastTradePrice { get; init; }
        public decimal? DepthAsk1c { get; init; }
        public decimal? DepthAsk2c { get; init; }
        public decimal? DepthAsk5c { get; init; }
        public decimal? DepthBid1c { get; init; }
        public decimal? DepthBid2c { get; init; }
        public decimal? DepthBid5c { get; init; }
        public TokenBookRawStatus RawBookStatus { get; init; }
        public DateTimeOffset LastUpdatedUtc { get; init; }
        public string? Error { get; init; }

        public static TokenBookTop MissingToken(DateTimeOffset nowUtc) => new()
        {
            RawBookStatus = TokenBookRawStatus.MissingToken,
            LastUpdatedUtc = nowUtc
        };

        public static TokenBookTop FetchFailed(string tokenId, DateTimeOffset nowUtc, string error) => new()
        {
            TokenId = tokenId,
            RawBookStatus = TokenBookRawStatus.FetchFailed,
            LastUpdatedUtc = nowUtc,
            Error = error
        };

        public static TokenBookTop FromBook(PolymarketOrderBookSnapshot book)
        {
            ArgumentNullException.ThrowIfNull(book);
            var empty = !book.BestBid.HasValue || !book.BestAsk.HasValue;
            var mid = book.BestBid.HasValue && book.BestAsk.HasValue
                ? Math.Round((book.BestBid.Value + book.BestAsk.Value) / 2m, 6, MidpointRounding.AwayFromZero)
                : (decimal?)null;
            var spread = book.BestBid.HasValue && book.BestAsk.HasValue
                ? Math.Round(book.BestAsk.Value - book.BestBid.Value, 6, MidpointRounding.AwayFromZero)
                : (decimal?)null;

            return new TokenBookTop
            {
                TokenId = book.TokenId,
                BestBid = book.BestBid,
                BestBidSize = book.BidSize,
                BestAsk = book.BestAsk,
                BestAskSize = book.AskSize,
                Mid = mid,
                Spread = spread,
                LastTradePrice = book.LastTradePrice,
                DepthAsk1c = DepthAsks(book.Asks, book.BestAsk, .01m),
                DepthAsk2c = DepthAsks(book.Asks, book.BestAsk, .02m),
                DepthAsk5c = DepthAsks(book.Asks, book.BestAsk, .05m),
                DepthBid1c = DepthBids(book.Bids, book.BestBid, .01m),
                DepthBid2c = DepthBids(book.Bids, book.BestBid, .02m),
                DepthBid5c = DepthBids(book.Bids, book.BestBid, .05m),
                RawBookStatus = empty ? TokenBookRawStatus.EmptyBook : TokenBookRawStatus.Ok,
                LastUpdatedUtc = book.FetchedAtUtc
            };
        }

        private static decimal? DepthAsks(IReadOnlyList<PolymarketBookLevel> asks, decimal? bestAsk, decimal tolerance)
        {
            if (!bestAsk.HasValue || asks.Count == 0)
                return null;

            var maxPrice = bestAsk.Value + tolerance;
            return asks
                .Where(level => level.Price >= bestAsk.Value && level.Price <= maxPrice)
                .Sum(level => level.Price * level.Size);
        }

        private static decimal? DepthBids(IReadOnlyList<PolymarketBookLevel> bids, decimal? bestBid, decimal tolerance)
        {
            if (!bestBid.HasValue || bids.Count == 0)
                return null;

            var minPrice = bestBid.Value - tolerance;
            return bids
                .Where(level => level.Price <= bestBid.Value && level.Price > minPrice)
                .Sum(level => level.Price * level.Size);
        }
    }

    public sealed record PolymarketComboMarketSnapshot
    {
        public string? ComboMarketId { get; init; }
        public string? Slug { get; init; }
        public string? Question { get; init; }
        public string? ConditionId { get; init; }
        public string? SportsMarketType { get; init; }
        public decimal? Line { get; init; }
        public IReadOnlyList<decimal?> Prices { get; init; } = [];
        public IReadOnlyList<PolymarketOutcomeToken> Tokens { get; init; } = [];
    }

    public sealed record PolymarketComboMarketPage(
        IReadOnlyList<PolymarketComboMarketSnapshot> Markets,
        string? NextCursor);

    public sealed record PolymarketPositionSnapshot
    {
        public string? ProxyWallet { get; init; }
        public string? Asset { get; init; }
        public string? ConditionId { get; init; }
        public decimal? Size { get; init; }
        public decimal? AveragePrice { get; init; }
        public decimal? InitialValue { get; init; }
        public decimal? CurrentValue { get; init; }
        public decimal? CashPnl { get; init; }
        public decimal? PercentPnl { get; init; }
        public decimal? TotalBought { get; init; }
        public decimal? RealizedPnl { get; init; }
        public decimal? PercentRealizedPnl { get; init; }
        public decimal? CurrentPrice { get; init; }
        public bool Redeemable { get; init; }
        public bool Mergeable { get; init; }
        public string? Title { get; init; }
        public string? Slug { get; init; }
        public string? Icon { get; init; }
        public string? EventSlug { get; init; }
        public string? Outcome { get; init; }
        public int? OutcomeIndex { get; init; }
        public string? OppositeOutcome { get; init; }
        public string? OppositeAsset { get; init; }
        public string? EndDate { get; init; }
        public bool NegativeRisk { get; init; }
    }
}
