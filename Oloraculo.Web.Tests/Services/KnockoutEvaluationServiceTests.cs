using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.Models;
using Oloraculo.Web.Services;

namespace Oloraculo.Web.Tests;

public class KnockoutEvaluationServiceTests : TestFixtures
{
    [Fact]
    public async Task Evaluation_UsesWinnerTeamForTiedKnockoutScores()
    {
        await using var db = await NewDb();
        var fixture = new Fixture
        {
            Id = KnockoutBracketService.FixtureId(73),
            Group = "KO",
            HomeTeamId = "a",
            AwayTeamId = "b",
            NeutralVenue = true
        };
        db.Teams.AddRange(new Team { Id = "a", Name = "A" }, new Team { Id = "b", Name = "B" });
        db.Fixtures.Add(fixture);
        db.Snapshots.Add(new PredictionSnapshot
        {
            Kind = "match",
            FixtureId = fixture.Id,
            ModelName = "Final",
            InputSummaryHash = "hash",
            PayloadJson = "{}",
            Explanation = "test",
            HomeWin = .7,
            Draw = .2,
            AwayWin = .1
        });
        await db.SaveChangesAsync();

        var count = await new EvaluationService(db).EvaluateLatestSnapshotAsync(fixture, 1, 1, "b");
        var evaluation = await db.Evaluations.SingleAsync();

        Assert.Equal(1, count);
        Assert.Equal("Away", evaluation.Actual);
        Assert.Equal("b", fixture.WinnerTeamId);
        Assert.False(evaluation.TopPickCorrect);
    }
}
