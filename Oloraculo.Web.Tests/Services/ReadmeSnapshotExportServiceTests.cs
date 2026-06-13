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

public class ReadmeSnapshotExportServiceTests : TestFixtures
{
    [Fact]
    public void ReadmeExporter_ReplacesOnlyMarkedSnapshotBlock()
    {
        var readme = """
        # Title

        before
        <!-- oloraculo:snapshots:start -->
        stale
        <!-- oloraculo:snapshots:end -->
        after
        """;

        var updated = ReadmeSnapshotExportService.ReplaceSnapshotBlock(readme, "fresh");

        Assert.Contains("before", updated);
        Assert.Contains("fresh", updated);
        Assert.Contains("after", updated);
        Assert.DoesNotContain("stale", updated);
    }

    [Fact]
    public void ReadmeExporter_AppendsSnapshotBlockWhenMarkersAreMissing()
    {
        var updated = ReadmeSnapshotExportService.ReplaceSnapshotBlock("# Title", "fresh");

        Assert.Contains(ReadmeSnapshotExportService.StartMarker, updated);
        Assert.Contains("fresh", updated);
        Assert.Contains(ReadmeSnapshotExportService.EndMarker, updated);
    }

    [Fact]
    public void ReadmeExporter_InsertsSnapshotBlockAfterFirstTopLevelSection()
    {
        var readme = """
        # Holi.
        intro

        ## Video
        link

        # Oloraculo
        details
        """;

        var updated = ReadmeSnapshotExportService.ReplaceSnapshotBlock(readme, "fresh");

        Assert.True(updated.IndexOf("fresh", StringComparison.Ordinal) < updated.IndexOf("# Oloraculo", StringComparison.Ordinal));
        Assert.True(updated.IndexOf("fresh", StringComparison.Ordinal) > updated.IndexOf("## Video", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadmeExporter_RendersTournamentRowsByChampionProbability()
    {
        var projection = new TournamentProjection
        {
            GeneratedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            Simulations = 100,
            ModelName = "Final",
            InputSummaryHash = "hash",
            Teams =
            [
                new TeamTournamentProbability { TeamId = "france", Group = "D", Qualify = .7, ReachQuarterFinal = .4, ReachSemiFinal = .3, ReachFinal = .2, WinTournament = .1 },
                new TeamTournamentProbability { TeamId = "argentina", Group = "C", Qualify = .8, ReachQuarterFinal = .5, ReachSemiFinal = .4, ReachFinal = .3, WinTournament = .2 }
            ]
        };

        var rendered = ReadmeSnapshotExportService.RenderSnapshotBlock(projection, [], Names(), DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        Assert.True(rendered.IndexOf("Argentina", StringComparison.Ordinal) < rendered.IndexOf("France", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadmeExporter_RendersActualScoreForPlayedFixtures()
    {
        var rendered = ReadmeSnapshotExportService.RenderSnapshotBlock(
            TournamentProjection("hash", 100, DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            [PredictionResult(PlayedFixture())],
            Names(),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        Assert.Contains("**2-1**", rendered);
        Assert.Contains("Prediction:", rendered);
        Assert.Contains("FT", rendered);
    }

    [Fact]
    public void ReadmeExporter_RendersPredictionForUnplayedFixtures()
    {
        var rendered = ReadmeSnapshotExportService.RenderSnapshotBlock(
            TournamentProjection("hash", 100, DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            [PredictionResult(UnplayedFixture())],
            Names(),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        Assert.Contains("| <img", rendered);
        Assert.Contains("1-0", rendered);
        Assert.Contains("60", rendered);
        Assert.Contains("%", rendered);
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

    private static IReadOnlyDictionary<string, string> Names() => new Dictionary<string, string>
    {
        ["argentina"] = "Argentina",
        ["france"] = "France"
    };

    private static Fixture PlayedFixture() => new()
    {
        Id = "played",
        Group = "C",
        HomeTeamId = "argentina",
        AwayTeamId = "france",
        IsPlayed = true,
        HomeGoals = 2,
        AwayGoals = 1,
        Status = "FT"
    };

    private static Fixture UnplayedFixture() => new()
    {
        Id = "unplayed",
        Group = "C",
        HomeTeamId = "argentina",
        AwayTeamId = "france"
    };

    private static MatchPredictionResult PredictionResult(Fixture fixture) => new()
    {
        Fixture = fixture,
        HomeTeamName = "Argentina",
        AwayTeamName = "France",
        BestPrediction = new MatchPrediction
        {
            FixtureId = fixture.Id,
            HomeTeamId = fixture.HomeTeamId,
            AwayTeamId = fixture.AwayTeamId,
            PredictorName = "Oraculo final",
            PredictorPriority = 5,
            Outcome = new OutcomeProbabilities(.6, .25, .15),
            MostLikelyScore = (1, 0),
            Explanation = "test"
        }
    };

}
