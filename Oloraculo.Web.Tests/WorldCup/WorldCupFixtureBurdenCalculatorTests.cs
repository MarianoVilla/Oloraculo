using Oloraculo.Web.Models;
using Oloraculo.Web.WorldCup.Burden;

namespace Oloraculo.Web.Tests.WorldCup;

public class WorldCupFixtureBurdenCalculatorTests
{
    [Fact]
    public void Score_ProducesHighConfidenceCompositeWhenAllInputsExist()
    {
        var score = WorldCupFixtureBurdenCalculator.Score(new WorldCupFixtureBurdenInput
        {
            FixtureId = "fixture-france-boston",
            Venue = "Gillette Stadium",
            City = "Boston",
            KickoffUtc = new DateTimeOffset(2026, 6, 20, 19, 0, 0, TimeSpan.Zero),
            ExpectedWbgtC = 26.5,
            LocalKickoffHour = 15,
            VenueExposure = WorldCupVenueExposure.OpenAir,
            AltitudeMeters = 20,
            PriorTravelKm = 480,
            EastwardTimeZoneShiftHours = 1,
            RestDaysBefore = 4
        });

        Assert.Equal(WorldCupBurdenConfidence.High, score.Confidence);
        Assert.Empty(score.Blockers);
        Assert.True(score.HeatScore > 80);
        Assert.True(score.TravelScore > 0);
        Assert.True(score.RestScore > 0);
        Assert.True(score.CompositeScore > 40);
        Assert.True(score.IsUsable);
    }

    [Fact]
    public void Score_FailsClosedWithVisibleBlockersWhenEnvironmentIsMissing()
    {
        var score = WorldCupFixtureBurdenCalculator.Score(new WorldCupFixtureBurdenInput
        {
            FixtureId = "fixture-missing"
        });

        Assert.Equal(WorldCupBurdenConfidence.Low, score.Confidence);
        Assert.Contains("NO_KICKOFF_UTC", score.Blockers);
        Assert.Contains("NO_WBGT_ESTIMATE", score.Blockers);
        Assert.Contains("NO_ROOF_OR_OPEN_AIR_STATE", score.Blockers);
        Assert.False(score.IsUsable);
    }

    [Fact]
    public void ClimateControlledVenueCapsHeatBurden()
    {
        var openAir = WorldCupFixtureBurdenCalculator.HeatScore(28, 14, WorldCupVenueExposure.OpenAir);
        var climateControlled = WorldCupFixtureBurdenCalculator.HeatScore(28, 14, WorldCupVenueExposure.ClimateControlled);

        Assert.True(openAir > 80);
        Assert.True(climateControlled < 20);
    }

    [Fact]
    public void CoverageSnapshotCountsFixtureMetadataBlockers()
    {
        var snapshot = WorldCupBurdenCoverageService.Snapshot([
            new Fixture { Id = "a", KickoffUtc = DateTimeOffset.UtcNow, Venue = "Venue", City = "City" },
            new Fixture { Id = "b", Venue = "Venue" },
            new Fixture { Id = "c" }
        ]);

        Assert.Equal(3, snapshot.TotalFixtures);
        Assert.Equal(1, snapshot.FixturesWithKickoff);
        Assert.Equal(2, snapshot.FixturesWithVenue);
        Assert.Equal(1, snapshot.FixturesWithCity);
        Assert.Equal(1, snapshot.BurdenReadyFixtures);
        Assert.Equal(2, snapshot.BlockerCounts["NO_KICKOFF_UTC"]);
        Assert.Equal(2, snapshot.BlockerCounts["NO_CITY"]);
    }
}
