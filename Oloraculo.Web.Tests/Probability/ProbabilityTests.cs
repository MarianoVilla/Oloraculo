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

public class ProbabilityTests : TestFixtures
{
    [Fact]
    public void OutcomeProbabilities_NormalizesAndUsesOutcomeLabels()
    {
        var p = new OutcomeProbabilities(2, 1, 1).Normalize();

        Assert.True(p.IsValid);
        Assert.Equal(0.5, p.HomeWin, 3);
        Assert.Equal("Home", p.TopPick);
    }

    [Fact]
    public void OutcomeFromExpectation_TreatsEqualMagnitudeGapsSymmetrically()
    {
        var strongerHome = ProbabilityHelper.OutcomeFromExpectation(.78, 400);
        var strongerAway = ProbabilityHelper.OutcomeFromExpectation(.22, -400);

        Assert.Equal(strongerHome.Draw, strongerAway.Draw, 6);
    }

    [Fact]
    public void PoissonScoreline_ProducesARealProbabilityGrid()
    {
        var dist = ProbabilityHelper.PoissonScoreline(2.2, .7);
        var sum = 0.0;
        for (var h = 0; h <= dist.MaxGoals; h++)
            for (var a = 0; a <= dist.MaxGoals; a++)
                sum += dist.Probability(h, a);

        Assert.Equal(1.0, sum, 6);
        Assert.True(dist.ToOutcome().HomeWin > dist.ToOutcome().AwayWin);
        Assert.NotEqual((0, 0), dist.MostLikelyScoreline());
    }

    [Fact]
    public void ScorelineDistribution_ExposesBettingMarketLenses()
    {
        var matrix = new double[3, 3];
        matrix[0, 0] = .10;
        matrix[0, 1] = .05;
        matrix[1, 0] = .20;
        matrix[1, 1] = .15;
        matrix[2, 1] = .30;
        matrix[1, 2] = .20;
        var dist = new ScorelineDistribution { MaxGoals = 2, Matrix = matrix };

        Assert.Equal(0, dist.Probability(-1, 0));
        Assert.Equal(.15, dist.TeamNoGoalProbability(homeTeam: true), 6);
        Assert.Equal(.30, dist.TeamNoGoalProbability(homeTeam: false), 6);
        Assert.Equal(.65, dist.BothTeamsToScoreProbability(), 6);
        Assert.Equal(.50, dist.TotalGoalsOverProbability(2.5), 6);

        Assert.Equal(.65, dist.TeamWinOrNoGoalProbability(homeTeam: true), 6);
        Assert.Equal(.35, dist.TeamScoresAndDoesNotWinProbability(homeTeam: true), 6);
        Assert.Equal(1.0,
            dist.TeamWinOrNoGoalProbability(homeTeam: true) + dist.TeamScoresAndDoesNotWinProbability(homeTeam: true),
            6);

        Assert.Equal(.55, dist.TeamWinOrNoGoalProbability(homeTeam: false), 6);
        Assert.Equal(.45, dist.TeamScoresAndDoesNotWinProbability(homeTeam: false), 6);
    }

    [Fact]
    public void ScorelineDistribution_ReweightsShapeToTargetOutcomeProbabilities()
    {
        var dist = ProbabilityHelper.PoissonScoreline(1.6, 1.0);
        var target = new OutcomeProbabilities(.55, .25, .20);

        var reweighted = dist.ReweightToOutcome(target);
        var outcome = reweighted.ToOutcome();

        Assert.Equal(1.0, Sum(reweighted), 6);
        Assert.Equal(.55, outcome.HomeWin, 6);
        Assert.Equal(.25, outcome.Draw, 6);
        Assert.Equal(.20, outcome.AwayWin, 6);
        Assert.Equal(
            dist.Probability(2, 0) / dist.Probability(1, 0),
            reweighted.Probability(2, 0) / reweighted.Probability(1, 0),
            6);
    }

    [Fact]
    public void ScorelineDistribution_ExposesSpreadsTeamTotalsAndFirstGoal()
    {
        var dist = ProbabilityHelper.PoissonScoreline(.575, 2.54);
        var firstGoal = dist.FirstGoalProbabilities();

        Assert.Equal(dist.TotalGoalsOverProbability(2.5), 1.0 - dist.TotalGoalsUnderProbability(2.5), 6);
        Assert.True(dist.SpreadCoverProbability(homeTeam: false, handicap: -1.5) > .50);
        Assert.True(dist.TeamTotalOverProbability(homeTeam: false, 1.5) > dist.TeamTotalOverProbability(homeTeam: false, 2.5));
        Assert.Equal(dist.TeamGoalsAtLeastProbability(homeTeam: true, 1), 1.0 - dist.TeamGoalsExactlyProbability(homeTeam: true, 0), 6);
        Assert.Equal(1.0, firstGoal.Total, 6);
        Assert.True(firstGoal.AwayFirst > firstGoal.HomeFirst);
    }

    [Fact]
    public void LinkedEventParlayModel_SeparatesNaiveProductFromSharedEventLift()
    {
        var independent = LinkedEventParlayModel.ThreeLegAllOnePlus(
        [
            new("Diatta fouls won only", SameGameLegMask.LegA, 1.73),
            new("Theo fouls only", SameGameLegMask.LegB, .89),
            new("Rabiot fouls only", SameGameLegMask.LegC, 1.30)
        ]);
        var linked = LinkedEventParlayModel.ThreeLegAllOnePlus(
        [
            new("Diatta fouled by others", SameGameLegMask.LegA, 1.13),
            new("Theo fouls others", SameGameLegMask.LegB, .59),
            new("Rabiot fouls others", SameGameLegMask.LegC, 1.00),
            new("Theo fouls Diatta", SameGameLegMask.LegA | SameGameLegMask.LegB, .30),
            new("Rabiot fouls Diatta", SameGameLegMask.LegA | SameGameLegMask.LegC, .30)
        ]);
        var limitedMinutes = LinkedEventParlayModel.ThreeLegAllOnePlus(linked.Components, exposureMultiplier: 25.0 / 90.0);

        Assert.Equal(.353, independent.AllHitProbability, 3);
        Assert.Equal(independent.IndependentProductProbability, independent.AllHitProbability, 6);
        Assert.True(linked.AllHitProbability > linked.IndependentProductProbability);
        Assert.True(linked.CorrelationLift > .02);
        Assert.True(limitedMinutes.AllHitProbability < linked.AllHitProbability);
    }

    private static double Sum(ScorelineDistribution distribution)
    {
        var sum = 0.0;
        for (var home = 0; home <= distribution.MaxGoals; home++)
            for (var away = 0; away <= distribution.MaxGoals; away++)
                sum += distribution.Probability(home, away);
        return sum;
    }

}
