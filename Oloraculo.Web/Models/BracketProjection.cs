using System.Text.Json.Serialization;

namespace Oloraculo.Web.Models
{
    public class BracketProjection
    {
        public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
        public string ModelName { get; set; } = "Cuadro";
        public string InputSummaryHash { get; set; } = "";
        public IReadOnlyList<BracketTieProjection> Ties { get; init; } = [];
    }

    public class BracketTieProjection
    {
        public int TieId { get; set; }
        public string FixtureId { get; set; } = "";
        public string Stage { get; set; } = "";
        public string StageLabel { get; set; } = "";
        public string HomeSlotLabel { get; set; } = "";
        public string AwaySlotLabel { get; set; } = "";
        public string? HomeTeamId { get; set; }
        public string? AwayTeamId { get; set; }
        public string? PredictedWinnerTeamId { get; set; }
        public double? HomeAdvanceProbability { get; set; }
        public double? AwayAdvanceProbability { get; set; }
        public int? PredictedHomeGoals { get; set; }
        public int? PredictedAwayGoals { get; set; }
        public string? PredictionModelName { get; set; }
        public bool IsPlayed { get; set; }
        public int? ActualHomeGoals { get; set; }
        public int? ActualAwayGoals { get; set; }
        public string? ActualWinnerTeamId { get; set; }
        public int? LatestSnapshotId { get; set; }
        public bool HasEvaluation { get; set; }
        public string? Error { get; set; }

        [JsonIgnore]
        public MatchPrediction? Prediction { get; set; }
    }
}
