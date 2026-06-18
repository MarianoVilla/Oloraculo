namespace Oloraculo.Web.WorldCup.Burden
{
    public enum WorldCupVenueExposure
    {
        Unknown,
        OpenAir,
        RetractableRoof,
        ClimateControlled
    }

    public enum WorldCupBurdenConfidence
    {
        Low,
        Medium,
        High
    }

    public sealed record WorldCupFixtureBurdenInput
    {
        public required string FixtureId { get; init; }
        public string? Venue { get; init; }
        public string? City { get; init; }
        public DateTimeOffset? KickoffUtc { get; init; }
        public double? ExpectedWbgtC { get; init; }
        public int? LocalKickoffHour { get; init; }
        public WorldCupVenueExposure VenueExposure { get; init; } = WorldCupVenueExposure.Unknown;
        public int? AltitudeMeters { get; init; }
        public double? PriorTravelKm { get; init; }
        public int? EastwardTimeZoneShiftHours { get; init; }
        public double? RestDaysBefore { get; init; }
        public string Source { get; init; } = "operator-input";
    }

    public sealed record WorldCupFixtureBurdenScore
    {
        public required string FixtureId { get; init; }
        public double HeatScore { get; init; }
        public double TravelScore { get; init; }
        public double RestScore { get; init; }
        public double AltitudeScore { get; init; }
        public double CompositeScore { get; init; }
        public WorldCupBurdenConfidence Confidence { get; init; }
        public IReadOnlyList<string> Blockers { get; init; } = [];
        public string Source { get; init; } = "operator-input";

        public bool IsUsable => Blockers.Count == 0 && Confidence != WorldCupBurdenConfidence.Low;
    }

    public sealed record WorldCupBurdenCoverageSnapshot(
        int TotalFixtures,
        int FixturesWithKickoff,
        int FixturesWithVenue,
        int FixturesWithCity,
        int BurdenReadyFixtures,
        IReadOnlyDictionary<string, int> BlockerCounts)
    {
        public bool HasAnyFixtures => TotalFixtures > 0;
        public double ReadyRate => TotalFixtures == 0 ? 0 : BurdenReadyFixtures / (double)TotalFixtures;
    }
}
