using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Models;
using Oloraculo.Web.Predictors;
using Oloraculo.Web.Probability;
using Oloraculo.Web.Services;
using System.Net;

namespace Oloraculo.Web.Tests;

public abstract class TestFixtures
{
    protected static IOptions<OloraculoConfig> SimulationOptions(int simulations, int seed) =>
        Options.Create(new OloraculoConfig
        {
            GoalModelYearsWindow = 3,
            RecentResultCount = 8,
            SimulationCount = simulations,
            SimulationSeed = seed
        });

    protected static IOptions<OloraculoConfig> AvailabilityOptions(string[] sources) =>
        Options.Create(new OloraculoConfig
        {
            OpenRouterApiKey = "test-key",
            OpenRouterBaseUrl = "https://openrouter.test/",
            OpenRouterModel = "test-model",
            AvailabilitySourceUrls = sources,
            AvailabilityMaxArticleChars = 4000,
            AvailabilityRequireCrossCheck = true
        });

    protected static async Task<OloraculoDbContext> ImportedDb()
    {
        var db = await NewDb();
        await new CsvImportService(db, new TestEnvironment(WebProjectRoot())).ImportAllAsync();
        return db;
    }

    protected static async Task<OloraculoDbContext> NewDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<OloraculoDbContext>().UseSqlite(connection).Options;
        var db = new OloraculoDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    protected static MatchContext TestContext(string homeId = "a", string awayId = "b", FixtureContext? fixtureContext = null) => new()
    {
        Fixture = new Fixture { Id = "test", HomeTeamId = homeId, AwayTeamId = awayId, NeutralVenue = true },
        HomeTeam = new Team { Id = homeId, Name = homeId.ToUpperInvariant() },
        AwayTeam = new Team { Id = awayId, Name = awayId.ToUpperInvariant() },
        HomeElo = new Rating { TeamId = homeId, Type = RatingTypeEnum.Elo, Value = 1800, Source = "test" },
        AwayElo = new Rating { TeamId = awayId, Type = RatingTypeEnum.Elo, Value = 1700, Source = "test" },
        HomeRecentMatchHistory = [],
        AwayRecentMatchHistory = [],
        FixtureContext = fixtureContext
    };

    protected static MatchResult Result(string home, string away, int homeGoals, int awayGoals) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        HomeTeamId = home,
        AwayTeamId = away,
        HomeGoals = homeGoals,
        AwayGoals = awayGoals,
        Date = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(1, 30)),
        Tournament = "test",
        Neutral = true,
        Source = "test"
    };

    protected static MatchPrediction Prediction(
        int priority,
        string name,
        double home,
        double draw,
        double away,
        bool degraded = false,
        IReadOnlyList<string>? missing = null,
        ScorelineDistribution? scoreline = null,
        IReadOnlyList<SourceMetadata>? sources = null) => new()
    {
        PredictorPriority = priority,
        PredictorName = name,
        FixtureId = "f",
        HomeTeamId = "a",
        AwayTeamId = "b",
        Outcome = new OutcomeProbabilities(home, draw, away).Normalize(),
        Scoreline = scoreline,
        Explanation = name,
        FeaturesMissing = missing ?? [],
        Sources = sources ?? [],
        Degraded = degraded
    };

    protected static void AssertPredictionResultEqual(MatchPredictionResult expected, MatchPredictionResult actual)
    {
        Assert.Equal(expected.Fixture.Id, actual.Fixture.Id);
        Assert.Equal(expected.Fixture.HomeTeamId, actual.Fixture.HomeTeamId);
        Assert.Equal(expected.Fixture.AwayTeamId, actual.Fixture.AwayTeamId);
        Assert.Equal(expected.HomeTeamName, actual.HomeTeamName);
        Assert.Equal(expected.AwayTeamName, actual.AwayTeamName);
        Assert.Equal(expected.Predictions.Count, actual.Predictions.Count);

        for (var i = 0; i < expected.Predictions.Count; i++)
            AssertPredictionEqual(expected.Predictions[i], actual.Predictions[i]);

        AssertPredictionEqual(expected.BestPrediction, actual.BestPrediction);
    }

    protected static void AssertPredictionEqual(MatchPrediction expected, MatchPrediction actual)
    {
        Assert.Equal(expected.PredictorName, actual.PredictorName);
        Assert.Equal(expected.PredictorPriority, actual.PredictorPriority);
        Assert.Equal(expected.FixtureId, actual.FixtureId);
        Assert.Equal(expected.HomeTeamId, actual.HomeTeamId);
        Assert.Equal(expected.AwayTeamId, actual.AwayTeamId);
        Assert.Equal(expected.Outcome.HomeWin, actual.Outcome.HomeWin);
        Assert.Equal(expected.Outcome.Draw, actual.Outcome.Draw);
        Assert.Equal(expected.Outcome.AwayWin, actual.Outcome.AwayWin);
        Assert.Equal(expected.ExpectedHomeGoals, actual.ExpectedHomeGoals);
        Assert.Equal(expected.ExpectedAwayGoals, actual.ExpectedAwayGoals);
        Assert.Equal(expected.MostLikelyScore, actual.MostLikelyScore);
        Assert.Equal(expected.Degraded, actual.Degraded);
        Assert.Equal(expected.FeaturesMissing, actual.FeaturesMissing);
        AssertScorelineEqual(expected.Scoreline, actual.Scoreline);
    }

    protected static void AssertScorelineEqual(ScorelineDistribution? expected, ScorelineDistribution? actual)
    {
        Assert.Equal(expected is null, actual is null);
        if (expected is null || actual is null)
            return;

        Assert.Equal(expected.MaxGoals, actual.MaxGoals);
        for (var home = 0; home <= expected.MaxGoals; home++)
            for (var away = 0; away <= expected.MaxGoals; away++)
                Assert.Equal(expected.Probability(home, away), actual.Probability(home, away));
    }

    protected static string WebProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Oloraculo.Web");
            if (File.Exists(Path.Combine(candidate, "Data", OloraculoDataFiles.GroupsCsv)))
                return candidate;

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Oloraculo.Web"));
    }

    protected static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "OloraculoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    protected sealed class TestEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Oloraculo.Web.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    protected sealed class FakeHttpMessageHandler(IReadOnlyDictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? "";
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var methodKey = string.IsNullOrWhiteSpace(body)
                ? $"{request.Method.Method} {uri}"
                : $"{request.Method.Method} {uri}\n{body}";

            if (!responses.TryGetValue(methodKey, out var content) && !responses.TryGetValue(uri, out content))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
