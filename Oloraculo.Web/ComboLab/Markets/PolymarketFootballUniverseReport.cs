namespace Oloraculo.Web.ComboLab.Markets
{
    public sealed record PolymarketFootballUniverseRow(
        PolymarketMarketSnapshot Market,
        PolymarketFootballMarketClassification Classification,
        bool ComboEligible,
        string? ComboMarketId)
    {
        public string? MarketId => Market.MarketId;
        public string? Slug => Market.Slug;
        public string? ConditionId => Market.ConditionId;
        public string? SportsMarketType => Market.SportsMarketType;
        public IReadOnlyList<PolymarketRejectReason> RejectReasons => Market.RejectReasons;
    }

    public sealed record PolymarketFootballUniverseSummary(
        int TotalMarkets,
        int ComboEligibleMarkets,
        int SourceRejectedMarkets,
        IReadOnlyDictionary<PolymarketFootballMarketFamily, int> FamilyCounts,
        IReadOnlyDictionary<PolymarketFootballModelCoverage, int> CoverageCounts);

    public sealed record PolymarketFootballUniverseReport(
        IReadOnlyList<PolymarketFootballUniverseRow> Rows,
        PolymarketFootballUniverseSummary Summary);

    public static class PolymarketFootballUniverseReporter
    {
        public static PolymarketFootballUniverseReport Build(
            IEnumerable<PolymarketMarketSnapshot> markets,
            IEnumerable<PolymarketComboMarketSnapshot>? comboMarkets = null)
        {
            ArgumentNullException.ThrowIfNull(markets);

            var comboIndex = BuildComboIndex(comboMarkets ?? []);
            var rows = markets.Select(market =>
            {
                var classification = PolymarketFootballMarketClassifier.Classify(market);
                var comboMarket = FindComboMarket(market, comboIndex);
                return new PolymarketFootballUniverseRow(
                    market,
                    classification,
                    comboMarket is not null,
                    comboMarket?.ComboMarketId);
            }).ToList();

            var summary = new PolymarketFootballUniverseSummary(
                rows.Count,
                rows.Count(row => row.ComboEligible),
                rows.Count(row => row.RejectReasons.Count > 0),
                rows.GroupBy(row => row.Classification.Family).ToDictionary(group => group.Key, group => group.Count()),
                rows.GroupBy(row => row.Classification.Coverage).ToDictionary(group => group.Key, group => group.Count()));

            return new PolymarketFootballUniverseReport(rows, summary);
        }

        private sealed record ComboIndex(
            IReadOnlyDictionary<string, PolymarketComboMarketSnapshot> ByCondition,
            IReadOnlyDictionary<string, PolymarketComboMarketSnapshot> BySlug);

        private static ComboIndex BuildComboIndex(IEnumerable<PolymarketComboMarketSnapshot> comboMarkets)
        {
            var byCondition = new Dictionary<string, PolymarketComboMarketSnapshot>(StringComparer.OrdinalIgnoreCase);
            var bySlug = new Dictionary<string, PolymarketComboMarketSnapshot>(StringComparer.OrdinalIgnoreCase);

            foreach (var market in comboMarkets)
            {
                if (!string.IsNullOrWhiteSpace(market.ConditionId))
                    byCondition.TryAdd(market.ConditionId, market);
                if (!string.IsNullOrWhiteSpace(market.Slug))
                    bySlug.TryAdd(market.Slug, market);
            }

            return new ComboIndex(byCondition, bySlug);
        }

        private static PolymarketComboMarketSnapshot? FindComboMarket(PolymarketMarketSnapshot market, ComboIndex comboIndex)
        {
            if (!string.IsNullOrWhiteSpace(market.ConditionId) && comboIndex.ByCondition.TryGetValue(market.ConditionId, out var byCondition))
                return byCondition;
            if (!string.IsNullOrWhiteSpace(market.Slug) && comboIndex.BySlug.TryGetValue(market.Slug, out var bySlug))
                return bySlug;
            return null;
        }
    }
}
