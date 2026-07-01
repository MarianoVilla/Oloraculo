using Oloraculo.Web.Services.Simulation;

namespace Oloraculo.Web.Models;

public class KnockoutMatch
{
    public int MatchNumber { get; set; }
    public KnockoutStageEnum Stage { get; set; }
    public string? ExternalFixtureId { get; set; }
    public DateTimeOffset? KickoffUtc { get; set; }
    public string? Venue { get; set; }
    public string? City { get; set; }
    public string? Status { get; set; }
    public string? ConfirmedHomeTeamId { get; set; }
    public string? ConfirmedAwayTeamId { get; set; }
    public int? HomeGoals { get; set; }
    public int? AwayGoals { get; set; }
    public int? HomePenaltyGoals { get; set; }
    public int? AwayPenaltyGoals { get; set; }
    public string? WinnerTeamId { get; set; }
    public bool IsPlayed { get; set; }
    public string Source { get; set; } = "official-bracket";
    public DateTimeOffset? SourceUpdatedAt { get; set; }

    public string FixtureId => $"wc2026:match:{MatchNumber}";
}
