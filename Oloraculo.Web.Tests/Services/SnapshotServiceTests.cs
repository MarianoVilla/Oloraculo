using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Models.ApiFootballModels;
using Oloraculo.Web.Models.CsvModels;
using Oloraculo.Web.Predictors;
using Oloraculo.Web.Probability;
using Oloraculo.Web.Services;
using Oloraculo.Web.Services.Simulation;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Oloraculo.Web.Tests;

public class SnapshotServiceTests : TestFixtures
{
    [Fact]
    public async Task SnapshotService_SavesTournamentSnapshotAgainstLegacyNonNullProbabilityColumns()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE "Snapshots" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Snapshots" PRIMARY KEY AUTOINCREMENT,
                    "Kind" TEXT NOT NULL,
                    "FixtureId" TEXT NULL,
                    "ModelName" TEXT NOT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    "InputSummaryHash" TEXT NOT NULL,
                    "PayloadJson" TEXT NOT NULL,
                    "Explanation" TEXT NOT NULL,
                    "HomeWin" REAL NOT NULL,
                    "Draw" REAL NOT NULL,
                    "AwayWin" REAL NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<OloraculoDbContext>().UseSqlite(connection).Options;
        await using var db = new OloraculoDbContext(options);

        var snapshot = await new SnapshotService(db).SaveTournamentAsync(new TournamentProjection
        {
            ModelName = "Final",
            InputSummaryHash = "hash",
            Simulations = 1,
            Teams = []
        });

        Assert.Equal("tournament", snapshot.Kind);
        Assert.Equal(0, snapshot.AwayWin);
    }

    [Fact]
    public async Task SnapshotService_SavesMatchSnapshotsInBulk()
    {
        await using var db = await NewDb();
        var first = Prediction(4, "Final", .6, .2, .2);
        var second = Prediction(4, "Final", .2, .3, .5);
        first.FixtureId = "f1";
        second.FixtureId = "f2";
        var service = new SnapshotService(db);

        var snapshots = await service.SaveMatchesAsync([first, second]);

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(["f1", "f2"], snapshots.Select(snapshot => snapshot.FixtureId));
        Assert.Equal(2, await db.Snapshots.CountAsync(snapshot => snapshot.Kind == "match"));
    }

    [Fact]
    public async Task SnapshotService_SavesFullFixtureAsParentBatchAndMatchChildren()
    {
        await using var db = await NewDb();
        var service = new SnapshotService(db);
        var first = Prediction(4, "Final", .6, .2, .2);
        var second = Prediction(4, "Final", .2, .3, .5);
        first.FixtureId = "f1";
        second.FixtureId = "f2";

        var batch = await service.SaveFullFixtureAsync([first, second]);

        Assert.Equal("full-fixture", batch.Kind);
        Assert.Null(batch.BatchId);
        var children = await db.Snapshots
            .Where(snapshot => snapshot.Kind == "match" && snapshot.BatchId == batch.Id)
            .OrderBy(snapshot => snapshot.FixtureId)
            .ToListAsync();
        Assert.Equal(["f1", "f2"], children.Select(snapshot => snapshot.FixtureId));
        Assert.Equal(1, await db.Snapshots.CountAsync(snapshot => snapshot.Kind == "full-fixture"));
        Assert.Equal(2, await db.Snapshots.CountAsync(snapshot => snapshot.Kind == "match"));
    }

    [Fact]
    public async Task SnapshotService_LoadsFullFixtureBatchAsPredictionResults()
    {
        await using var db = await NewDb();
        db.Teams.AddRange(
            new Team { Id = "a", Name = "Alpha" },
            new Team { Id = "b", Name = "Beta" },
            new Team { Id = "c", Name = "Gamma" },
            new Team { Id = "d", Name = "Delta" });
        db.Fixtures.AddRange(
            new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b" },
            new Fixture { Id = "f2", Group = "A", HomeTeamId = "c", AwayTeamId = "d" });
        await db.SaveChangesAsync();
        var first = Prediction(4, "Final", .6, .2, .2);
        var second = Prediction(4, "Final", .2, .3, .5);
        first.FixtureId = "f1";
        first.HomeTeamId = "a";
        first.AwayTeamId = "b";
        first.ExpectedHomeGoals = 1.4;
        first.ExpectedAwayGoals = .9;
        first.MostLikelyScore = (1, 0);
        second.FixtureId = "f2";
        second.HomeTeamId = "c";
        second.AwayTeamId = "d";
        var service = new SnapshotService(db);
        var batch = await service.SaveFullFixtureAsync([first, second]);

        var result = await service.LoadFullFixtureSnapshotAsync(batch.Id);

        Assert.True(result.IsValid);
        Assert.Equal(["f1", "f2"], result.Predictions.Select(prediction => prediction.Fixture.Id));
        Assert.Equal("Alpha", result.Predictions[0].HomeTeamName);
        AssertPredictionEqual(first, result.Predictions[0].BestPrediction);
        AssertPredictionEqual(second, result.Predictions[1].BestPrediction);
    }

    [Fact]
    public async Task SnapshotService_ListsMatchSnapshotsNewestFirstAndLoadsLatest()
    {
        await using var db = await NewDb();
        db.Teams.AddRange(new Team { Id = "a", Name = "A" }, new Team { Id = "b", Name = "B" });
        db.Fixtures.Add(new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b" });
        await db.SaveChangesAsync();
        var oldPrediction = Prediction(4, "Old", .6, .2, .2);
        var newPrediction = Prediction(4, "New", .2, .3, .5);
        oldPrediction.FixtureId = "f1";
        newPrediction.FixtureId = "f1";
        var service = new SnapshotService(db);
        var oldSnapshot = await service.SaveMatchAsync(oldPrediction);
        var newSnapshot = await service.SaveMatchAsync(newPrediction);

        var summaries = await service.MatchSnapshotsAsync("f1");
        var latest = await service.LoadLatestMatchSnapshotAsync("f1");

        Assert.Equal([newSnapshot.Id, oldSnapshot.Id], summaries.Select(summary => summary.Id));
        Assert.True(latest.IsValid);
        AssertPredictionEqual(newPrediction, latest.Prediction!.BestPrediction);
    }

    [Fact]
    public async Task SnapshotService_LoadsLatestMatchSnapshotAtOrBeforeCutoff()
    {
        await using var db = await NewDb();
        db.Teams.AddRange(new Team { Id = "a", Name = "A" }, new Team { Id = "b", Name = "B" });
        db.Fixtures.Add(new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b" });
        await db.SaveChangesAsync();
        var service = new SnapshotService(db);
        var cutoff = DateTimeOffset.Parse("2026-06-11T19:00:00Z");
        var oldPrediction = Prediction(4, "Old pre-game", .6, .2, .2);
        var latestPreGamePrediction = Prediction(4, "Latest pre-game", .2, .6, .2);
        var postGamePrediction = Prediction(4, "Post-game", .1, .1, .8);
        oldPrediction.FixtureId = "f1";
        latestPreGamePrediction.FixtureId = "f1";
        postGamePrediction.FixtureId = "f1";

        var oldSnapshot = await service.SaveMatchAsync(oldPrediction);
        oldSnapshot.CreatedAt = cutoff.AddHours(-2);
        var latestPreGameSnapshot = await service.SaveMatchAsync(latestPreGamePrediction);
        latestPreGameSnapshot.CreatedAt = cutoff.AddMinutes(-1);
        var postGameSnapshot = await service.SaveMatchAsync(postGamePrediction);
        postGameSnapshot.CreatedAt = cutoff.AddMinutes(1);
        await db.SaveChangesAsync();

        var loaded = await service.LoadLatestMatchSnapshotAtOrBeforeAsync("f1", cutoff);

        Assert.True(loaded.IsValid);
        Assert.Equal(latestPreGameSnapshot.Id, (await service.MatchSnapshotsAsync("f1"))
            .Single(summary => summary.ModelName == "Latest pre-game").Id);
        Assert.Equal("Latest pre-game", loaded.Prediction!.BestPrediction.PredictorName);
        Assert.NotEqual(postGameSnapshot.Id, latestPreGameSnapshot.Id);
        Assert.NotEqual(oldSnapshot.Id, latestPreGameSnapshot.Id);
    }

    [Fact]
    public async Task SnapshotService_LoadLatestMatchSnapshotAtOrBeforeCutoffReturnsEmptyWhenOnlyPostCutoffSnapshotsExist()
    {
        await using var db = await NewDb();
        db.Teams.AddRange(new Team { Id = "a", Name = "A" }, new Team { Id = "b", Name = "B" });
        db.Fixtures.Add(new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b" });
        await db.SaveChangesAsync();
        var service = new SnapshotService(db);
        var cutoff = DateTimeOffset.Parse("2026-06-11T19:00:00Z");
        var prediction = Prediction(4, "Post-game", .1, .1, .8);
        prediction.FixtureId = "f1";

        var snapshot = await service.SaveMatchAsync(prediction);
        snapshot.CreatedAt = cutoff.AddSeconds(1);
        await db.SaveChangesAsync();

        var loaded = await service.LoadLatestMatchSnapshotAtOrBeforeAsync("f1", cutoff);

        Assert.False(loaded.IsValid);
        Assert.Null(loaded.Prediction);
    }

    [Fact]
    public async Task SnapshotService_LoadsLegacyMatchSnapshotFromColumnsAndCurrentFixture()
    {
        await using var db = await NewDb();
        db.Teams.AddRange(new Team { Id = "a", Name = "A" }, new Team { Id = "b", Name = "B" });
        db.Fixtures.Add(new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b" });
        db.Snapshots.Add(new PredictionSnapshot
        {
            Kind = "match",
            FixtureId = "f1",
            ModelName = "Legacy",
            InputSummaryHash = "legacy",
            PayloadJson = "{}",
            Explanation = "legacy prediction",
            HomeWin = .6,
            Draw = .2,
            AwayWin = .2
        });
        await db.SaveChangesAsync();
        var service = new SnapshotService(db);

        var loaded = await service.LoadLatestMatchSnapshotAsync("f1");

        Assert.True(loaded.IsValid);
        Assert.Equal("Legacy", loaded.Prediction!.BestPrediction.PredictorName);
        Assert.Equal("a", loaded.Prediction.BestPrediction.HomeTeamId);
        Assert.Equal(.6, loaded.Prediction.BestPrediction.Outcome.HomeWin);
    }

    [Fact]
    public async Task SnapshotService_SurfacesMalformedFullFixtureSnapshots()
    {
        await using var db = await NewDb();
        db.Snapshots.Add(new PredictionSnapshot
        {
            Kind = "full-fixture",
            ModelName = "Final",
            InputSummaryHash = "bad-fixture",
            PayloadJson = "not json",
            Explanation = "bad",
            HomeWin = 0,
            Draw = 0,
            AwayWin = 0
        });
        await db.SaveChangesAsync();
        var service = new SnapshotService(db);

        var summaries = await service.FullFixtureSnapshotsAsync();
        var loaded = await service.LoadFullFixtureSnapshotAsync(summaries.Single().Id);

        Assert.False(summaries.Single().IsValid);
        Assert.False(loaded.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Error));
    }

    [Fact]
    public async Task SnapshotService_ListsTournamentSnapshotsNewestFirstAndExcludesMatches()
    {
        await using var db = await NewDb();
        var service = new SnapshotService(db);
        var oldSnapshot = await service.SaveTournamentAsync(TournamentProjection("old-hash", 100, DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        db.Snapshots.Add(new PredictionSnapshot
        {
            Kind = "match",
            FixtureId = "f1",
            ModelName = "Match",
            CreatedAt = DateTimeOffset.Parse("2026-01-03T00:00:00Z"),
            InputSummaryHash = "match-hash",
            PayloadJson = "{}",
            Explanation = "match",
            HomeWin = .4,
            Draw = .3,
            AwayWin = .3
        });
        await db.SaveChangesAsync();
        var newSnapshot = await service.SaveTournamentAsync(TournamentProjection("new-hash", 200, DateTimeOffset.Parse("2026-01-02T00:00:00Z")));

        var snapshots = await service.TournamentSnapshotsAsync();

        Assert.Equal([newSnapshot.Id, oldSnapshot.Id], snapshots.Select(s => s.Id));
        Assert.Equal(200, snapshots[0].Simulations);
        Assert.All(snapshots, s => Assert.True(s.IsValid));
    }

    [Fact]
    public async Task SnapshotService_LoadsTournamentSnapshotPayload()
    {
        await using var db = await NewDb();
        var service = new SnapshotService(db);
        var snapshot = await service.SaveTournamentAsync(TournamentProjection("hash", 123, DateTimeOffset.Parse("2026-01-01T00:00:00Z")));

        var result = await service.LoadTournamentSnapshotAsync(snapshot.Id);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Projection);
        Assert.Equal(123, result.Projection.Simulations);
        Assert.Equal("argentina", result.Projection.Teams.Single().TeamId);
        Assert.Equal(.42, result.Projection.Teams.Single().WinTournament);
    }

    [Fact]
    public async Task SnapshotService_SurfacesMalformedTournamentSnapshotPayloads()
    {
        await using var db = await NewDb();
        db.Snapshots.Add(new PredictionSnapshot
        {
            Kind = "tournament",
            ModelName = "Final",
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            InputSummaryHash = "bad-hash",
            PayloadJson = "not json",
            Explanation = "bad",
            HomeWin = 0,
            Draw = 0,
            AwayWin = 0
        });
        await db.SaveChangesAsync();
        var service = new SnapshotService(db);

        var snapshots = await service.TournamentSnapshotsAsync();
        var result = await service.LoadTournamentSnapshotAsync(snapshots.Single().Id);

        Assert.False(snapshots.Single().IsValid);
        Assert.Null(snapshots.Single().Simulations);
        Assert.False(result.IsValid);
        Assert.Null(result.Projection);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private static TournamentProjection TournamentProjection(string hash, int simulations, DateTimeOffset generatedAt) => new()
    {
        GeneratedAt = generatedAt,
        Simulations = simulations,
        ModelName = "Final",
        InputSummaryHash = hash,
        Teams =
        [
            new TeamTournamentProbability
            {
                TeamId = "argentina",
                Group = "A",
                Qualify = .8,
                ReachRoundOf16 = .7,
                ReachQuarterFinal = .6,
                ReachSemiFinal = .5,
                ReachFinal = .45,
                WinTournament = .42
            }
            ]
    };

}
