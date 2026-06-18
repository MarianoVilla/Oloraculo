using System.Text.RegularExpressions;

namespace Oloraculo.Web.ComboLab.Markets
{
    public static class PolymarketWorldCupMarketSurface
    {
        public const string SportSlug = "fifwc";
        public const int TagId = 102232;
        public const int SeriesId = 11433;
    }

    public enum PolymarketFootballMarketFamily
    {
        Unknown,
        Moneyline,
        Spread,
        MatchTotal,
        TeamTotal,
        BothTeamsToScore,
        ExactScore,
        FirstToScore,
        HalftimeResult,
        SecondHalfResult,
        FirstHalfTotal,
        SecondHalfTotal,
        TotalCorners,
        TeamTotalCorners,
        FirstCorner,
        PlayerShots,
        PlayerShotsOnTarget,
        PlayerGoals,
        PlayerAssists,
        GoalkeeperSaves,
        TournamentFuture
    }

    public enum PolymarketFootballModelCoverage
    {
        UnsupportedOrUnknown,
        ScorelineGrid,
        GoalTimingApproximation,
        HalfSplitModelNeeded,
        CornerModelNeeded,
        PlayerModelNeeded,
        TournamentSimulationNeeded
    }

    public sealed record PolymarketFootballMarketClassification(
        PolymarketFootballMarketFamily Family,
        PolymarketFootballModelCoverage Coverage,
        string Evidence,
        string Notes)
    {
        public bool IsScorelineGridPriced => Coverage == PolymarketFootballModelCoverage.ScorelineGrid;
        public bool NeedsNewModel => Coverage is
            PolymarketFootballModelCoverage.HalfSplitModelNeeded or
            PolymarketFootballModelCoverage.CornerModelNeeded or
            PolymarketFootballModelCoverage.PlayerModelNeeded or
            PolymarketFootballModelCoverage.TournamentSimulationNeeded;
    }

    public static class PolymarketFootballMarketClassifier
    {
        public static PolymarketFootballMarketClassification Classify(PolymarketMarketSnapshot market)
        {
            ArgumentNullException.ThrowIfNull(market);
            var question = string.Join(' ', new[] { market.GroupItemTitle, market.Question, string.Join(' ', market.Outcomes) }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return Classify(market.SportsMarketType, market.MarketType, question, market.Slug, market.Description);
        }

        public static PolymarketFootballMarketClassification Classify(
            string? sportsMarketType,
            string? marketType = null,
            string? question = null,
            string? slug = null,
            string? description = null)
        {
            var evidence = string.Join(' ', new[] { sportsMarketType, marketType, question, slug?.Replace('-', ' '), description }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var normalizedType = Normalize(sportsMarketType ?? marketType ?? string.Empty);
            var normalizedText = Normalize(evidence);

            if (normalizedType.Contains("corner") || normalizedType.Contains("player") || normalizedType.Contains("goalkeeper") || normalizedType.Contains("half") || normalizedType.Contains("firsttoscore"))
                return ClassifyNonScoreline(normalizedType, normalizedText, evidence);

            if (IsAny(normalizedType, "moneyline", "soccermoneyline", "matchwinner", "fulltimeresult", "threeway", "1x2") ||
                normalizedText.Contains("moneyline") ||
                normalizedText.Contains("matchwinner") ||
                normalizedText.Contains("whowillwin") ||
                normalizedText.Contains("whowins") ||
                normalizedText.Contains("towin"))
                return Scoreline(PolymarketFootballMarketFamily.Moneyline, evidence, "Regulation outcome can be priced from the scoreline grid.");

            if (IsAny(normalizedType, "spreads", "spread", "handicap", "soccerhandicap") ||
                normalizedText.Contains("spread") ||
                normalizedText.Contains("handicap") ||
                LooksLikeSignedLine(evidence))
                return Scoreline(PolymarketFootballMarketFamily.Spread, evidence, "Half-point regulation handicap can be priced from the scoreline grid.");

            if (IsAny(normalizedType, "soccerteamtotals", "teamtotals", "teamtotal", "teamtotalgoals", "soccerteamtotalgoals") || normalizedText.Contains("teamtotalgoals") || normalizedText.Contains("teamtotal"))
                return Scoreline(PolymarketFootballMarketFamily.TeamTotal, evidence, "Regulation team total goals can be priced from the scoreline grid.");

            if (IsAny(normalizedType, "totals", "totalgoals", "soccertotals", "soccertotalgoals", "matchtotal") ||
                normalizedText.Contains("totalgoals") ||
                normalizedText.Contains("overunder") ||
                LooksLikeOverUnderGoals(evidence))
                return Scoreline(PolymarketFootballMarketFamily.MatchTotal, evidence, "Regulation total goals can be priced from the scoreline grid.");

            if (IsAny(normalizedType, "btts", "soccerbtts", "bothteamstoscore", "soccerbothteamstoscore") || normalizedText.Contains("btts") || normalizedText.Contains("bothteamstoscore"))
                return Scoreline(PolymarketFootballMarketFamily.BothTeamsToScore, evidence, "BTTS can be priced from the scoreline grid.");

            if (IsAny(normalizedType, "soccerexactscore", "exactscore", "correctscore") || normalizedText.Contains("exactscore") || normalizedText.Contains("correctscore"))
                return Scoreline(PolymarketFootballMarketFamily.ExactScore, evidence, "Exact score cells can be priced from the scoreline grid; field buckets need tail handling.");

            return ClassifyNonScoreline(normalizedType, normalizedText, evidence);
        }

        private static PolymarketFootballMarketClassification ClassifyNonScoreline(string normalizedType, string normalizedText, string evidence)
        {
            if (IsAny(normalizedType, "soccerfirsttoscore", "firsttoscore", "firstteamtoscore") ||
                normalizedText.Contains("firstteamtoscore") ||
                normalizedText.Contains("firsttoscore") ||
                normalizedText.Contains("firstgoal") ||
                normalizedText.Contains("scorefirst"))
                return new(PolymarketFootballMarketFamily.FirstToScore, PolymarketFootballModelCoverage.GoalTimingApproximation, evidence, "Needs goal-timing approximation, not independent parlay math.");

            if (IsAny(normalizedType, "soccerhalftimeresult", "halftimeresult", "firsthalfresult"))
                return new(PolymarketFootballMarketFamily.HalftimeResult, PolymarketFootballModelCoverage.HalfSplitModelNeeded, evidence, "Needs first-half scoreline split.");

            if (IsAny(normalizedType, "soccersecondhalfresult", "secondhalfresult"))
                return new(PolymarketFootballMarketFamily.SecondHalfResult, PolymarketFootballModelCoverage.HalfSplitModelNeeded, evidence, "Needs second-half scoreline split.");

            if (IsAny(normalizedType, "firsthalftotals", "firsthalftotal", "soccerfirsthalftotals"))
                return new(PolymarketFootballMarketFamily.FirstHalfTotal, PolymarketFootballModelCoverage.HalfSplitModelNeeded, evidence, "Needs first-half goal distribution.");

            if (IsAny(normalizedType, "secondhalftotals", "secondhalftotal", "soccersecondhalftotals"))
                return new(PolymarketFootballMarketFamily.SecondHalfTotal, PolymarketFootballModelCoverage.HalfSplitModelNeeded, evidence, "Needs second-half goal distribution.");

            if (IsAny(normalizedType, "teamtotalcorners", "soccerteamtotalcorners") || normalizedText.Contains("teamtotalcorners"))
                return new(PolymarketFootballMarketFamily.TeamTotalCorners, PolymarketFootballModelCoverage.CornerModelNeeded, evidence, "Needs team corner-count model.");

            if (IsAny(normalizedType, "totalcorners", "soccercorners", "soccertotalcorners") || normalizedText.Contains("totalcorners"))
                return new(PolymarketFootballMarketFamily.TotalCorners, PolymarketFootballModelCoverage.CornerModelNeeded, evidence, "Needs corner-count model.");

            if (IsAny(normalizedType, "firstcorner", "soccerfirstcorner") || normalizedText.Contains("firstcorner"))
                return new(PolymarketFootballMarketFamily.FirstCorner, PolymarketFootballModelCoverage.CornerModelNeeded, evidence, "Needs first-corner model.");

            return ClassifyPlayerOrFuture(normalizedType, normalizedText, evidence);
        }

        private static PolymarketFootballMarketClassification ClassifyPlayerOrFuture(string normalizedType, string normalizedText, string evidence)
        {
            if (IsAny(normalizedType, "soccerplayershotsontarget", "playershotsontarget", "playersot") || normalizedText.Contains("shotsontarget"))
                return new(PolymarketFootballMarketFamily.PlayerShotsOnTarget, PolymarketFootballModelCoverage.PlayerModelNeeded, evidence, "Needs lineup/minutes/player SOT model.");

            if (IsAny(normalizedType, "soccerplayershots", "playershots") || normalizedText.Contains("playershots"))
                return new(PolymarketFootballMarketFamily.PlayerShots, PolymarketFootballModelCoverage.PlayerModelNeeded, evidence, "Needs lineup/minutes/player shot model.");

            if (IsAny(normalizedType, "soccerplayergoals", "playergoals", "goalscorer") || normalizedText.Contains("playergoals") || normalizedText.Contains("goalscorer"))
                return new(PolymarketFootballMarketFamily.PlayerGoals, PolymarketFootballModelCoverage.PlayerModelNeeded, evidence, "Needs lineup/minutes/player scoring model.");

            if (IsAny(normalizedType, "soccerplayerassists", "playerassists") || normalizedText.Contains("playerassists"))
                return new(PolymarketFootballMarketFamily.PlayerAssists, PolymarketFootballModelCoverage.PlayerModelNeeded, evidence, "Needs lineup/minutes/player assist model.");

            if (IsAny(normalizedType, "goalkeepersaves", "soccergoalkeepersaves", "gksaves") || normalizedText.Contains("goalkeepersaves") || normalizedText.Contains("gksaves"))
                return new(PolymarketFootballMarketFamily.GoalkeeperSaves, PolymarketFootballModelCoverage.PlayerModelNeeded, evidence, "Needs goalkeeper save model.");

            if (LooksLikeTournamentFuture(normalizedText))
                return new(PolymarketFootballMarketFamily.TournamentFuture, PolymarketFootballModelCoverage.TournamentSimulationNeeded, evidence, "Needs tournament Monte Carlo state, not a single-fixture scoreline grid.");

            return new(PolymarketFootballMarketFamily.Unknown, PolymarketFootballModelCoverage.UnsupportedOrUnknown, evidence, "Market family is not classified; reject until mapped from documented fields.");
        }

        private static bool LooksLikeTournamentFuture(string normalizedText) =>
            new[]
            {
                "winner",
                "champion",
                "advance",
                "qualify",
                "groupwinner",
                "grouplast",
                "reachround",
                "reachr16",
                "reachquarter",
                "reachsemi",
                "reachfinal",
                "stageofelimination",
                "topscorer",
                "goldenboot",
                "mostassists",
                "mostcleansheets",
                "mostgoals",
                "goalcontributions",
                "furthestadvancing",
                "unbeaten"
            }.Any(normalizedText.Contains);

        private static bool LooksLikeOverUnderGoals(string evidence) =>
            Regex.IsMatch(evidence, @"\b(over|under)\s*\d+(?:\.\d+)?\s*(?:goals?)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            Regex.IsMatch(evidence, @"\bo/u\s*\d+(?:\.\d+)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool LooksLikeSignedLine(string evidence) =>
            Regex.IsMatch(evidence, @"(?<!\d)[+-]\d+(?:\.\d+)?(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static PolymarketFootballMarketClassification Scoreline(PolymarketFootballMarketFamily family, string evidence, string notes) =>
            new(family, PolymarketFootballModelCoverage.ScorelineGrid, evidence, notes);

        private static bool IsAny(string normalizedValue, params string[] normalizedChoices) => normalizedChoices.Any(choice => normalizedValue == choice);

        private static string Normalize(string value) =>
            Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
    }
}
