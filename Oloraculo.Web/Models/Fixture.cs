namespace Oloraculo.Web.Models
{
    public class Fixture
    {
        public string Id { get; set; } = "";
        public string Group { get; set; } = "";
        public string HomeTeamId { get; set; } = "";
        public string AwayTeamId { get; set; } = "";
        public DateTimeOffset? KickoffUtc { get; set; }
        public string? Venue { get; set; }
        public string? City { get; set; }
        public string? Status { get; set; }
        public bool NeutralVenue { get; set; } = true;
        public bool IsPlayed { get; set; }
        public int? HomeGoals { get; set; }
        public int? AwayGoals { get; set; }
        public string Source { get; set; } = "derived";
        public static string GenerateFixtureId(string Group, string HomeTeamId, string AwayTeamId) => $"grp:{Group}:{HomeTeamId}:{AwayTeamId}";
    }
}
