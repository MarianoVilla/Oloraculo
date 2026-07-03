using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Predictors
{
    public static class FinalPredictionSelector
    {
        private static readonly IReadOnlyDictionary<string, double> BaseWeights = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Ranking FIFA"] = 0.15,
            ["Elo"] = 0.25,
            ["Forma reciente"] = 0.10,
            ["Modelo de goles (Poisson)"] = 0.35,
            ["Goles + contexto reciente"] = 0.15
        };

        public static MatchPrediction Select(IReadOnlyList<MatchPrediction> ladder)
        {
            if (ladder.Count == 0)
                return EmptyFinal();

            var ordered = ladder.OrderBy(p => p.PredictorPriority).ToList();
            var selected = ordered.LastOrDefault(p => !p.Degraded) ?? ordered.First();
            var blended = BlendUsablePredictions(ordered, selected);
            var skippedHigher = ordered
                .Where(p => p.PredictorPriority > selected.PredictorPriority && p.Degraded)
                .OrderByDescending(p => p.PredictorPriority)
                .ToList();

            var drivers = new List<string>
            {
                $"Usó {blended.ModelCount} modelos disponibles en un ensamble calibrado."
            };
            drivers.AddRange(skippedHigher.Select(p => $"Omitió {p.PredictorName}: {Reason(p)}"));
            drivers.AddRange(selected.Drivers);
            drivers.Add($"Pesos efectivos: {string.Join(", ", blended.WeightNotes)}.");

            var sources = selected.Sources
                .Concat(blended.Sources)
                .Concat([new SourceMetadata("model ladder", "derived", Notes: string.Join(", ", blended.ModelNames))])
                .Distinct()
                .ToList();

            return new MatchPrediction
            {
                PredictorName = "Oráculo final",
                PredictorPriority = selected.PredictorPriority,
                FixtureId = selected.FixtureId,
                HomeTeamId = selected.HomeTeamId,
                AwayTeamId = selected.AwayTeamId,
                Outcome = blended.Outcome,
                ExpectedHomeGoals = selected.ExpectedHomeGoals,
                ExpectedAwayGoals = selected.ExpectedAwayGoals,
                Scoreline = selected.Scoreline,
                MostLikelyScore = selected.MostLikelyScore,
                Explanation = BuildExplanation(selected, skippedHigher, blended),
                Drivers = drivers,
                FeaturesUsed = ordered.Where(p => !p.Degraded).SelectMany(p => p.FeaturesUsed).Distinct().ToList(),
                FeaturesMissing = skippedHigher.SelectMany(p => p.FeaturesMissing).Distinct().ToList(),
                Sources = sources,
                Degraded = selected.Degraded
            };
        }

        private static BlendedPrediction BlendUsablePredictions(IReadOnlyList<MatchPrediction> ordered, MatchPrediction fallback)
        {
            var weighted = ordered
                .Where(p => !p.Degraded)
                .Select(p => new
                {
                    Prediction = p,
                    Weight = BaseWeights.TryGetValue(p.PredictorName, out var weight) ? weight : 0.05
                })
                .Where(x => x.Weight > 0)
                .ToList();

            if (weighted.Count == 0)
                return new BlendedPrediction(
                    fallback.Outcome,
                    [fallback.PredictorName],
                    [$"{fallback.PredictorName} 100%"],
                    fallback.Sources,
                    1);

            var total = weighted.Sum(x => x.Weight);
            var outcome = new OutcomeProbabilities(
                weighted.Sum(x => x.Prediction.Outcome.HomeWin * x.Weight) / total,
                weighted.Sum(x => x.Prediction.Outcome.Draw * x.Weight) / total,
                weighted.Sum(x => x.Prediction.Outcome.AwayWin * x.Weight) / total).Normalize();

            return new BlendedPrediction(
                outcome,
                weighted.Select(x => x.Prediction.PredictorName).ToList(),
                weighted.Select(x => $"{x.Prediction.PredictorName} {x.Weight / total:P0}").ToList(),
                weighted.SelectMany(x => x.Prediction.Sources).ToList(),
                weighted.Count);
        }

        private static string BuildExplanation(
            MatchPrediction selected,
            IReadOnlyList<MatchPrediction> skippedHigher,
            BlendedPrediction blended)
        {
            var ensembleSentence = $" El resultado final mezcla los modelos disponibles con pesos calibrados: {string.Join(", ", blended.WeightNotes)}.";

            if (skippedHigher.Count == 0)
                return $"El Oráculo final usó un ensamble calibrado y conservó la grilla de marcador de {selected.PredictorName}. {selected.Explanation}{ensembleSentence}";

            var skipped = string.Join("; ", skippedHigher.Select(p => $"{p.PredictorName} {Reason(p)}"));
            return $"El Oráculo final usó un ensamble calibrado y conservó la grilla de marcador de {selected.PredictorName}; omitió {skipped}. {selected.Explanation}{ensembleSentence}";
        }

        private static string Reason(MatchPrediction prediction)
        {
            if (prediction.FeaturesMissing.Count == 0)
                return "no era usable";

            var missingVerb = prediction.FeaturesMissing.Count == 1 ? "faltaba" : "faltaban";
            return $"no era usable: {missingVerb} {string.Join(", ", prediction.FeaturesMissing)}";
        }

        private static MatchPrediction EmptyFinal() => new()
        {
            PredictorName = "Oráculo final",
            PredictorPriority = 0,
            Outcome = OutcomeProbabilities.Uniform,
            Explanation = "El Oráculo final no tenía predicciones de la escalera, así que devolvió la base.",
            Drivers = ["No había predicciones disponibles en la escalera."],
            FeaturesMissing = ["predicciones de la escalera"],
            Sources = [new SourceMetadata("model ladder", "derived")],
            Degraded = true
        };

        private sealed record BlendedPrediction(
            OutcomeProbabilities Outcome,
            IReadOnlyList<string> ModelNames,
            IReadOnlyList<string> WeightNotes,
            IReadOnlyList<SourceMetadata> Sources,
            int ModelCount);
    }
}
