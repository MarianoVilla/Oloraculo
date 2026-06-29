using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.Models;
using Oloraculo.Web.Services;

namespace Oloraculo.Web.Tests;

public class BracketSnapshotServiceTests : TestFixtures
{
    [Fact]
    public async Task SnapshotService_SavesAndLoadsBracketSnapshotsWithChildMatchSnapshots()
    {
        await using var db = await NewDb();
        var prediction = Prediction(4, "Final", .6, .2, .2);
        prediction.FixtureId = KnockoutBracketService.FixtureId(73);
        prediction.HomeTeamId = "a";
        prediction.AwayTeamId = "b";
        var service = new SnapshotService(db);
        var projection = new BracketProjection
        {
            GeneratedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            ModelName = "Cuadro",
            InputSummaryHash = "hash",
            Ties =
            [
                new BracketTieProjection
                {
                    TieId = 73,
                    FixtureId = prediction.FixtureId,
                    StageLabel = "16avos",
                    HomeTeamId = "a",
                    AwayTeamId = "b",
                    Prediction = prediction
                }
            ]
        };

        var snapshot = await service.SaveBracketAsync(projection);
        var summaries = await service.BracketSnapshotsAsync();
        var loaded = await service.LoadBracketSnapshotAsync(snapshot.Id);

        Assert.Single(summaries);
        Assert.Equal(1, summaries.Single().TieCount);
        Assert.True(loaded.IsValid);
        Assert.Equal(73, loaded.Projection!.Ties.Single().TieId);
        Assert.Equal(1, await db.Snapshots.CountAsync(s => s.Kind == "bracket"));
        Assert.Equal(1, await db.Snapshots.CountAsync(s => s.Kind == "match" && s.BatchId == snapshot.Id));
    }
}
