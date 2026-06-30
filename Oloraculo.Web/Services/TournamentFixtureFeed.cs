namespace Oloraculo.Web.Services;

public interface ITournamentFixtureSource
{
    Task<TournamentFixtureFeedResult> FetchTournamentFixturesAsync(CancellationToken ct = default);
}

public sealed class TournamentFixtureFeedResult
{
    public bool IsConfigured { get; init; }
    public IReadOnlyList<TournamentFixtureFeedRow> Fixtures { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed class TournamentFixtureFeedRow
{
    public required string ExternalFixtureId { get; init; }
    public string? Round { get; init; }
    public DateTimeOffset? KickoffUtc { get; init; }
    public string? Venue { get; init; }
    public string? City { get; init; }
    public string? Status { get; init; }
    public string? HomeTeamId { get; init; }
    public string? AwayTeamId { get; init; }
    public int? HomeGoals { get; init; }
    public int? AwayGoals { get; init; }
    public int? HomePenaltyGoals { get; init; }
    public int? AwayPenaltyGoals { get; init; }
    public string? WinnerTeamId { get; init; }
    public bool IsFinished { get; init; }
}
