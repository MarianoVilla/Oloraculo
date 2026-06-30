namespace Oloraculo.Web.Models.ApiFootballModels
{
    public class ApiFootballRefreshReport
    {
        public bool IsConfigured { get; init; }
        public int FixturesFetched { get; init; }
        public int FixturesMatched { get; init; }
        public int ContextRows { get; init; }
        public int FixtureInjuryRows { get; init; }
        public int LeagueInjuryRows { get; init; }
        public int LineupRows { get; init; }
        public int PreMatchOddsRows { get; init; }
        public int LiveOddsRows { get; init; }
        public int ImpactMatchedPlayers { get; init; }
        public int ImpactFallbackPlayers { get; init; }
        public IReadOnlyList<string> Notes { get; init; } = [];
        public IReadOnlyList<string> Errors { get; init; } = [];
    }
}
