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
            ModelName = "Oráculo final",
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
    public async Task Evaluation_RecordManualResultCreatesEvaluationWhenSnapshotExists()
    {
        await using var db = await NewDb();
        var fixture = new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b" };
        db.Fixtures.Add(fixture);
        db.Snapshots.Add(Snapshot("f1", DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        await db.SaveChangesAsync();

        var report = await new EvaluationService(db).RecordManualResultAsync(fixture, 2, 1);

        Assert.True(report.Saved);
        Assert.True(report.Evaluated);
        Assert.True(fixture.IsPlayed);
        Assert.Equal(2, fixture.HomeGoals);
        Assert.Equal(1, fixture.AwayGoals);
        Assert.Equal(1, await db.Results.CountAsync(r => r.Source == "manual"));
        Assert.Equal(1, await db.Evaluations.CountAsync(e => e.FixtureId == "f1"));
    }

    [Fact]
    public async Task Evaluation_RecordManualResultWithoutSnapshotStillStoresResult()
    {
        await using var db = await NewDb();
        var fixture = new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b" };
        db.Fixtures.Add(fixture);
        await db.SaveChangesAsync();

        var report = await new EvaluationService(db).RecordManualResultAsync(fixture, 1, 1);

        Assert.True(report.Saved);
        Assert.False(report.Evaluated);
        Assert.True(fixture.IsPlayed);
        Assert.Equal(1, fixture.HomeGoals);
        Assert.Equal(1, fixture.AwayGoals);
        Assert.Equal(1, await db.Results.CountAsync(r => r.Source == "manual"));
        Assert.Equal(0, await db.Evaluations.CountAsync());
    }

    [Fact]
    public async Task Evaluation_RecordManualResultUpdatesExistingEvaluationScoreOnly()
    {
        await using var db = await NewDb();
        var fixture = new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b" };
        var predictedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        db.Fixtures.Add(fixture);
        db.Snapshots.Add(Snapshot("f1", predictedAt));
        await db.SaveChangesAsync();
        var service = new EvaluationService(db);

        await service.RecordManualResultAsync(fixture, 2, 1);
        var original = await db.Evaluations.SingleAsync(e => e.FixtureId == "f1");
        var originalId = original.Id;
        var originalModelName = original.ModelName;
        var originalHomeWin = original.HomeWin;
        var originalDraw = original.Draw;
        var originalAwayWin = original.AwayWin;
        var originalPredictedAt = original.PredictedAt;
        var originalBrier = original.BrierScore;

        var report = await service.RecordManualResultAsync(fixture, 0, 1);

        var updated = await db.Evaluations.SingleAsync(e => e.FixtureId == "f1");
        Assert.True(report.Evaluated);
        Assert.Equal(originalId, updated.Id);
        Assert.Equal(originalModelName, updated.ModelName);
        Assert.Equal(originalHomeWin, updated.HomeWin);
        Assert.Equal(originalDraw, updated.Draw);
        Assert.Equal(originalAwayWin, updated.AwayWin);
        Assert.Equal(originalPredictedAt, updated.PredictedAt);
        Assert.Equal(0, updated.HomeGoals);
        Assert.Equal(1, updated.AwayGoals);
        Assert.Equal("Away", updated.Actual);
        Assert.False(updated.TopPickCorrect);
        Assert.NotEqual(originalBrier, updated.BrierScore);
        var predicted = new OutcomeProbabilities(originalHomeWin, originalDraw, originalAwayWin).Normalize();
        Assert.Equal(ProbabilityHelper.BrierScore(predicted, "Away"), updated.BrierScore, 12);
        Assert.Equal(ProbabilityHelper.RankedProbabilityScore(predicted, "Away"), updated.RankedProbabilityScore, 12);
        Assert.Equal(ProbabilityHelper.LogLoss(predicted, "Away"), updated.LogLoss, 12);
    }

    [Fact]
    public async Task Evaluation_ClearResultRemovesAssociatedEvaluation()
    {
        await using var db = await NewDb();
        var fixture = new Fixture { Id = "f1", Group = "A", HomeTeamId = "a", AwayTeamId = "b" };
        db.Fixtures.Add(fixture);
        db.Snapshots.Add(Snapshot("f1"));
        await db.SaveChangesAsync();
        var service = new EvaluationService(db);
        await service.RecordManualResultAsync(fixture, 2, 1);

        var cleared = await service.ClearResultAsync(fixture);

        Assert.True(cleared);
        Assert.False(fixture.IsPlayed);
        Assert.Null(fixture.HomeGoals);
        Assert.Null(fixture.AwayGoals);
        Assert.Equal(0, await db.Results.CountAsync());
        Assert.Equal(0, await db.Evaluations.CountAsync());
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
        ModelName = "Oráculo final",
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
        ModelName = "Oráculo final",
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
