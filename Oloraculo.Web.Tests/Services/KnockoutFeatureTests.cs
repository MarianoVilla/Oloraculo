using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;
using Oloraculo.Web.Services;
using Oloraculo.Web.Services.Simulation;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.Helpers;

namespace Oloraculo.Web.Tests;

public class KnockoutFeatureTests : TestFixtures
{
    [Fact]
    public void OfficialTopologyContainsAllThirtyTwoMatchesAndBronzeDependencies()
    {
        var ties = WorldCup2026Bracket.KnockoutTies;

        Assert.Equal(32, ties.Count);
        Assert.Equal(Enumerable.Range(73, 32), ties.Select(t => t.Id).Order());
        var bronze = Assert.Single(ties, t => t.Id == 103);
        Assert.Equal(KnockoutStageEnum.ThirdPlace, bronze.Stage);
        Assert.Equal(BracketSlotKindEnum.LoserOfTie, bronze.Home.Kind);
        Assert.Equal(101, bronze.Home.TieId);
        Assert.Equal(BracketSlotKindEnum.LoserOfTie, bronze.Away.Kind);
        Assert.Equal(102, bronze.Away.TieId);
        Assert.Equal(32, WorldCup2026Bracket.OfficialSchedule.Count);
        Assert.Equal(new DateOnly(2026, 6, 29), WorldCup2026Bracket.OfficialSchedule[76].Date);
        Assert.Equal("Houston", WorldCup2026Bracket.OfficialSchedule[76].Location);
    }

    [Fact]
    public void PreferredScoreUsesRoundedExpectedGoalsBeforePoissonMode()
    {
        var prediction = MatchPrediction("a", "b");
        prediction.ExpectedHomeGoals = 1.5;
        prediction.ExpectedAwayGoals = .49;
        prediction.MostLikelyScore = (1, 1);

        var available = PredictionScoreHelper.TryPreferredScore(prediction, out var score);

        Assert.True(available);
        Assert.Equal((2, 0), score);
    }

    [Fact]
    public async Task PregameSnapshotRequiresMatchingParticipantsAndCutoff()
    {
        await using var db = await NewDb();
        db.Teams.AddRange(
            new Team { Id = "a", Name = "A" },
            new Team { Id = "b", Name = "B" },
            new Team { Id = "x", Name = "X" },
            new Team { Id = "y", Name = "Y" });
        await db.SaveChangesAsync();
        var service = new SnapshotService(db);
        var cutoff = DateTimeOffset.UtcNow;

        var matching = await service.SaveMatchAsync(MatchPrediction("a", "b"));
        matching.CreatedAt = cutoff.AddHours(-2);
        var wrongPair = await service.SaveMatchAsync(MatchPrediction("x", "y"));
        wrongPair.CreatedAt = cutoff.AddHours(-1);
        var tooLate = await service.SaveMatchAsync(MatchPrediction("a", "b"));
        tooLate.CreatedAt = cutoff.AddMinutes(1);
        await db.SaveChangesAsync();

        var loaded = await service.LoadPregamePredictionAsync("wc2026:match:74", "a", "b", cutoff);

        Assert.NotNull(loaded.Prediction);
        Assert.Equal(matching.CreatedAt, loaded.CreatedAt);
        Assert.Equal("a", loaded.Prediction!.BestPrediction.HomeTeamId);
        Assert.Equal("b", loaded.Prediction.BestPrediction.AwayTeamId);
    }

    [Fact]
    public async Task LatestKnockoutBoardIsOrderedInMemoryForSqlite()
    {
        await using var db = await NewDb();
        var service = new SnapshotService(db);
        await service.SaveKnockoutBoardAsync(new KnockoutBoard
        {
            GeneratedAt = new DateTimeOffset(2026, 6, 29, 10, 0, 0, TimeSpan.Zero),
            Warnings = ["older"]
        });
        await service.SaveKnockoutBoardAsync(new KnockoutBoard
        {
            GeneratedAt = new DateTimeOffset(2026, 6, 30, 10, 0, 0, TimeSpan.Zero),
            Warnings = ["newer"]
        });

        var loaded = await service.LoadLatestKnockoutBoardAsync();

        Assert.NotNull(loaded);
        Assert.Equal("newer", Assert.Single(loaded.Warnings));
    }

    [Fact]
    public void ReadmeRenderingIncludesConfirmedProjectedAndActualKnockoutData()
    {
        var projection = new TournamentProjection { ModelName = "Final", InputSummaryHash = "hash", Simulations = 1 };
        var board = new KnockoutBoard
        {
            Matches =
            [
                new KnockoutMatchView
                {
                    MatchNumber = 74,
                    Stage = KnockoutStageEnum.RoundOf32,
                    HomeTeamId = "germany",
                    HomeTeamName = "Germany",
                    HomeResolution = ParticipantResolution.Confirmed,
                    AwayTeamId = "paraguay",
                    AwayTeamName = "Paraguay",
                    AwayResolution = ParticipantResolution.Projected,
                    PredictedHomeGoals = 1,
                    PredictedAwayGoals = 1,
                    PredictedWinnerTeamId = "paraguay",
                    IsPlayed = true,
                    HomeGoals = 0,
                    AwayGoals = 1
                }
            ]
        };

        var rendered = ReadmeSnapshotExportService.RenderSnapshotBlock(
            projection, [], new Dictionary<string, string>(), DateTimeOffset.UtcNow, knockoutBoard: board);

        Assert.Contains("### Eliminatorias", rendered);
        Assert.Contains("Germany <sub>confirmed</sub>", rendered);
        Assert.Contains("Paraguay <sub>projected</sub>", rendered);
        Assert.Contains("1-1; Paraguay advances", rendered);
        Assert.Contains("**0-1**", rendered);
    }

    [Fact]
    public async Task AuthoritativeWinnerReplacesProjectionAndPropagatesForward()
    {
        await using var db = await ImportedDb();
        var feed = new StubFixtureSource(
        [
            new TournamentFixtureFeedRow
            {
                ExternalFixtureId = "api-73",
                Round = "Round of 32",
                KickoffUtc = new DateTimeOffset(2026, 6, 28, 20, 0, 0, TimeSpan.Zero),
                Venue = "Los Angeles Stadium",
                Status = "FT",
                HomeTeamId = "south-africa",
                AwayTeamId = "canada",
                HomeGoals = 0,
                AwayGoals = 1,
                WinnerTeamId = "canada",
                IsFinished = true
            }
        ]);
        var options = Options.Create(new OloraculoConfig { RecentResultCount = 8, GoalModelYearsWindow = 4 });
        var snapshots = new SnapshotService(db);
        var service = new KnockoutUpdateService(db, feed, new PredictionService(db, options), snapshots);

        var report = await service.RefreshAsync();

        var match73 = Assert.Single(report.Board.Matches, match => match.MatchNumber == 73);
        var match90 = Assert.Single(report.Board.Matches, match => match.MatchNumber == 90);
        Assert.True(match73.IsPlayed);
        Assert.Equal("canada", match73.WinnerTeamId);
        Assert.Equal("canada", match90.HomeTeamId);
        Assert.Contains(await db.Results.ToListAsync(), result => result.Id == "api-football:api-73");
    }

    private static MatchPrediction MatchPrediction(string home, string away) => new()
    {
        FixtureId = "wc2026:match:74",
        HomeTeamId = home,
        AwayTeamId = away,
        PredictorName = "test",
        PredictorPriority = 1,
        Outcome = new OutcomeProbabilities(.5, .2, .3),
        MostLikelyScore = (1, 0),
        Explanation = "test"
    };

    private sealed class StubFixtureSource(IReadOnlyList<TournamentFixtureFeedRow> fixtures) : ITournamentFixtureSource
    {
        public Task<TournamentFixtureFeedResult> FetchTournamentFixturesAsync(CancellationToken ct = default) =>
            Task.FromResult(new TournamentFixtureFeedResult { IsConfigured = true, Fixtures = fixtures });
    }
}
