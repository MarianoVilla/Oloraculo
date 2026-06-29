using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Models;
using Oloraculo.Web.Services;

namespace Oloraculo.Web.Tests;

public class KnockoutBracketServiceTests : TestFixtures
{
    [Fact]
    public async Task BracketService_BuildsOfficialTiesAndAdvanceOdds()
    {
        await using var db = await ImportedDb();
        var service = BracketService(db);

        var projection = await service.BuildAsync();

        Assert.Equal(31, projection.Ties.Count);
        Assert.Equal(73, projection.Ties.First().TieId);
        Assert.Equal(104, projection.Ties.Last().TieId);
        Assert.All(
            projection.Ties.Where(t => t.HomeAdvanceProbability.HasValue && t.AwayAdvanceProbability.HasValue),
            tie => Assert.Equal(1, tie.HomeAdvanceProbability!.Value + tie.AwayAdvanceProbability!.Value, 6));
        Assert.Equal(31, await db.Fixtures.CountAsync(f => f.Id.StartsWith(KnockoutBracketService.KnockoutFixturePrefix)));
    }

    [Fact]
    public async Task BracketService_PropagatesActualKnockoutWinners()
    {
        await using var db = await ImportedDb();
        var service = BracketService(db);
        var firstProjection = await service.BuildAsync();
        var roundOf32 = firstProjection.Ties.Single(t => t.TieId == 73);
        var fixture = await db.Fixtures.SingleAsync(f => f.Id == KnockoutBracketService.FixtureId(73));
        fixture.IsPlayed = true;
        fixture.HomeGoals = 1;
        fixture.AwayGoals = 1;
        fixture.WinnerTeamId = fixture.HomeTeamId;
        await db.SaveChangesAsync();

        var updated = await service.BuildAsync();

        Assert.Equal(roundOf32.HomeTeamId, updated.Ties.Single(t => t.TieId == 73).ActualWinnerTeamId);
        Assert.Equal(roundOf32.HomeTeamId, updated.Ties.Single(t => t.TieId == 90).HomeTeamId);
    }

    [Fact]
    public async Task Import_PreservesSyntheticKnockoutFixtures()
    {
        await using var db = await NewDb();
        db.Fixtures.Add(new Fixture
        {
            Id = KnockoutBracketService.FixtureId(73),
            Group = "KO",
            HomeTeamId = "a",
            AwayTeamId = "b",
            Source = "test"
        });
        await db.SaveChangesAsync();

        await new CsvImportService(db, new TestEnvironment(WebProjectRoot())).ImportAllAsync();

        Assert.True(await db.Fixtures.AnyAsync(f => f.Id == KnockoutBracketService.FixtureId(73)));
        Assert.True(await db.Fixtures.AnyAsync(f => f.Id.StartsWith("grp:")));
    }

    private static KnockoutBracketService BracketService(OloraculoDbContext db)
    {
        var options = SimulationOptions(3, 7);
        return new KnockoutBracketService(db, new PredictionService(db, options));
    }
}
