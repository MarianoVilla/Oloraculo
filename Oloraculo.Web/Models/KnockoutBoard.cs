using Oloraculo.Web.Probability;
using Oloraculo.Web.Services.Simulation;

namespace Oloraculo.Web.Models;

public enum ParticipantResolution
{
    Tbd,
    Projected,
    Confirmed
}

public sealed class KnockoutBoard
{
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Source { get; init; } = "API-Football";
    public bool SourceRefreshSucceeded { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<KnockoutMatchView> Matches { get; init; } = [];
}

public sealed class KnockoutMatchView
{
    public int MatchNumber { get; init; }
    public KnockoutStageEnum Stage { get; init; }
    public DateTimeOffset? KickoffUtc { get; init; }
    public string? Venue { get; init; }
    public string? City { get; init; }
    public string? Status { get; init; }
    public string? HomeTeamId { get; init; }
    public string? HomeTeamName { get; init; }
    public ParticipantResolution HomeResolution { get; init; }
    public string? AwayTeamId { get; init; }
    public string? AwayTeamName { get; init; }
    public ParticipantResolution AwayResolution { get; init; }
    public int? PredictedHomeGoals { get; init; }
    public int? PredictedAwayGoals { get; init; }
    public string? PredictedWinnerTeamId { get; init; }
    public OutcomeProbabilities? Probabilities { get; init; }
    public DateTimeOffset? PredictionCreatedAt { get; init; }
    public bool PredictionUnavailable { get; init; }
    public bool IsPlayed { get; init; }
    public int? HomeGoals { get; init; }
    public int? AwayGoals { get; init; }
    public int? HomePenaltyGoals { get; init; }
    public int? AwayPenaltyGoals { get; init; }
    public string? WinnerTeamId { get; init; }
}

public sealed class KnockoutRefreshReport
{
    public required KnockoutBoard Board { get; init; }
    public int FixturesFetched { get; init; }
    public int FixturesApplied { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}
