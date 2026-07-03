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

public class PredictorTests : TestFixtures
{
    [Fact]
    public void GoalModel_ProducesUsableScorelineWhenTeamsHaveEnoughHistory()
    {
        var model = new GoalModel(
        [
            Result("a", "b", 2, 0),
            Result("a", "b", 1, 0),
            Result("b", "a", 1, 2)
        ]);

        var prediction = model.Predict(TestContext());

        Assert.False(prediction.Degraded);
        Assert.NotNull(prediction.Scoreline);
        Assert.True(prediction.ExpectedHomeGoals > 0.1);
        Assert.True(prediction.Outcome.IsValid);
    }

    [Fact]
    public void GoalModel_ExtremeHistoryKeepsExpectedGoalsBoundedAndValid()
    {
        var now = DateTimeOffset.UtcNow;
        var results = Enumerable.Range(0, 6)
            .Select(i => new MatchResult
            {
                Id = $"extreme-{i}",
                HomeTeamId = "a",
                AwayTeamId = "b",
                HomeGoals = 20,
                AwayGoals = 0,
                Date = now.AddDays(-i),
                Tournament = "test",
                Neutral = true,
                Source = "test"
            })
            .ToList();
        var model = new GoalModel(results);

        var (homeGoals, awayGoals, degraded) = model.ExpectedGoals(TestContext());
        var prediction = model.Predict(TestContext());

        Assert.False(degraded);
        Assert.InRange(homeGoals, 0.1, 5.5);
        Assert.InRange(awayGoals, 0.1, 5.5);
        Assert.True(double.IsFinite(homeGoals));
        Assert.True(double.IsFinite(awayGoals));
        Assert.True(prediction.Outcome.IsValid);
    }

    [Fact]
    public void ContextModel_DoesNotClaimLineupsOrOddsWereUsedWithoutConversionLogic()
    {
        var goal = new GoalModel(
        [
            Result("a", "b", 2, 0),
            Result("a", "b", 1, 0),
            Result("b", "a", 1, 2)
        ]);
        var context = TestContext(fixtureContext: new FixtureContext
        {
            FixtureId = "test",
            HasLineups = true,
            HasOdds = true
        });

        var prediction = new GoalPlusRecentContextModel(goal).Predict(context);

        Assert.DoesNotContain(nameof(FeaturesEnum.Lineups), prediction.FeaturesUsed);
        Assert.DoesNotContain(nameof(FeaturesEnum.Odds), prediction.FeaturesUsed);
        Assert.Contains("modelo de impacto de alineaciones", prediction.FeaturesMissing);
        Assert.Contains("calibración por cuotas", prediction.FeaturesMissing);
        Assert.True(prediction.Degraded);
    }

    [Fact]
    public void ContextModel_BecomesUsableWhenAvailabilityActuallyAdjustsGoals()
    {
        var goal = new GoalModel(
        [
            Result("a", "b", 2, 0),
            Result("a", "b", 1, 0),
            Result("b", "a", 1, 2)
        ]);
        var context = TestContext(fixtureContext: new FixtureContext
        {
            FixtureId = "test",
            UnavailableHomePlayers = 2
        });

        var prediction = new GoalPlusRecentContextModel(goal).Predict(context);

        Assert.False(prediction.Degraded);
        Assert.Contains("Disponibilidad de jugadores", prediction.FeaturesUsed);
    }

    [Fact]
    public void FinalSelector_BlendsUsableRungsAndKeepsHighestScoreline()
    {
        var form = Prediction(3, "Forma reciente", .05, .05, .90);
        var goal = Prediction(4, "Modelo de goles (Poisson)", .90, .05, .05, scoreline: ProbabilityHelper.PoissonScoreline(3.0, .4));
        var context = Prediction(5, "Goles + contexto reciente", .10, .80, .10, degraded: true, missing: ["availability"]);

        var final = FinalPredictionSelector.Select([form, goal, context]);

        Assert.Equal("Oráculo final", final.PredictorName);
        Assert.Equal(4, final.PredictorPriority);
        Assert.Equal(.71111, final.Outcome.HomeWin, 5);
        Assert.Equal(.05000, final.Outcome.Draw, 5);
        Assert.Equal(.23889, final.Outcome.AwayWin, 5);
        Assert.Same(goal.Scoreline, final.Scoreline);
        Assert.Contains(final.Drivers, d => d.Contains("ensamble calibrado"));
    }

    [Fact]
    public void FinalSelector_BlendsRankingsWithGoalModelInsteadOfApplyingConditionalBias()
    {
        var fifa = Prediction(1, "Ranking FIFA", .15, .20, .65, sources: [SourceMetadata.FifaRankings]);
        var elo = Prediction(2, "Elo", .10, .20, .70, sources: [SourceMetadata.EloRatings]);
        var goalScoreline = ProbabilityHelper.PoissonScoreline(1.4, 1.1);
        var goal = Prediction(4, "Modelo de goles (Poisson)", .45, .35, .20, scoreline: goalScoreline);

        var final = FinalPredictionSelector.Select([fifa, elo, goal]);

        Assert.Equal("Oráculo final", final.PredictorName);
        Assert.Equal(4, final.PredictorPriority);
        Assert.Equal(.27333, final.Outcome.HomeWin, 5);
        Assert.Equal(.27000, final.Outcome.Draw, 5);
        Assert.Equal(.45667, final.Outcome.AwayWin, 5);
        Assert.Same(goalScoreline, final.Scoreline);
        Assert.Contains(final.Drivers, d => d.Contains("Pesos efectivos"));
        Assert.Contains("ensamble calibrado", final.Explanation);
        Assert.Contains(SourceMetadata.FifaRankings, final.Sources);
        Assert.Contains(SourceMetadata.EloRatings, final.Sources);
    }

    [Fact]
    public void FinalSelector_StillUsesRankingModelsWhenTheyDisagree()
    {
        var fifa = Prediction(1, "Ranking FIFA", .65, .20, .15, sources: [SourceMetadata.FifaRankings]);
        var elo = Prediction(2, "Elo", .10, .20, .70, sources: [SourceMetadata.EloRatings]);
        var goal = Prediction(4, "Modelo de goles (Poisson)", .45, .35, .20);

        var final = FinalPredictionSelector.Select([fifa, elo, goal]);

        Assert.NotEqual(goal.Outcome, final.Outcome);
        Assert.Contains(final.Drivers, d => d.Contains("Pesos efectivos"));
        Assert.Contains(SourceMetadata.FifaRankings, final.Sources);
        Assert.Contains(SourceMetadata.EloRatings, final.Sources);
    }

    [Fact]
    public void FinalSelector_ExcludesDegradedRankingModelsFromBlend()
    {
        var fifa = Prediction(1, "Ranking FIFA", .15, .20, .65, degraded: true, sources: [SourceMetadata.FifaRankings]);
        var elo = Prediction(2, "Elo", .10, .20, .70, sources: [SourceMetadata.EloRatings]);
        var goal = Prediction(4, "Modelo de goles (Poisson)", .45, .35, .20);

        var final = FinalPredictionSelector.Select([fifa, elo, goal]);

        Assert.Equal(.30417, final.Outcome.HomeWin, 5);
        Assert.Equal(.28750, final.Outcome.Draw, 5);
        Assert.Equal(.40833, final.Outcome.AwayWin, 5);
        Assert.DoesNotContain(final.Drivers, d => d.Contains("Ranking FIFA 20%"));
        Assert.DoesNotContain(SourceMetadata.FifaRankings, final.Sources);
        Assert.Contains(SourceMetadata.EloRatings, final.Sources);
    }

    [Fact]
    public void KnockoutAdvanceProbability_TreatsDrawsAsNearCoinFlipWithSmallStrengthEdge()
    {
        var prediction = new MatchPredictionResult
        {
            Fixture = new Fixture { Id = "f", HomeTeamId = "a", AwayTeamId = "b" },
            HomeTeamName = "A",
            AwayTeamName = "B",
            BestPrediction = Prediction(4, "Modelo de goles (Poisson)", .45, .30, .25)
        };

        var pHome = MatchSamplerCache.KnockoutAdvanceHomeProbability(prediction);

        Assert.Equal(.612, pHome, 3);
    }

}
