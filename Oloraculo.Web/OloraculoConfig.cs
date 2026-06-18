namespace Oloraculo.Web
{
    public class OloraculoConfig
    {
        public int SimulationCount { get; set; }
        public int? SimulationSeed { get; set; }
        public int RecentResultCount { get; set; }
        public int GoalModelYearsWindow { get; set; }
        public string ApiFootballBaseUrl { get; set; } = "https://v3.football.api-sports.io/";
        public string? ApiFootballApiKey { get; set; }
        public int ApiFootballLeagueId { get; set; }
        public int ApiFootballSeason { get; set; }
        public bool RankingRefreshOnStartup { get; set; } = true;
        public int EloRefreshMaxLookbackDays { get; set; } = 14;
        public string FifaRankingsRawUrl { get; set; } = "https://en.wikipedia.org/w/index.php?title=Module:SportsRankings/data/FIFA_World_Rankings&action=raw";
        public string EloRankingsBaseUrl { get; set; } = "https://www.international-football.net/elo-ratings-table";
        public string RankingRefreshUserAgent { get; set; } = "WorldCupEdgeLab";
        public string GoalscorersRawUrl { get; set; } = "https://raw.githubusercontent.com/martj42/international_results/refs/heads/master/goalscorers.csv";
        public int GoalscorerLookbackYears { get; set; } = 6;
        public string OpenRouterBaseUrl { get; set; } = "https://openrouter.ai/api/v1/";
        public string? OpenRouterApiKey { get; set; }
        public string OpenRouterModel { get; set; } = "openai/gpt-4o-mini";
        public string PolymarketGammaBaseUrl { get; set; } = "https://gamma-api.polymarket.com";
        public string PolymarketClobBaseUrl { get; set; } = "https://clob.polymarket.com";
        public string PolymarketDataBaseUrl { get; set; } = "https://data-api.polymarket.com";
        public string PolymarketComboRfqBaseUrl { get; set; } = "https://combos-rfq-api.polymarket.com";
        public string[] PolymarketClobStatusTokenIds { get; set; } = [];
        public decimal PolymarketClobStatusMinimumDepthUsd { get; set; } = 1m;
        public string DatabetSportsbookBaseUrl { get; set; } = "https://sportsbook-gql.databet.cloud/graphql";
        public string DatabetSportsbookAuthEnvironmentVariable { get; set; } = "SPORTSBOOK_XAUTH";
        public string DatabetSportsbookHealthQuery { get; set; } = "{ sportEventListByFilters(offset:0, limit:10, matchStatuses:[LIVE]) { sportEvents { id } } }";
        public string DatabetWidgetsBaseUrl { get; set; } = "https://widgets-gql.databet.cloud/graphql";
        public string DatabetWidgetsTokenEnvironmentVariable { get; set; } = "DATABET_WIDGET_TOKEN";
        public string DatabetWidgetsHealthQuery { get; set; } = "{ listSportEventWidgets(filter: {}) { id sport status } }";
        public string OddsPapiBaseUrl { get; set; } = "https://api.oddspapi.io/v4/";
        public string OddsPapiKeyEnvironmentVariable { get; set; } = "ODDSPAPI_KEY";
        public string OddsPapiBookmaker { get; set; } = "pinnacle";
        public string OddsPapiHealthPath { get; set; } = "odds-by-tournaments?tournamentIds=16";
        public string GridBaseUrl { get; set; } = "";
        public string GridKeyEnvironmentVariable { get; set; } = "GRID_KEY";
        public string GridHealthPath { get; set; } = "health";
        public ObjectArchiveConfig ObjectArchive { get; set; } = new();
        public string[] AvailabilitySourceUrls { get; set; } =
        [
            "https://www.espn.com/soccer/story/_/id/48572979/2026-fifa-world-cup-injuries-tracker-which-stars-miss-latest-info",
            "https://talksport.com/football/world-cup/4311921/world-cup-2026-injury-tracker-full-squads-messi/"
        ];
        public string AvailabilityRefreshUserAgent { get; set; } = "WorldCupEdgeLab";
        public int AvailabilityMaxArticleChars { get; set; } = 24000;
        public bool AvailabilityRequireCrossCheck { get; set; } = true;
    }

    public sealed class ObjectArchiveConfig
    {
        public bool Enabled { get; set; }
        public string Provider { get; set; } = "R2";
        public string? Bucket { get; set; }
        public string? Endpoint { get; set; }
        public string Region { get; set; } = "auto";
        public string Prefix { get; set; } = "oloraculo";
        public bool ForcePathStyle { get; set; } = true;
        public string BucketEnvironmentVariable { get; set; } = "OLORACULO_R2_BUCKET";
        public string EndpointEnvironmentVariable { get; set; } = "OLORACULO_R2_ENDPOINT";
        public string RegionEnvironmentVariable { get; set; } = "AWS_REGION";
        public string AccessKeyIdEnvironmentVariable { get; set; } = "OLORACULO_R2_ACCESS_KEY_ID";
        public string SecretAccessKeyEnvironmentVariable { get; set; } = "OLORACULO_R2_SECRET_ACCESS_KEY";
        public int MaxPendingLocalBatchCount { get; set; }
    }

    public static class OloraculoDataFiles
    {
        public const string GroupsCsv = "wc2026_groups.csv";
        public const string EloCsv = "elo_snapshot.csv";
        public const string FifaRankingsCsv = "fifa_rankings.csv";
        public const string HistoricalResultsCsv = "historical_results.csv";
        public const string GoalscorersCsv = "goalscorers.csv";
    }
}
