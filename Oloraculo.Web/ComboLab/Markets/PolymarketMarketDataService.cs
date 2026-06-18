using Microsoft.Extensions.Options;
using Oloraculo.Web.Helpers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Oloraculo.Web.ComboLab.Markets
{
    public sealed class PolymarketMarketDataService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private const int GammaEventsKeysetMaxLimit = 500;
        private const int GammaMarketsKeysetMaxLimit = 100;
        private readonly HttpClient _http;
        private readonly OloraculoConfig _config;

        public PolymarketMarketDataService(HttpClient http, IOptions<OloraculoConfig> options)
        {
            _http = http;
            _config = options.Value;
        }

        public async Task<PolymarketEventSnapshot> FetchEventBySlugAsync(string slug, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("Event slug is required.", nameof(slug));

            var url = $"{TrimSlash(_config.PolymarketGammaBaseUrl)}{PolymarketApiEndpoints.GammaEventBySlugPrefix}{Uri.EscapeDataString(slug)}?include_chat=false&include_template=false";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            return ParseEvent(doc.RootElement, DateTimeOffset.UtcNow, CryptoUtil.GetSha256(json));
        }

        public async Task<PolymarketOrderBookSnapshot> FetchBookAsync(string tokenId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tokenId))
                throw new ArgumentException("Token id is required.", nameof(tokenId));

            var url = $"{TrimSlash(_config.PolymarketClobBaseUrl)}{PolymarketApiEndpoints.ClobBook}?token_id={Uri.EscapeDataString(tokenId)}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            return ParseBook(tokenId, doc.RootElement, DateTimeOffset.UtcNow, CryptoUtil.GetSha256(json));
        }

        public async Task<IReadOnlyList<PolymarketOrderBookSnapshot>> FetchBooksAsync(IEnumerable<string> tokenIds, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(tokenIds);

            var requestedTokenIds = tokenIds
                .Where(tokenId => !string.IsNullOrWhiteSpace(tokenId))
                .Select(tokenId => tokenId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (requestedTokenIds.Count == 0)
                return [];

            var payload = requestedTokenIds
                .Select(tokenId => new Dictionary<string, string> { ["token_id"] = tokenId })
                .ToList();
            var url = $"{TrimSlash(_config.PolymarketClobBaseUrl)}{PolymarketApiEndpoints.ClobBooks}";
            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(url, content, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var array = RootArrayOrProperty(doc.RootElement, "data", "books");
            if (array.ValueKind != JsonValueKind.Array)
                return [];

            var nowUtc = DateTimeOffset.UtcNow;
            var rawPayloadHash = CryptoUtil.GetSha256(json);
            var books = new List<PolymarketOrderBookSnapshot>();
            var index = 0;
            foreach (var item in array.EnumerateArray())
            {
                var tokenId = StringPropertyAny(item, "asset_id", "token_id", "tokenId") ??
                    (index < requestedTokenIds.Count ? requestedTokenIds[index] : string.Empty);
                books.Add(ParseBook(tokenId, item, nowUtc, rawPayloadHash));
                index++;
            }

            return books;
        }

        public async Task<TokenBookTop> FetchTokenBookTopAsync(string tokenId, CancellationToken ct = default)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(tokenId))
                return TokenBookTop.MissingToken(nowUtc);

            try
            {
                var book = await FetchBookAsync(tokenId, ct);
                return TokenBookTop.FromBook(book);
            }
            catch (Exception ex)
            {
                return TokenBookTop.FetchFailed(tokenId, nowUtc, ex.Message);
            }
        }

        public async Task<IReadOnlyList<PolymarketSportsMarketType>> FetchSportsMarketTypesAsync(CancellationToken ct = default)
        {
            var url = $"{TrimSlash(_config.PolymarketGammaBaseUrl)}{PolymarketApiEndpoints.GammaSportsMarketTypes}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            return ParseSportsMarketTypes(doc.RootElement);
        }

        public async Task<IReadOnlyList<PolymarketEventSnapshot>> FetchWorldCupEventsAsync(int limit = 100, CancellationToken ct = default)
        {
            return await FetchWorldCupEventsPagedAsync(limit: limit, ct: ct);
        }

        public async Task<IReadOnlyList<PolymarketEventSnapshot>> FetchWorldCupEventsPagedAsync(int maxPages = 10, int limit = 100, CancellationToken ct = default)
        {
            if (maxPages <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPages), "Max pages must be positive.");
            ValidateEventKeysetLimit(limit);

            var events = new List<PolymarketEventSnapshot>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? cursor = null;
            for (var pageIndex = 0; pageIndex < maxPages; pageIndex++)
            {
                var page = await FetchEventsPageAsync(
                    limit: limit,
                    cursor: cursor,
                    seriesId: PolymarketWorldCupMarketSurface.SeriesId,
                    includeChildren: true,
                    includeBestLines: true,
                    active: true,
                    closed: false,
                    ct: ct);
                foreach (var ev in page.Events)
                {
                    var key = !string.IsNullOrWhiteSpace(ev.EventId)
                        ? $"id:{ev.EventId}"
                        : $"slug:{ev.Slug}";
                    if (seen.Add(key))
                        events.Add(ev);
                }

                if (string.IsNullOrWhiteSpace(page.NextCursor))
                    break;
                cursor = page.NextCursor;
            }

            return events;
        }

        public async Task<IReadOnlyList<PolymarketEventSnapshot>> FetchEventsAsync(
            int limit = 100,
            int? tagId = null,
            bool relatedTags = true,
            bool closed = false,
            int offset = 0,
            bool active = true,
            CancellationToken ct = default)
        {
            ValidateEventKeysetLimit(limit);
            if (offset != 0)
                throw new ArgumentOutOfRangeException(nameof(offset), "Use FetchEventsPageAsync with cursor-based pagination.");

            var page = await FetchEventsPageAsync(
                limit: limit,
                cursor: null,
                tagId: tagId,
                relatedTags: relatedTags,
                closed: closed,
                active: active,
                ct: ct);
            return page.Events;
        }

        public async Task<PolymarketEventPage> FetchEventsPageAsync(
            int limit = 100,
            string? cursor = null,
            int? tagId = null,
            bool relatedTags = true,
            int? seriesId = null,
            bool? includeChildren = null,
            bool? includeBestLines = null,
            bool closed = false,
            bool? active = true,
            CancellationToken ct = default)
        {
            ValidateEventKeysetLimit(limit);

            var query = new List<string>();
            query.Add($"closed={closed.ToString().ToLowerInvariant()}");
            if (tagId.HasValue)
            {
                query.Add($"tag_id={tagId.Value.ToString(CultureInfo.InvariantCulture)}");
                query.Add($"related_tags={relatedTags.ToString().ToLowerInvariant()}");
            }
            if (seriesId.HasValue)
                query.Add($"series_id={seriesId.Value.ToString(CultureInfo.InvariantCulture)}");
            if (includeChildren.HasValue)
                query.Add($"include_children={includeChildren.Value.ToString().ToLowerInvariant()}");
            if (includeBestLines.HasValue)
                query.Add($"include_best_lines={includeBestLines.Value.ToString().ToLowerInvariant()}");

            query.Add($"limit={limit.ToString(CultureInfo.InvariantCulture)}");
            if (!string.IsNullOrWhiteSpace(cursor))
                query.Add($"after_cursor={Uri.EscapeDataString(cursor)}");

            var url = $"{TrimSlash(_config.PolymarketGammaBaseUrl)}{PolymarketApiEndpoints.GammaEventsKeyset}?{string.Join('&', query)}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var page = ParseEventPage(doc.RootElement, DateTimeOffset.UtcNow, CryptoUtil.GetSha256(json));
            if (!active.HasValue)
                return page;

            return page with
            {
                Events = page.Events.Where(ev => ev.Active == active.Value).ToList()
            };
        }

        public async Task<IReadOnlyList<PolymarketMarketSnapshot>> FetchWorldCupMarketsByTypeAsync(string sportsMarketType, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sportsMarketType))
                throw new ArgumentException("Sports market type is required.", nameof(sportsMarketType));

            var markets = new List<PolymarketMarketSnapshot>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? cursor = null;
            for (var pageIndex = 0; pageIndex < 10; pageIndex++)
            {
                var page = await FetchWorldCupMarketsByTypePageAsync(sportsMarketType, cursor: cursor, ct: ct);
                foreach (var market in page.Markets)
                {
                    var key = MarketDedupKey(market);
                    if (seen.Add(key))
                        markets.Add(market);
                }

                if (string.IsNullOrWhiteSpace(page.NextCursor))
                    break;
                cursor = page.NextCursor;
            }

            return markets;
        }

        public async Task<PolymarketMarketPage> FetchWorldCupMarketsByTypePageAsync(string sportsMarketType, int limit = 100, string? cursor = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sportsMarketType))
                throw new ArgumentException("Sports market type is required.", nameof(sportsMarketType));
            ValidateMarketKeysetLimit(limit);

            var query = new List<string>
            {
                "closed=false",
                $"tag_id={PolymarketWorldCupMarketSurface.TagId}",
                "related_tags=true",
                $"sports_market_types={Uri.EscapeDataString(sportsMarketType)}",
                $"limit={limit.ToString(CultureInfo.InvariantCulture)}"
            };
            if (!string.IsNullOrWhiteSpace(cursor))
                query.Add($"after_cursor={Uri.EscapeDataString(cursor)}");

            var url = $"{TrimSlash(_config.PolymarketGammaBaseUrl)}{PolymarketApiEndpoints.GammaMarketsKeyset}?{string.Join('&', query)}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            return ParseMarketPage(doc.RootElement);
        }

        public async Task<IReadOnlyList<PolymarketMarketSnapshot>> FetchWorldCupMarketsByTypesAsync(IEnumerable<string> sportsMarketTypes, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(sportsMarketTypes);
            var markets = new List<PolymarketMarketSnapshot>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sportsMarketType in sportsMarketTypes.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var batch = await FetchWorldCupMarketsByTypeAsync(sportsMarketType, ct);
                foreach (var market in batch)
                {
                    var key = MarketDedupKey(market);
                    if (seen.Add(key))
                        markets.Add(market);
                }
            }

            return markets;
        }

        public async Task<PolymarketComboMarketPage> FetchComboMarketsPageAsync(int limit = 100, string? cursor = null, CancellationToken ct = default)
        {
            if (limit <= 0)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");

            var url = $"{TrimSlash(_config.PolymarketComboRfqBaseUrl)}{PolymarketApiEndpoints.ComboMarkets}?limit={limit.ToString(CultureInfo.InvariantCulture)}";
            if (!string.IsNullOrWhiteSpace(cursor))
                url += $"&cursor={Uri.EscapeDataString(cursor)}";

            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            return ParseComboMarketPage(doc.RootElement);
        }

        public async Task<IReadOnlyList<PolymarketComboMarketSnapshot>> FetchComboMarketsAsync(int maxPages = 10, int limit = 100, CancellationToken ct = default)
        {
            if (maxPages <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPages), "Max pages must be positive.");

            var markets = new List<PolymarketComboMarketSnapshot>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? cursor = null;
            for (var pageIndex = 0; pageIndex < maxPages; pageIndex++)
            {
                var page = await FetchComboMarketsPageAsync(limit, cursor, ct);
                foreach (var market in page.Markets)
                {
                    var key = !string.IsNullOrWhiteSpace(market.ConditionId)
                        ? $"condition:{market.ConditionId}"
                        : !string.IsNullOrWhiteSpace(market.ComboMarketId)
                            ? $"combo:{market.ComboMarketId}"
                            : $"slug:{market.Slug}";
                    if (seen.Add(key))
                        markets.Add(market);
                }

                if (string.IsNullOrWhiteSpace(page.NextCursor))
                    break;
                cursor = page.NextCursor;
            }

            return markets;
        }

        public async Task<IReadOnlyList<PolymarketPositionSnapshot>> FetchCurrentPositionsAsync(
            string user,
            int limit = 100,
            int offset = 0,
            decimal sizeThreshold = 1m,
            CancellationToken ct = default)
        {
            if (!IsHexAddress(user))
                throw new ArgumentException("User must be a 0x-prefixed 40-hex address.", nameof(user));
            if (limit is < 0 or > 500)
                throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 0 and 500.");
            if (offset is < 0 or > 10000)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be between 0 and 10000.");
            if (sizeThreshold < 0)
                throw new ArgumentOutOfRangeException(nameof(sizeThreshold), "Size threshold cannot be negative.");

            var query = new List<string>
            {
                $"user={Uri.EscapeDataString(user)}",
                $"sizeThreshold={sizeThreshold.ToString(CultureInfo.InvariantCulture)}",
                $"limit={limit.ToString(CultureInfo.InvariantCulture)}",
                $"offset={offset.ToString(CultureInfo.InvariantCulture)}"
            };
            var url = $"{TrimSlash(_config.PolymarketDataBaseUrl)}{PolymarketApiEndpoints.DataPositions}?{string.Join('&', query)}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            return ParsePositions(doc.RootElement);
        }

        public static PolymarketEventSnapshot ParseEvent(JsonElement root, DateTimeOffset fetchedAtUtc, string? rawPayloadHash = null)
        {
            var markets = new List<PolymarketMarketSnapshot>();
            if (TryGetProperty(root, "markets", out var marketsElement) && marketsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var market in marketsElement.EnumerateArray())
                    markets.Add(ParseMarket(market));
            }

            return new PolymarketEventSnapshot
            {
                EventId = StringProperty(root, "id"),
                Slug = StringProperty(root, "slug"),
                Title = StringPropertyAny(root, "title", "name"),
                Active = BoolProperty(root, "active"),
                Closed = BoolProperty(root, "closed"),
                Archived = BoolProperty(root, "archived"),
                Live = BoolProperty(root, "live"),
                Ended = BoolProperty(root, "ended"),
                Score = StringProperty(root, "score"),
                Elapsed = StringProperty(root, "elapsed"),
                StartTimeUtc = DateTimeOffsetPropertyAny(root, "startTime", "start_time", "startDate", "start_date", "gameStartTime", "game_start_time"),
                EndTimeUtc = DateTimeOffsetPropertyAny(root, "endTime", "end_time", "endDate", "end_date"),
                Volume = DecimalPropertyAny(root, "volume", "volumeNum", "volume_num"),
                Volume24h = DecimalPropertyAny(root, "volume24hr", "volume24h", "volume24hrClob", "volume_24hr", "volume_24h"),
                LiquidityGamma = DecimalPropertyAny(root, "liquidity", "liquidityNum", "liquidity_num"),
                FetchedAtUtc = fetchedAtUtc,
                RawPayloadHash = rawPayloadHash,
                Markets = markets
            };
        }

        public static IReadOnlyList<PolymarketEventSnapshot> ParseEvents(JsonElement root, DateTimeOffset fetchedAtUtc, string? rawPayloadHash = null)
        {
            var array = RootArrayOrProperty(root, "data", "events");
            if (array.ValueKind != JsonValueKind.Array)
                return [];

            var events = new List<PolymarketEventSnapshot>();
            foreach (var item in array.EnumerateArray())
                events.Add(ParseEvent(item, fetchedAtUtc, rawPayloadHash));
            return events;
        }

        public static PolymarketEventPage ParseEventPage(JsonElement root, DateTimeOffset fetchedAtUtc, string? rawPayloadHash = null)
        {
            var nextCursor = StringPropertyAny(root, "next_cursor", "nextCursor");
            return new PolymarketEventPage(ParseEvents(root, fetchedAtUtc, rawPayloadHash), nextCursor);
        }

        public static IReadOnlyList<PolymarketMarketSnapshot> ParseMarkets(JsonElement root)
        {
            var array = RootArrayOrProperty(root, "data", "markets");
            if (array.ValueKind != JsonValueKind.Array)
                return [];

            var markets = new List<PolymarketMarketSnapshot>();
            foreach (var market in array.EnumerateArray())
                markets.Add(ParseMarket(market));
            return markets;
        }

        public static PolymarketMarketPage ParseMarketPage(JsonElement root)
        {
            var nextCursor = StringPropertyAny(root, "next_cursor", "nextCursor");
            return new PolymarketMarketPage(ParseMarkets(root), nextCursor);
        }

        public static IReadOnlyList<PolymarketSportsMarketType> ParseSportsMarketTypes(JsonElement root)
        {
            var array = RootArrayOrProperty(root, "data", "marketTypes", "sportsMarketTypes", "sports_market_types");
            if (array.ValueKind == JsonValueKind.Array)
            {
                return array.EnumerateArray()
                    .Select(ParseSportsMarketType)
                    .Where(type => !string.IsNullOrWhiteSpace(type.Slug))
                    .DistinctBy(type => type.Slug, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                var types = new List<PolymarketSportsMarketType>();
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        types.AddRange(property.Value.EnumerateArray()
                            .Select(ParseSportsMarketType)
                            .Where(type => !string.IsNullOrWhiteSpace(type.Slug)));
                    }
                    else if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Object)
                    {
                        var type = ParseSportsMarketType(property.Value);
                        if (!string.IsNullOrWhiteSpace(type.Slug))
                            types.Add(type);
                    }
                }

                return types.DistinctBy(type => type.Slug, StringComparer.OrdinalIgnoreCase).ToList();
            }

            return [];
        }

        public static PolymarketComboMarketPage ParseComboMarketPage(JsonElement root)
        {
            var array = RootArrayOrProperty(root, "data", "markets", "comboMarkets", "combo_markets");
            var markets = new List<PolymarketComboMarketSnapshot>();
            if (array.ValueKind == JsonValueKind.Array)
            {
                foreach (var market in array.EnumerateArray())
                    markets.Add(ParseComboMarket(market));
            }

            var nextCursor = StringPropertyAny(root, "next_cursor", "nextCursor");
            if (string.IsNullOrWhiteSpace(nextCursor) && TryGetProperty(root, "pagination", out var pagination))
                nextCursor = StringPropertyAny(pagination, "next_cursor", "nextCursor");

            return new PolymarketComboMarketPage(markets, nextCursor);
        }

        public static PolymarketComboMarketSnapshot ParseComboMarket(JsonElement market)
        {
            var outcomes = ParseStringList(PropertyOrDefaultAny(market, "outcomes", "outcome_names"));
            var tokenIds = ParseStringList(PropertyOrDefaultAny(market, "clobTokenIds", "clob_token_ids", "token_ids"));
            var prices = ParseDecimalList(PropertyOrDefaultAny(market, "outcomePrices", "outcome_prices", "prices"));
            var tokens = new List<PolymarketOutcomeToken>();
            for (var i = 0; i < Math.Min(outcomes.Count, tokenIds.Count); i++)
            {
                tokens.Add(new PolymarketOutcomeToken(
                    outcomes[i],
                    tokenIds[i],
                    i < prices.Count ? prices[i] : null));
            }

            return new PolymarketComboMarketSnapshot
            {
                ComboMarketId = StringPropertyAny(market, "id", "marketId", "market_id"),
                Slug = StringProperty(market, "slug"),
                Question = StringPropertyAny(market, "question", "title", "name"),
                ConditionId = StringPropertyAny(market, "conditionId", "condition_id", "condition"),
                SportsMarketType = StringPropertyAny(market, "sportsMarketType", "sports_market_type", "sportsMarketTypeSlug"),
                Line = DecimalPropertyAny(market, "line", "marketLine"),
                Prices = prices,
                Tokens = tokens
            };
        }

        public static IReadOnlyList<PolymarketPositionSnapshot> ParsePositions(JsonElement root)
        {
            var array = RootArrayOrProperty(root, "data", "positions");
            if (array.ValueKind != JsonValueKind.Array)
                return [];

            var positions = new List<PolymarketPositionSnapshot>();
            foreach (var item in array.EnumerateArray())
            {
                positions.Add(new PolymarketPositionSnapshot
                {
                    ProxyWallet = StringPropertyAny(item, "proxyWallet", "proxy_wallet"),
                    Asset = StringProperty(item, "asset"),
                    ConditionId = StringPropertyAny(item, "conditionId", "condition_id"),
                    Size = DecimalProperty(item, "size"),
                    AveragePrice = DecimalPropertyAny(item, "avgPrice", "avg_price"),
                    InitialValue = DecimalPropertyAny(item, "initialValue", "initial_value"),
                    CurrentValue = DecimalPropertyAny(item, "currentValue", "current_value"),
                    CashPnl = DecimalPropertyAny(item, "cashPnl", "cash_pnl"),
                    PercentPnl = DecimalPropertyAny(item, "percentPnl", "percent_pnl"),
                    TotalBought = DecimalPropertyAny(item, "totalBought", "total_bought"),
                    RealizedPnl = DecimalPropertyAny(item, "realizedPnl", "realized_pnl"),
                    PercentRealizedPnl = DecimalPropertyAny(item, "percentRealizedPnl", "percent_realized_pnl"),
                    CurrentPrice = DecimalPropertyAny(item, "curPrice", "cur_price", "currentPrice", "current_price"),
                    Redeemable = BoolProperty(item, "redeemable"),
                    Mergeable = BoolProperty(item, "mergeable"),
                    Title = StringProperty(item, "title"),
                    Slug = StringProperty(item, "slug"),
                    Icon = StringProperty(item, "icon"),
                    EventSlug = StringPropertyAny(item, "eventSlug", "event_slug"),
                    Outcome = StringProperty(item, "outcome"),
                    OutcomeIndex = IntPropertyAny(item, "outcomeIndex", "outcome_index"),
                    OppositeOutcome = StringPropertyAny(item, "oppositeOutcome", "opposite_outcome"),
                    OppositeAsset = StringPropertyAny(item, "oppositeAsset", "opposite_asset"),
                    EndDate = StringPropertyAny(item, "endDate", "end_date"),
                    NegativeRisk = BoolPropertyAny(item, "negativeRisk", "negative_risk", "negRisk")
                });
            }

            return positions;
        }

        public static PolymarketMarketSnapshot ParseMarket(JsonElement market)
        {
            var parsedTokens = ParseMarketTokens(market);
            var outcomes = parsedTokens.Outcomes;
            var tokenIds = parsedTokens.TokenIds;
            var prices = ParseDecimalList(PropertyOrDefaultAny(market, "outcomePrices", "outcome_prices", "prices"));
            var tokens = new List<PolymarketOutcomeToken>();
            for (var i = 0; i < Math.Min(outcomes.Count, tokenIds.Count); i++)
            {
                tokens.Add(new PolymarketOutcomeToken(
                    outcomes[i],
                    tokenIds[i],
                    i < prices.Count ? prices[i] : null));
            }

            var rejectReasons = new List<PolymarketRejectReason>();
            var conditionId = StringPropertyAny(market, "conditionId", "condition_id", "condition");
            var active = BoolProperty(market, "active");
            var closed = BoolProperty(market, "closed");
            var archived = BoolProperty(market, "archived");
            var acceptingOrders = BoolPropertyNullableAny(market, "acceptingOrders", "accepting_orders", "accepting_orders_enabled");
            var enableOrderBook = BoolPropertyNullableAny(market, "enableOrderBook", "enable_order_book") ?? true;
            var liquidity = DecimalPropertyAny(market, "liquidity", "liquidityNum", "liquidity_num");
            var dataQualityFlags = new List<string>();
            if (tokenIds.Count == 0)
                dataQualityFlags.Add("missing_token_ids");
            if (!liquidity.HasValue)
                dataQualityFlags.Add("liquidity_unknown");
            else if (liquidity.Value == 0m)
                dataQualityFlags.Add("liquidity_zero_confirmed");

            if (string.IsNullOrWhiteSpace(conditionId))
                rejectReasons.Add(PolymarketRejectReason.MissingConditionId);
            if (tokens.Count == 0)
                rejectReasons.Add(PolymarketRejectReason.MissingTokenId);
            if (!active || closed || archived || acceptingOrders == false)
                rejectReasons.Add(PolymarketRejectReason.ClosedOrNotAccepting);
            if (!enableOrderBook)
                rejectReasons.Add(PolymarketRejectReason.NoOrderBook);
            if (!DecimalPropertyAny(market, "orderMinSize", "order_min_size").HasValue)
                rejectReasons.Add(PolymarketRejectReason.MissingMinOrderSize);
            if (!DecimalPropertyAny(market, "orderPriceMinTickSize", "order_price_min_tick_size", "minimum_tick_size").HasValue)
                rejectReasons.Add(PolymarketRejectReason.MissingTickSize);

            return new PolymarketMarketSnapshot
            {
                MarketId = StringPropertyAny(market, "id", "marketId", "market_id"),
                Slug = StringProperty(market, "slug"),
                Question = StringPropertyAny(market, "question", "title", "name"),
                GroupItemTitle = StringPropertyAny(market, "groupItemTitle", "group_item_title"),
                Description = StringProperty(market, "description"),
                ConditionId = conditionId,
                Active = active,
                Closed = closed,
                Archived = archived,
                AcceptingOrders = acceptingOrders,
                EnableOrderBook = enableOrderBook,
                OrderMinSize = DecimalPropertyAny(market, "orderMinSize", "order_min_size"),
                OrderPriceMinTickSize = DecimalPropertyAny(market, "orderPriceMinTickSize", "order_price_min_tick_size", "minimum_tick_size"),
                Category = StringProperty(market, "category"),
                MarketType = StringPropertyAny(market, "marketType", "market_type"),
                SportsMarketType = StringPropertyAny(market, "sportsMarketType", "sports_market_type", "sportsMarketTypeSlug"),
                Line = DecimalPropertyAny(market, "line", "marketLine"),
                GameStartTime = StringPropertyAny(market, "gameStartTime", "game_start_time"),
                StartTimeUtc = DateTimeOffsetPropertyAny(market, "startTime", "start_time", "startDate", "start_date", "gameStartTime", "game_start_time"),
                EndTimeUtc = DateTimeOffsetPropertyAny(market, "endTime", "end_time", "endDate", "end_date"),
                Volume = DecimalPropertyAny(market, "volume", "volumeNum", "volume_num"),
                Volume24h = DecimalPropertyAny(market, "volume24hr", "volume24h", "volume24hrClob", "volume_24hr", "volume_24h"),
                LiquidityGamma = liquidity,
                Outcomes = outcomes,
                ClobTokenIds = tokenIds,
                Tokens = tokens,
                RejectReasons = rejectReasons,
                DataQuality = new PolymarketMarketDataQuality
                {
                    MissingTokenIds = tokenIds.Count == 0,
                    LiquidityMissing = !liquidity.HasValue,
                    LiquidityZeroConfirmed = liquidity == 0m,
                    Flags = dataQualityFlags
                },
                Raw = market.GetRawText()
            };
        }

        private sealed record ParsedMarketTokens(IReadOnlyList<string> Outcomes, IReadOnlyList<string> TokenIds);

        private static ParsedMarketTokens ParseMarketTokens(JsonElement market)
        {
            var outcomes = ParseStringList(PropertyOrDefaultAny(market, "outcomes", "outcome_names", "outcomeNames"));
            var tokenIds = ParseStringList(PropertyOrDefaultAny(market, "clobTokenIds", "clob_token_ids", "token_ids", "tokenIds"));
            var tokenOutcomes = new List<string>();
            var objectTokenIds = new List<string>();

            if (TryGetProperty(market, "tokens", out var tokensElement))
            {
                foreach (var token in TokenArray(tokensElement))
                {
                    var outcome = StringPropertyAny(token, "outcome", "name", "label", "outcomeName", "outcome_name");
                    var tokenId = StringPropertyAny(token, "token_id", "tokenId", "clobTokenId", "clob_token_id", "id", "asset_id", "assetId");
                    if (!string.IsNullOrWhiteSpace(outcome))
                        tokenOutcomes.Add(outcome);
                    if (!string.IsNullOrWhiteSpace(tokenId))
                        objectTokenIds.Add(tokenId);
                }
            }

            if (outcomes.Count == 0 && tokenOutcomes.Count > 0)
                outcomes = tokenOutcomes;
            if (tokenIds.Count == 0 && objectTokenIds.Count > 0)
                tokenIds = objectTokenIds;

            return new ParsedMarketTokens(outcomes, tokenIds);
        }

        private static IEnumerable<JsonElement> TokenArray(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
                return element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).ToList();

            if (element.ValueKind == JsonValueKind.String)
            {
                var raw = element.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return [];
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        return doc.RootElement.EnumerateArray()
                            .Where(item => item.ValueKind == JsonValueKind.Object)
                            .Select(item => item.Clone())
                            .ToList();
                }
                catch (JsonException)
                {
                    return [];
                }
            }

            return [];
        }

        public static PolymarketOrderBookSnapshot ParseBook(string tokenId, JsonElement root, DateTimeOffset fetchedAtUtc, string? rawPayloadHash = null)
        {
            var bids = ParseBookLevels(PropertyOrDefault(root, "bids")).OrderByDescending(level => level.Price).ToList();
            var asks = ParseBookLevels(PropertyOrDefault(root, "asks")).OrderBy(level => level.Price).ToList();
            var bestBid = bids.OrderByDescending(level => level.Price).FirstOrDefault();
            var bestAsk = asks.OrderBy(level => level.Price).FirstOrDefault();
            var rejectReasons = new List<PolymarketRejectReason>();
            if (bestBid is null)
                rejectReasons.Add(PolymarketRejectReason.NoBid);
            if (bestAsk is null)
                rejectReasons.Add(PolymarketRejectReason.NoAsk);
            var minOrderSize = DecimalProperty(root, "min_order_size");
            var tickSize = DecimalProperty(root, "tick_size");
            if (!minOrderSize.HasValue)
                rejectReasons.Add(PolymarketRejectReason.MissingMinOrderSize);
            if (!tickSize.HasValue)
                rejectReasons.Add(PolymarketRejectReason.MissingTickSize);

            return new PolymarketOrderBookSnapshot
            {
                TokenId = tokenId,
                ConditionId = StringProperty(root, "market"),
                BestBid = bestBid?.Price,
                BidSize = bestBid?.Size,
                BestAsk = bestAsk?.Price,
                AskSize = bestAsk?.Size,
                MinOrderSize = minOrderSize,
                TickSize = tickSize,
                NegRisk = BoolProperty(root, "neg_risk"),
                LastTradePrice = DecimalProperty(root, "last_trade_price"),
                FetchedAtUtc = fetchedAtUtc,
                RawPayloadHash = rawPayloadHash,
                Bids = bids,
                Asks = asks,
                RejectReasons = rejectReasons
            };
        }

        private static PolymarketSportsMarketType ParseSportsMarketType(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var slug = element.GetString() ?? string.Empty;
                return new PolymarketSportsMarketType(slug);
            }

            if (element.ValueKind != JsonValueKind.Object)
                return new PolymarketSportsMarketType(string.Empty);

            var slugValue = StringPropertyAny(element, "slug", "id", "key", "type", "marketType", "sportsMarketType", "sports_market_type") ?? string.Empty;
            return new PolymarketSportsMarketType(
                slugValue,
                StringPropertyAny(element, "name", "label", "title"),
                StringPropertyAny(element, "sport", "sportSlug", "sport_slug"));
        }

        private static JsonElement RootArrayOrProperty(JsonElement root, params string[] names)
        {
            if (root.ValueKind == JsonValueKind.Array)
                return root;

            foreach (var name in names)
            {
                if (TryGetProperty(root, name, out var value) && value.ValueKind == JsonValueKind.Array)
                    return value;
            }

            return default;
        }

        private static IReadOnlyList<PolymarketBookLevel> ParseBookLevels(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array)
                return [];

            var levels = new List<PolymarketBookLevel>();
            foreach (var level in element.EnumerateArray())
            {
                var price = DecimalProperty(level, "price");
                var size = DecimalProperty(level, "size");
                if (price is >= 0 and <= 1 && size is > 0)
                    levels.Add(new PolymarketBookLevel(price.Value, size.Value));
            }

            return levels;
        }

        private static IReadOnlyList<string> ParseStringList(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
                return element.EnumerateArray().Select(ValueAsString).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToList();

            if (element.ValueKind == JsonValueKind.String)
            {
                var raw = element.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                    return [];
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        return doc.RootElement.EnumerateArray().Select(ValueAsString).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToList();
                }
                catch (JsonException)
                {
                    if (raw.TrimStart().StartsWith("[", StringComparison.Ordinal) || raw.TrimStart().StartsWith("{", StringComparison.Ordinal))
                        return [];
                    return [raw];
                }
            }

            return [];
        }

        private static IReadOnlyList<decimal?> ParseDecimalList(JsonElement element) =>
            ParseStringList(element).Select(ParseDecimal).ToList();

        private static string? ValueAsString(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };

        private static JsonElement PropertyOrDefault(JsonElement element, string name) =>
            TryGetProperty(element, name, out var value) ? value : default;

        private static JsonElement PropertyOrDefaultAny(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (TryGetProperty(element, name, out var value))
                    return value;
            }

            return default;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
                return true;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static string? StringProperty(JsonElement element, string name) =>
            TryGetProperty(element, name, out var value) ? ValueAsString(value) : null;

        private static string? StringPropertyAny(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                var value = StringProperty(element, name);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        private static bool BoolProperty(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out var value))
                return false;
            return BoolValue(value) ?? false;
        }

        private static bool BoolPropertyAny(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (TryGetProperty(element, name, out var value))
                    return BoolValue(value) ?? false;
            }

            return false;
        }

        private static bool? BoolPropertyNullableAny(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (TryGetProperty(element, name, out var value))
                    return BoolValue(value);
            }

            return null;
        }

        private static bool? BoolValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) ? parsed : null,
                JsonValueKind.Number => decimal.TryParse(value.GetRawText(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number != 0m : null,
                _ => null
            };
        }

        private static decimal? DecimalProperty(JsonElement element, string name) =>
            TryGetProperty(element, name, out var value) ? ParseDecimal(ValueAsString(value)) : null;

        private static decimal? DecimalPropertyAny(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                var value = DecimalProperty(element, name);
                if (value.HasValue)
                    return value;
            }

            return null;
        }

        private static int? IntPropertyAny(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (!TryGetProperty(element, name, out var value))
                    continue;
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
                    return number;
                if (int.TryParse(ValueAsString(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }

            return null;
        }

        private static decimal? ParseDecimal(string? value) =>
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

        private static DateTimeOffset? DateTimeOffsetPropertyAny(JsonElement element, params string[] names)
        {
            foreach (var name in names)
            {
                if (DateTimeOffset.TryParse(StringProperty(element, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                    return parsed.ToUniversalTime();
            }

            return null;
        }

        private static string TrimSlash(string value) => value.TrimEnd('/');

        private static void ValidateEventKeysetLimit(int limit)
        {
            if (limit is < 1 or > GammaEventsKeysetMaxLimit)
                throw new ArgumentOutOfRangeException(nameof(limit), $"Limit must be between 1 and {GammaEventsKeysetMaxLimit}.");
        }

        private static void ValidateMarketKeysetLimit(int limit)
        {
            if (limit is < 1 or > GammaMarketsKeysetMaxLimit)
                throw new ArgumentOutOfRangeException(nameof(limit), $"Limit must be between 1 and {GammaMarketsKeysetMaxLimit}.");
        }

        private static string MarketDedupKey(PolymarketMarketSnapshot market) =>
            !string.IsNullOrWhiteSpace(market.ConditionId)
                ? $"condition:{market.ConditionId}"
                : !string.IsNullOrWhiteSpace(market.MarketId)
                    ? $"market:{market.MarketId}"
                    : $"slug:{market.Slug}";

        private static bool IsHexAddress(string value) =>
            value.Length == 42 &&
            value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            value.Skip(2).All(Uri.IsHexDigit);
    }
}
