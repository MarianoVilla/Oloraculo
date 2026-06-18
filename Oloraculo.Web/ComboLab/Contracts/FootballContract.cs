namespace Oloraculo.Web.ComboLab.Contracts
{
    public enum FootballMarketType
    {
        Moneyline,
        Spread,
        TeamTotal,
        MatchTotal,
        BothTeamsToScore,
        ExactScore
    }

    public enum ContractSelection
    {
        Home,
        Draw,
        Away,
        NotHome,
        NotDraw,
        NotAway,
        Over,
        Under,
        Yes,
        No
    }

    public enum TeamSide
    {
        Home,
        Away
    }

    public enum PeriodScope
    {
        Unknown,
        Regulation90
    }

    public sealed record PolymarketContractIdentity(
        string? EventSlug = null,
        string? EventId = null,
        string? MarketId = null,
        string? ConditionId = null,
        string? TokenId = null,
        string? OutcomeSide = null,
        string? Question = null,
        string? ResolutionRules = null);

    public sealed record FootballContract
    {
        public required string FixtureId { get; init; }
        public string? HomeTeamId { get; init; }
        public string? AwayTeamId { get; init; }
        public PolymarketContractIdentity Identity { get; init; } = new();
        public required FootballMarketType MarketType { get; init; }
        public required ContractSelection Selection { get; init; }
        public PeriodScope Period { get; init; } = PeriodScope.Regulation90;
        public TeamSide? Team { get; init; }
        public decimal? Line { get; init; }
        public int? ExactHomeGoals { get; init; }
        public int? ExactAwayGoals { get; init; }
        public string Label { get; init; } = string.Empty;
    }
}
