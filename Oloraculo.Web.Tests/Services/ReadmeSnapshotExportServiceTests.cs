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
using Microsoft.Extensions.Logging.Abstractions;

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
    public async Task ReadmeExporter_EvaluatesPlayedFixturesBeforeCreatingFreshSnapshots()
    {
        var root = NewTempRoot();
        try
        {
            var webRoot = Path.Combine(root, "Oloraculo.Web");
            var dataRoot = Path.Combine(webRoot, "Data");
            Directory.CreateDirectory(dataRoot);
            File.WriteAllText(Path.Combine(root, "Oloraculo.sln"), "");
            File.WriteAllText(Path.Combine(root, "README.md"), "# Holi.\n\n# Oloraculo\n");
            foreach (var file in Directory.GetFiles(Path.Combine(WebProjectRoot(), "Data"), "*.csv"))
                File.Copy(file, Path.Combine(dataRoot, Path.GetFileName(file)));

            await using var db = await NewDb();
            var environment = new TestEnvironment(webRoot);
            var options = Options.Create(new OloraculoConfig
            {
                SimulationCount = 1,
                SimulationSeed = 1,
                RecentResultCount = 8,
                GoalModelYearsWindow = 3,
                ApiFootballBaseUrl = "https://api.test/",
                ApiFootballLeagueId = 1,
                ApiFootballSeason = 2026,
                OpenRouterBaseUrl = "https://openrouter.test/",
                AvailabilitySourceUrls = [],
                EloRefreshMaxLookbackDays = 0
            });
            var importer = new CsvImportService(db, environment);
            await importer.ImportAllAsync();
            var fixture = await db.Fixtures.OrderBy(f => f.Id).FirstAsync();
            fixture.IsPlayed = true;
            fixture.HomeGoals = 2;
            fixture.AwayGoals = 1;
            var oldCreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            db.Snapshots.Add(new PredictionSnapshot
            {
                Kind = "match",
                FixtureId = fixture.Id,
                ModelName = "Oráculo final",
                CreatedAt = oldCreatedAt,
                InputSummaryHash = "old",
                PayloadJson = "{}",
                Explanation = "old prediction",
                HomeWin = .6,
                Draw = .2,
                AwayWin = .2
            });
            await db.SaveChangesAsync();

            var availability = new AvailabilityNewsService(
                new HttpClient(new FakeHttpMessageHandler(new Dictionary<string, string>())) { BaseAddress = new Uri("https://openrouter.test/") },
                db,
                options);
            var api = new ApiFootballService(
                new HttpClient(new FakeHttpMessageHandler(new Dictionary<string, string>())) { BaseAddress = new Uri("https://api.test/") },
                db,
                options,
                availability);
            var snapshots = new SnapshotService(db);
            var prediction = new PredictionService(db, options);
            var exporter = new ReadmeSnapshotExportService(
                db,
                importer,
                new RankingRefreshService(new HttpClient(new FakeHttpMessageHandler(new Dictionary<string, string>())), environment, options),
                api,
                availability,
                prediction,
                new EvaluationService(db),
                snapshots,
                new SimulationService(db, prediction, snapshots, options),
                environment,
                NullLogger<ReadmeSnapshotExportService>.Instance);

            await exporter.ExportAsync();

            var evaluation = Assert.Single(await db.Evaluations.Where(e => e.FixtureId == fixture.Id).ToListAsync());
            Assert.Equal(oldCreatedAt, evaluation.PredictedAt);
            Assert.True(await db.Snapshots.CountAsync(s => s.Kind == "match" && s.FixtureId == fixture.Id) > 1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadmeExporter_RendersPlayedFixtureFromLatestPreKickoffSnapshot()
    {
        var root = NewTempRoot();
        try
        {
            await using var db = await NewDb();
            var setup = await PrepareReadmeExportAsync(root, db);
            var fixture = await db.Fixtures.OrderBy(f => f.Id).FirstAsync();
            fixture.KickoffUtc = DateTimeOffset.Parse("2026-06-11T19:00:00Z");
            fixture.IsPlayed = true;
            fixture.HomeGoals = 2;
            fixture.AwayGoals = 1;
            fixture.Status = "FT";

            var snapshots = new SnapshotService(db);
            var preGame = await snapshots.SaveMatchAsync(SnapshotPrediction(fixture, "Pre-game", .2, .7, .1, (0, 0)));
            preGame.CreatedAt = fixture.KickoffUtc.Value.AddMinutes(-10);
            var postGame = await snapshots.SaveMatchAsync(SnapshotPrediction(fixture, "Post-game", .9, .05, .05, (5, 0)));
            postGame.CreatedAt = fixture.KickoffUtc.Value.AddMinutes(10);
            db.AvailabilityClaims.Add(AvailabilityClaim(
                "Current Leak",
                fixture.HomeTeamId,
                fixture.HomeTeamId,
                AvailabilityClaimStatus.ConfirmedOutInjury,
                affectsPrediction: true));
            await db.SaveChangesAsync();

            var exporter = BuildReadmeExporter(db, setup.Importer, setup.Environment, setup.Options);

            await exporter.ExportAsync();

            var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"));
            var playedRow = readme.Split(Environment.NewLine).Single(line => line.Contains("**2-1**", StringComparison.Ordinal));
            Assert.Contains("**2-1**", readme);
            Assert.Contains("Prediction: 0-0", readme);
            Assert.Contains("Model: Pre-game", readme);
            Assert.Contains("70 %", readme);
            Assert.DoesNotContain("Prediction: 5-0", readme);
            Assert.DoesNotContain("Model: Post-game", readme);
            Assert.DoesNotContain("Current Leak", playedRow);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadmeExporter_RendersUnavailableWhenPlayedFixtureHasNoPreKickoffSnapshot()
    {
        var root = NewTempRoot();
        try
        {
            await using var db = await NewDb();
            var setup = await PrepareReadmeExportAsync(root, db);
            var fixture = await db.Fixtures.OrderBy(f => f.Id).FirstAsync();
            fixture.KickoffUtc = DateTimeOffset.Parse("2026-06-11T19:00:00Z");
            fixture.IsPlayed = true;
            fixture.HomeGoals = 2;
            fixture.AwayGoals = 1;
            fixture.Status = "FT";
            await db.SaveChangesAsync();

            var exporter = BuildReadmeExporter(db, setup.Importer, setup.Environment, setup.Options);

            await exporter.ExportAsync();

            var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"));
            Assert.Contains("**2-1** <br><sub>Prediction: unavailable</sub>", readme);
            Assert.Contains("No pre-game snapshot", readme);
            Assert.Contains("| - | - | - |", readme);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
            [
                PredictionResult(
                    PlayedFixture(),
                    expectedHomeGoals: 2.6,
                    expectedAwayGoals: .8,
                    mostLikelyScore: (1, 1))
            ],
            Names(),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        Assert.Contains("**2-1**", rendered);
        Assert.Contains("<sub>Prediction: 3-1</sub>", rendered);
        Assert.Contains("FT", rendered);
    }

    [Fact]
    public void ReadmeExporter_RendersRoundedExpectedGoalsForUnplayedFixtures()
    {
        var rendered = ReadmeSnapshotExportService.RenderSnapshotBlock(
            TournamentProjection("hash", 100, DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            [
                PredictionResult(
                    UnplayedFixture(),
                    expectedHomeGoals: 2.6,
                    expectedAwayGoals: .8,
                    mostLikelyScore: (1, 1))
            ],
            Names(),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        Assert.Contains("| Match | Status | Result / Pick | Why | H | D | A |", rendered);
        Assert.Contains("| <img", rendered);
        Assert.Contains("| 3-1 |", rendered);
        Assert.Contains("Marcador m&#225;s probable: 1-1", rendered);
        Assert.Contains("60", rendered);
        Assert.Contains("%", rendered);
    }

    [Fact]
    public void ReadmeExporter_FallsBackToMostLikelyScoreWhenExpectedGoalsAreMissing()
    {
        var rendered = ReadmeSnapshotExportService.RenderSnapshotBlock(
            TournamentProjection("hash", 100, DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            [PredictionResult(UnplayedFixture())],
            Names(),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        Assert.Contains("1-0", rendered);
        Assert.Contains("60", rendered);
        Assert.Contains("%", rendered);
    }

    [Fact]
    public void ReadmeExporter_RendersPredictionRationaleModelAndSignals()
    {
        var rendered = ReadmeSnapshotExportService.RenderSnapshotBlock(
            TournamentProjection("hash", 100, DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            [
                PredictionResult(
                    UnplayedFixture(),
                    sources: [new SourceMetadata("model ladder", "derived", Notes: "Goles + contexto reciente")],
                    featuresUsed: ["Modelo de goles", "Disponibilidad de jugadores"],
                    drivers: ["Aplicó una calibración Elo/FIFA de 15 % hacia empate."])
            ],
            Names(),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        Assert.Contains("Model: Oraculo final (Goles + contexto reciente)", rendered);
        Assert.Contains("Signals: Modelo de goles, Disponibilidad de jugadores", rendered);
        Assert.Contains("Elo/FIFA", rendered);
    }

    [Fact]
    public void ReadmeExporter_RendersOnlyImpactfulAvailabilityClaims()
    {
        var fixture = UnplayedFixture();
        var rendered = ReadmeSnapshotExportService.RenderSnapshotBlock(
            TournamentProjection("hash", 100, DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            [PredictionResult(fixture)],
            Names(),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            new Dictionary<string, IReadOnlyList<AvailabilityClaim>>
            {
                [fixture.Id] =
                [
                    AvailabilityClaim("Moise Bombito", "argentina", "Argentina", AvailabilityClaimStatus.ConfirmedOutInjury, affectsPrediction: true),
                    AvailabilityClaim("Available Player", "france", "France", AvailabilityClaimStatus.Available, affectsPrediction: false)
                ]
            });

        Assert.Contains("Bajas: Argentina: Moise Bombito (injury)", rendered);
        Assert.DoesNotContain("Available Player", rendered);
    }

    [Fact]
    public void ReadmeExporter_SanitizesRationaleTableCells()
    {
        var rendered = ReadmeSnapshotExportService.RenderSnapshotBlock(
            TournamentProjection("hash", 100, DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            [
                PredictionResult(
                    UnplayedFixture(),
                    featuresUsed: ["line | pipe", "spaced\r\nvalue <tag>"],
                    missing: ["odds | market"])
            ],
            Names(),
            DateTimeOffset.Parse("2026-01-02T00:00:00Z"));

        Assert.Contains("line &#124; pipe", rendered);
        Assert.Contains("spaced value &lt;tag&gt;", rendered);
        Assert.Contains("odds &#124; market", rendered);
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

    private static MatchPredictionResult PredictionResult(
        Fixture fixture,
        IReadOnlyList<SourceMetadata>? sources = null,
        IReadOnlyList<string>? featuresUsed = null,
        IReadOnlyList<string>? drivers = null,
        IReadOnlyList<string>? missing = null,
        double? expectedHomeGoals = null,
        double? expectedAwayGoals = null,
        (int Home, int Away)? mostLikelyScore = null) => new()
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
            ExpectedHomeGoals = expectedHomeGoals,
            ExpectedAwayGoals = expectedAwayGoals,
            MostLikelyScore = mostLikelyScore ?? (1, 0),
            Explanation = "test",
            Sources = sources ?? [],
            FeaturesUsed = featuresUsed ?? [],
            Drivers = drivers ?? [],
            FeaturesMissing = missing ?? []
        }
    };

    private static AvailabilityClaim AvailabilityClaim(
        string player,
        string teamId,
        string teamName,
        AvailabilityClaimStatus status,
        bool affectsPrediction) => new()
    {
        Player = player,
        PlayerKey = player.ToLowerInvariant().Replace(" ", "-"),
        TeamId = teamId,
        TeamName = teamName,
        Status = status,
        SourceUrl = "https://example.test/source",
        AffectsPrediction = affectsPrediction
    };

    private static async Task<(TestEnvironment Environment, CsvImportService Importer, IOptions<OloraculoConfig> Options)> PrepareReadmeExportAsync(
        string root,
        OloraculoDbContext db)
    {
        var webRoot = Path.Combine(root, "Oloraculo.Web");
        var dataRoot = Path.Combine(webRoot, "Data");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(root, "Oloraculo.sln"), "");
        File.WriteAllText(Path.Combine(root, "README.md"), "# Holi.\n\n# Oloraculo\n");
        foreach (var file in Directory.GetFiles(Path.Combine(WebProjectRoot(), "Data"), "*.csv"))
            File.Copy(file, Path.Combine(dataRoot, Path.GetFileName(file)));

        var environment = new TestEnvironment(webRoot);
        var options = Options.Create(new OloraculoConfig
        {
            SimulationCount = 1,
            SimulationSeed = 1,
            RecentResultCount = 8,
            GoalModelYearsWindow = 3,
            ApiFootballBaseUrl = "https://api.test/",
            ApiFootballLeagueId = 1,
            ApiFootballSeason = 2026,
            OpenRouterBaseUrl = "https://openrouter.test/",
            AvailabilitySourceUrls = [],
            EloRefreshMaxLookbackDays = 0
        });
        var importer = new CsvImportService(db, environment);
        await importer.ImportAllAsync();

        return (environment, importer, options);
    }

    private static ReadmeSnapshotExportService BuildReadmeExporter(
        OloraculoDbContext db,
        CsvImportService importer,
        TestEnvironment environment,
        IOptions<OloraculoConfig> options)
    {
        var availability = new AvailabilityNewsService(
            new HttpClient(new FakeHttpMessageHandler(new Dictionary<string, string>())) { BaseAddress = new Uri("https://openrouter.test/") },
            db,
            options);
        var api = new ApiFootballService(
            new HttpClient(new FakeHttpMessageHandler(new Dictionary<string, string>())) { BaseAddress = new Uri("https://api.test/") },
            db,
            options,
            availability);
        var snapshots = new SnapshotService(db);
        var prediction = new PredictionService(db, options);

        return new ReadmeSnapshotExportService(
            db,
            importer,
            new RankingRefreshService(new HttpClient(new FakeHttpMessageHandler(new Dictionary<string, string>())), environment, options),
            api,
            availability,
            prediction,
            new EvaluationService(db),
            snapshots,
            new SimulationService(db, prediction, snapshots, options),
            environment,
            NullLogger<ReadmeSnapshotExportService>.Instance);
    }

    private static MatchPrediction SnapshotPrediction(
        Fixture fixture,
        string modelName,
        double home,
        double draw,
        double away,
        (int Home, int Away) mostLikelyScore) => new()
        {
            FixtureId = fixture.Id,
            HomeTeamId = fixture.HomeTeamId,
            AwayTeamId = fixture.AwayTeamId,
            PredictorName = modelName,
            PredictorPriority = 5,
            Outcome = new OutcomeProbabilities(home, draw, away),
            MostLikelyScore = mostLikelyScore,
            Explanation = modelName
        };

}
