using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Models;
using Oloraculo.Web.Services;

namespace Oloraculo.Web.Tests;

public class OfficialBracketServiceTests : TestFixtures
{
    [Fact]
    public async Task OfficialBracket_ReturnsPendingWhenNoMappedKnockoutFixturesExist()
    {
        await using var db = await NewDb();
        var service = Service(db);

        var projection = await service.BuildAsync();

        Assert.False(projection.HasOfficialFixtures);
        Assert.Empty(projection.Ties);
        Assert.Equal(OfficialBracketService.PendingMessage, projection.PendingMessage);
    }

    [Fact]
    public async Task OfficialBracket_IgnoresUnmappedLegacyKnockoutFixtures()
    {
        await using var db = await NewDb();
        db.Fixtures.Add(new Fixture { Id = "ko:73", Group = "KO", HomeTeamId = "germany", AwayTeamId = "bosnia-and-herzegovina" });
        await db.SaveChangesAsync();
        var service = Service(db);

        var projection = await service.BuildAsync();

        Assert.False(projection.HasOfficialFixtures);
        Assert.Empty(projection.Ties);
    }

    [Fact]
    public async Task OfficialBracket_UsesMappedOfficialFixtureAsCurrentPhase()
    {
        await using var db = await NewDb();
        db.Teams.AddRange(
            new Team { Id = "germany", Name = "Germany" },
            new Team { Id = "paraguay", Name = "Paraguay" });
        db.Ratings.AddRange(
            new Rating { TeamId = "germany", Type = RatingTypeEnum.Elo, Value = 1900, Source = "test", RatedAt = DateTimeOffset.UtcNow },
            new Rating { TeamId = "paraguay", Type = RatingTypeEnum.Elo, Value = 1650, Source = "test", RatedAt = DateTimeOffset.UtcNow },
            new Rating { TeamId = "germany", Type = RatingTypeEnum.Fifa, Value = 5, Source = "test", RatedAt = DateTimeOffset.UtcNow },
            new Rating { TeamId = "paraguay", Type = RatingTypeEnum.Fifa, Value = 40, Source = "test", RatedAt = DateTimeOffset.UtcNow });
        db.Fixtures.Add(new Fixture { Id = "ko:73", Group = "KO", HomeTeamId = "germany", AwayTeamId = "paraguay", NeutralVenue = true });
        db.ApiMappings.Add(new ApiMapping { LocalFixtureId = "ko:73", ExternalFixtureId = "730", UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var service = Service(db);

        var projection = await service.BuildAsync();
        var tie = Assert.Single(projection.Ties);

        Assert.True(projection.HasOfficialFixtures);
        Assert.Equal("ko:73", tie.FixtureId);
        Assert.True(tie.IsOfficialFixture);
        Assert.Equal("germany", tie.HomeTeamId);
        Assert.Equal("paraguay", tie.AwayTeamId);
        Assert.NotNull(tie.Prediction);
        Assert.NotEqual(tie.HomeAdvanceProbability, tie.Prediction!.Outcome.HomeWin);
    }

    private static OfficialBracketService Service(OloraculoDbContext db)
    {
        var options = Options.Create(new OloraculoConfig { GoalModelYearsWindow = 3, RecentResultCount = 8 });
        return new OfficialBracketService(db, new PredictionService(db, options));
    }
}
