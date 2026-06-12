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

        public string Name => "Goles + contexto reciente";
        public int Priority => 5;

        public MatchPrediction Predict(MatchContext context)
        {
            var (homeGoals, awayGoals, degradedGoalModel) = _goalModel.ExpectedGoals(context);
            var usedFeatures = new List<string> { "Modelo de goles" };
            var missingFeatures = new List<string>();
            var drivers = new List<string>();
            var appliedContext = false;

            if (degradedGoalModel)
                missingFeatures.Add("datos requeridos por el modelo de goles");

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
                    usedFeatures.Add("Disponibilidad de jugadores");
                    drivers.Add($"Impacto por rol aplicado. Equipo A: ataque -{ctx.UnavailableHomeAttackImpact:P1}, defensa -{ctx.UnavailableHomeDefenseImpact:P1}; equipo B: ataque -{ctx.UnavailableAwayAttackImpact:P1}, defensa -{ctx.UnavailableAwayDefenseImpact:P1}.");
                    appliedContext = true;
                }
                else if (ctx.UnavailableHomePlayers > 0 || ctx.UnavailableAwayPlayers > 0)
                {
                    homeGoals *= Math.Max(0.86, 1.0 - ctx.UnavailableHomePlayers * 0.02);
                    awayGoals *= Math.Max(0.86, 1.0 - ctx.UnavailableAwayPlayers * 0.02);
                    usedFeatures.Add("Disponibilidad de jugadores");
                    drivers.Add($"Disponibilidad de jugadores aplicada. Bajas: equipo A {ctx.UnavailableHomePlayers}, equipo B {ctx.UnavailableAwayPlayers}.");
                    appliedContext = true;
                }
                else
                {
                    missingFeatures.Add("disponibilidad de jugadores con impacto");
                }

                if (ctx.HasLineups)
                    missingFeatures.Add("modelo de impacto de alineaciones");
                else
                    missingFeatures.Add("alineaciones");

                if (ctx.HasOdds)
                    missingFeatures.Add("calibración por cuotas");
                else
                    missingFeatures.Add("cuotas");
            }
            else
            {
                missingFeatures.AddRange(["disponibilidad de jugadores", "alineaciones", "cuotas"]);
            }

            var scoreline = ProbabilityHelper.PoissonScoreline(homeGoals, awayGoals);
            usedFeatures.AddRange(
            [
                "Fuerza de ataque ajustada por rival",
                "Vulnerabilidad defensiva ajustada por rival",
                "Grilla de marcadores Dixon-Coles"
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
                    ? $"Modelo de goles ajustado con contexto de fuentes. Goles esperados: {context.HomeTeam.Name} {homeGoals:0.00} - {awayGoals:0.00} {context.AwayTeam.Name}."
                    : $"Ningún contexto de fuentes modificó el modelo de goles. Goles esperados: {context.HomeTeam.Name} {homeGoals:0.00} - {awayGoals:0.00} {context.AwayTeam.Name}.",
                Drivers = drivers.Count == 0 ? ["No se aplicó ajuste de contexto"] : drivers,
                FeaturesUsed = usedFeatures,
                FeaturesMissing = missingFeatures,
                Sources = sources,
                Degraded = degraded
            };
        }
    }
}
