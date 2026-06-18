using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Predictors
{
    public class GoalPlusRecentContextModel : IPredictor
    {
        private readonly GoalModel _goalModel;

        public GoalPlusRecentContextModel(IReadOnlyList<MatchResult> results, int yearsWindow = 3)
            : this(new GoalModel(results, yearsWindow))
        {
        }

        public GoalPlusRecentContextModel(GoalModel goalModel)
        {
            _goalModel = goalModel;
        }

        public string Name => "Goals + recent context";
        public int Priority => 5;

        public MatchPrediction Predict(MatchContext context)
        {
            var (homeGoals, awayGoals, degradedGoalModel) = _goalModel.ExpectedGoals(context);
            var usedFeatures = new List<string> { "Goal model" };
            var missingFeatures = new List<string>();
            var drivers = new List<string>();
            var appliedContext = false;

            if (degradedGoalModel)
                missingFeatures.Add("required goal-model data");

            if (context.FixtureContext is { } ctx)
            {
                var hasRoleAwareImpact =
                    ctx.UnavailableHomeAttackImpact > 0 ||
                    ctx.UnavailableHomeDefenseImpact > 0 ||
                    ctx.UnavailableAwayAttackImpact > 0 ||
                    ctx.UnavailableAwayDefenseImpact > 0;

                if (hasRoleAwareImpact)
                {
                    homeGoals *= Math.Max(0.82, 1.0 - ctx.UnavailableHomeAttackImpact);
                    awayGoals *= Math.Max(0.82, 1.0 - ctx.UnavailableAwayAttackImpact);
                    homeGoals *= 1.0 + ctx.UnavailableAwayDefenseImpact;
                    awayGoals *= 1.0 + ctx.UnavailableHomeDefenseImpact;
                    usedFeatures.Add("Player availability");
                    drivers.Add($"Role-aware impact applied. Team A: attack -{ctx.UnavailableHomeAttackImpact:P1}, defense -{ctx.UnavailableHomeDefenseImpact:P1}; Team B: attack -{ctx.UnavailableAwayAttackImpact:P1}, defense -{ctx.UnavailableAwayDefenseImpact:P1}.");
                    appliedContext = true;
                }
                else if (ctx.UnavailableHomePlayers > 0 || ctx.UnavailableAwayPlayers > 0)
                {
                    homeGoals *= Math.Max(0.86, 1.0 - ctx.UnavailableHomePlayers * 0.02);
                    awayGoals *= Math.Max(0.86, 1.0 - ctx.UnavailableAwayPlayers * 0.02);
                    usedFeatures.Add("Player availability");
                    drivers.Add($"Player availability applied. Absences: Team A {ctx.UnavailableHomePlayers}, Team B {ctx.UnavailableAwayPlayers}.");
                    appliedContext = true;
                }
                else
                {
                    missingFeatures.Add("player availability with impact");
                }

                if (ctx.HasLineups)
                    missingFeatures.Add("lineup impact model");
                else
                    missingFeatures.Add("lineups");

                if (ctx.HasOdds)
                    missingFeatures.Add("odds calibration");
                else
                    missingFeatures.Add("odds");
            }
            else
            {
                missingFeatures.AddRange(["player availability", "lineups", "odds"]);
            }

            var scoreline = _goalModel.BuildScoreline(homeGoals, awayGoals);
            usedFeatures.AddRange(
            [
                "Opponent-adjusted attacking strength",
                "Opponent-adjusted defensive vulnerability",
                "Dixon-Coles scoreline grid"
            ]);

            var degraded = degradedGoalModel || !appliedContext;
            var sources = new List<SourceMetadata> { SourceMetadata.HistoricalResultsCsv, SourceMetadata.ApiFootball };
            if (context.FixtureContext?.HasAvailabilityNews == true)
                sources.Add(SourceMetadata.AvailabilityNews);

            return new MatchPrediction
            {
                PredictorName = Name,
                PredictorPriority = Priority,
                FixtureId = context.Fixture.Id,
                HomeTeamId = context.HomeTeamId,
                AwayTeamId = context.AwayTeamId,
                Outcome = scoreline.ToOutcome(),
                ExpectedHomeGoals = Math.Round(homeGoals, 2),
                ExpectedAwayGoals = Math.Round(awayGoals, 2),
                Scoreline = scoreline,
                MostLikelyScore = scoreline.MostLikelyScoreline(),
                Explanation = appliedContext
                    ? $"Goal model adjusted with source context. Expected goals: {context.HomeTeam.Name} {homeGoals:0.00} - {awayGoals:0.00} {context.AwayTeam.Name}."
                    : $"No source context changed the goal model. Expected goals: {context.HomeTeam.Name} {homeGoals:0.00} - {awayGoals:0.00} {context.AwayTeam.Name}.",
                Drivers = drivers.Count == 0 ? ["No context adjustment applied"] : drivers,
                FeaturesUsed = usedFeatures,
                FeaturesMissing = missingFeatures,
                Sources = sources,
                Degraded = degraded
            };
        }
    }
}
