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

public class EvaluationServiceTests : TestFixtures
{
    [Fact]
    public async Task Evaluation_StoresFixtureLevelKnownResult()
    {
        await using var db = await NewDb();
        var fixture = new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b" };
        db.Teams.AddRange(new Team { Id = "a", Name = "A" }, new Team { Id = "b", Name = "B" });
        db.Fixtures.Add(fixture);
        db.Snapshots.Add(new PredictionSnapshot
        {
            Kind = "match",
            FixtureId = "f1",
            ModelName = "Final edge model",
            InputSummaryHash = "hash",
            PayloadJson = "{}",
            Explanation = "test",
            HomeWin = .6,
            Draw = .2,
            AwayWin = .2
        });
        await db.SaveChangesAsync();

        var count = await new EvaluationService(db).EvaluateLatestSnapshotAsync(fixture, 2, 1);

        Assert.Equal(1, count);
        Assert.True(fixture.IsPlayed);
        Assert.Equal(2, fixture.HomeGoals);
        Assert.Equal(1, fixture.AwayGoals);
    }

    [Fact]
    public async Task Evaluation_BulkEvaluatesPlayedFixturesWithPriorSnapshots()
    {
        await using var db = await NewDb();
        db.Fixtures.Add(PlayedFixture("f1", 2, 1));
        db.Snapshots.Add(Snapshot("f1", DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        await db.SaveChangesAsync();

        var report = await new EvaluationService(db).EvaluateUnevaluatedPlayedFixturesAsync();

        Assert.Equal(1, report.Evaluated);
        Assert.Equal(0, report.SkippedAlreadyEvaluated);
        Assert.Equal(0, report.SkippedWithoutSnapshot);
        Assert.Equal(1, await db.Evaluations.CountAsync(e => e.FixtureId == "f1"));
    }

    [Fact]
    public async Task Evaluation_BulkSkipsFixturesWithoutScores()
    {
        await using var db = await NewDb();
        db.Fixtures.Add(new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b", IsPlayed = true });
        db.Snapshots.Add(Snapshot("f1"));
        await db.SaveChangesAsync();

        var report = await new EvaluationService(db).EvaluateUnevaluatedPlayedFixturesAsync();

        Assert.Equal(0, report.Evaluated);
        Assert.Equal(0, await db.Evaluations.CountAsync());
    }

    [Fact]
    public async Task Evaluation_BulkSkipsAlreadyEvaluatedFixtures()
    {
        await using var db = await NewDb();
        db.Fixtures.Add(PlayedFixture("f1", 2, 1));
        db.Snapshots.Add(Snapshot("f1"));
        db.Evaluations.Add(Evaluation("f1"));
        await db.SaveChangesAsync();

        var report = await new EvaluationService(db).EvaluateUnevaluatedPlayedFixturesAsync();

        Assert.Equal(0, report.Evaluated);
        Assert.Equal(1, report.SkippedAlreadyEvaluated);
        Assert.Equal(1, await db.Evaluations.CountAsync(e => e.FixtureId == "f1"));
    }

    [Fact]
    public async Task Evaluation_BulkSkipsPlayedFixturesWithoutPriorSnapshots()
    {
        await using var db = await NewDb();
        db.Fixtures.Add(PlayedFixture("f1", 2, 1));
        await db.SaveChangesAsync();

        var report = await new EvaluationService(db).EvaluateUnevaluatedPlayedFixturesAsync();

        Assert.Equal(0, report.Evaluated);
        Assert.Equal(1, report.SkippedWithoutSnapshot);
        Assert.Equal(0, await db.Evaluations.CountAsync());
    }

    [Fact]
    public async Task Evaluation_BulkEvaluationIsIdempotent()
    {
        await using var db = await NewDb();
        db.Fixtures.Add(PlayedFixture("f1", 2, 1));
        db.Snapshots.Add(Snapshot("f1"));
        await db.SaveChangesAsync();
        var service = new EvaluationService(db);

        var first = await service.EvaluateUnevaluatedPlayedFixturesAsync();
        var second = await service.EvaluateUnevaluatedPlayedFixturesAsync();

        Assert.Equal(1, first.Evaluated);
        Assert.Equal(0, second.Evaluated);
        Assert.Equal(1, second.SkippedAlreadyEvaluated);
        Assert.Equal(1, await db.Evaluations.CountAsync(e => e.FixtureId == "f1"));
    }

    private static Fixture PlayedFixture(string id, int homeGoals, int awayGoals) => new()
    {
        Id = id,
        Group = "A",
        HomeTeamId = "a",
        AwayTeamId = "b",
        IsPlayed = true,
        HomeGoals = homeGoals,
        AwayGoals = awayGoals,
        NeutralVenue = true
    };

    private static PredictionSnapshot Snapshot(string fixtureId, DateTimeOffset? createdAt = null) => new()
    {
        Kind = "match",
        FixtureId = fixtureId,
        ModelName = "Final edge model",
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        InputSummaryHash = "hash",
        PayloadJson = "{}",
        Explanation = "test",
        HomeWin = .6,
        Draw = .2,
        AwayWin = .2
    };

    private static PredictionEvaluation Evaluation(string fixtureId) => new()
    {
        ModelName = "Final edge model",
        FixtureId = fixtureId,
        HomeTeamId = "a",
        AwayTeamId = "b",
        HomeGoals = 2,
        AwayGoals = 1,
        HomeWin = .6,
        Draw = .2,
        AwayWin = .2,
        Actual = "Home",
        BrierScore = 0,
        RankedProbabilityScore = 0,
        LogLoss = 0,
        TopPickCorrect = true,
        PredictedAt = DateTimeOffset.UtcNow
    };

}
