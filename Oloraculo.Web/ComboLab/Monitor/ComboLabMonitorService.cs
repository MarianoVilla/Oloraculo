using Oloraculo.Web.ComboLab.Markets;

namespace Oloraculo.Web.ComboLab.Monitor
{
    public sealed class ComboLabMonitorService
    {
        public static readonly IReadOnlyList<string> FallbackWorldCupSportsMarketTypes =
        [
            "moneyline",
            "spreads",
            "totals",
            "soccer_team_totals",
            "btts",
            "soccer_exact_score",
            "soccer_first_to_score",
            "soccer_halftime_result",
            "soccer_second_half_result",
            "first_half_totals",
            "second_half_totals",
            "total_corners",
            "soccer_team_total_corners",
            "first_corner",
            "soccer_player_shots",
            "soccer_player_shots_on_target",
            "soccer_player_goals",
            "soccer_player_assists",
            "goalkeeper_saves"
        ];

        private readonly PolymarketMarketDataService _markets;

        public ComboLabMonitorService(PolymarketMarketDataService markets) => _markets = markets;

        public async Task<ComboLabUniverseMonitorSnapshot> RefreshUniverseAsync(int comboMaxPages = 10, CancellationToken ct = default)
        {
            if (comboMaxPages <= 0)
                throw new ArgumentOutOfRangeException(nameof(comboMaxPages), "Combo max pages must be positive.");

            var errors = new List<string>();
            var sportsMarketTypes = await DiscoverSportsMarketTypesAsync(errors, ct);
            var gammaMarkets = new List<PolymarketMarketSnapshot>();
            var seenMarkets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sportsMarketType in sportsMarketTypes)
            {
                try
                {
                    var batch = await _markets.FetchWorldCupMarketsByTypeAsync(sportsMarketType, ct);
                    foreach (var market in batch)
                    {
                        var key = MarketIdentity(market);
                        if (seenMarkets.Add(key))
                            gammaMarkets.Add(market);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Gamma markets failed for sportsMarketType={sportsMarketType}: {ex.Message}");
                }
            }

            IReadOnlyList<PolymarketComboMarketSnapshot> comboMarkets = [];
            try
            {
                comboMarkets = await _markets.FetchComboMarketsAsync(comboMaxPages, ct: ct);
            }
            catch (Exception ex)
            {
                errors.Add($"Combo RFQ catalog failed: {ex.Message}");
            }

            var report = PolymarketFootballUniverseReporter.Build(gammaMarkets, comboMarkets);
            return new ComboLabUniverseMonitorSnapshot(
                DateTimeOffset.UtcNow,
                sportsMarketTypes,
                report,
                ProjectRows(report),
                errors);
        }

        public static IReadOnlyList<ComboLabMonitorMarketRow> ProjectRows(PolymarketFootballUniverseReport report) =>
            report.Rows
                .Select(row => new ComboLabMonitorMarketRow(
                    row.MarketId,
                    row.Slug,
                    row.Market.Question,
                    row.ConditionId,
                    row.SportsMarketType,
                    row.Classification.Family,
                    row.Classification.Coverage,
                    row.ComboEligible,
                    Verdict(row),
                    row.RejectReasons))
                .OrderBy(row => row.Verdict)
                .ThenBy(row => row.Family)
                .ThenBy(row => row.Slug)
                .ToList();

        public static ComboLabMonitorVerdict Verdict(PolymarketFootballUniverseRow row)
        {
            if (row.RejectReasons.Count > 0)
                return ComboLabMonitorVerdict.SourceBlocked;
            if (row.Classification.Family == PolymarketFootballMarketFamily.Unknown)
                return ComboLabMonitorVerdict.UnknownFamily;
            if (row.Classification.NeedsNewModel || row.Classification.Coverage == PolymarketFootballModelCoverage.GoalTimingApproximation)
                return ComboLabMonitorVerdict.NeedsModel;
            if (!row.ComboEligible)
                return ComboLabMonitorVerdict.NotComboEligible;
            return ComboLabMonitorVerdict.ReadyForPricing;
        }

        private async Task<IReadOnlyList<string>> DiscoverSportsMarketTypesAsync(List<string> errors, CancellationToken ct)
        {
            try
            {
                var types = await _markets.FetchSportsMarketTypesAsync(ct);
                var selected = types
                    .Select(type => type.Slug)
                    .Where(IsWorldCupRelevantMarketType)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (selected.Count > 0)
                    return selected;
                errors.Add("No recognized World Cup market types; using fallback list.");
            }
            catch (Exception ex)
            {
                errors.Add($"Sports market type discovery failed: {ex.Message}; using fallback list.");
            }

            return FallbackWorldCupSportsMarketTypes;
        }

        private static bool IsWorldCupRelevantMarketType(string? sportsMarketType)
        {
            if (string.IsNullOrWhiteSpace(sportsMarketType))
                return false;
            if (FallbackWorldCupSportsMarketTypes.Contains(sportsMarketType, StringComparer.OrdinalIgnoreCase))
                return true;
            var classification = PolymarketFootballMarketClassifier.Classify(sportsMarketType);
            return classification.Family != PolymarketFootballMarketFamily.Unknown;
        }

        private static string MarketIdentity(PolymarketMarketSnapshot market)
        {
            if (!string.IsNullOrWhiteSpace(market.ConditionId))
                return market.ConditionId;
            if (!string.IsNullOrWhiteSpace(market.MarketId))
                return market.MarketId;
            return market.Slug ?? string.Empty;
        }
    }
}
