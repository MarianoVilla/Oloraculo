using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Services.Simulation
{
    public class MatchSamplerCache
    {
        private readonly Func<string, string, CancellationToken, Task<MatchPredictionResult>> _predictPairAsync;
        private readonly Dictionary<string, MatchPredictionResult> _cache = new(StringComparer.Ordinal);

        public MatchSamplerCache(PredictionService prediction)
            : this(prediction.PredictPairAsync)
        {
        }

        public MatchSamplerCache(Func<string, string, CancellationToken, Task<MatchPredictionResult>> predictPairAsync) =>
            _predictPairAsync = predictPairAsync;

        public async Task<(int Home, int Away)> SampleScoreAsync(string homeId, string awayId, Random rng, CancellationToken ct)
        {
            var prediction = await GetPredictionAsync(homeId, awayId, ct);
            return SampleScoreFromPrediction(prediction, rng);
        }

        public async Task<string> KnockoutWinnerAsync(string homeId, string awayId, Random rng, CancellationToken ct)
        {
            var prediction = await GetPredictionAsync(homeId, awayId, ct);
            var score = SampleScoreFromPrediction(prediction, rng);
            return ResolveKnockoutWinner(homeId, awayId, prediction, score, rng);
        }

        public async Task<MatchPredictionResult> GetPredictionAsync(string homeId, string awayId, CancellationToken ct)
        {
            var key = $"{homeId}|{awayId}";
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            cached = await _predictPairAsync(homeId, awayId, ct);
            _cache[key] = cached;
            return cached;
        }

        public static (int Home, int Away) SampleScoreFromPrediction(MatchPredictionResult prediction, Random rng)
        {
            var final = prediction.BestPrediction;
            if (final.Scoreline is not null)
                return ProbabilityHelper.SampleScore(final.Scoreline, rng);

            var roll = rng.NextDouble();
            var outcome = final.Outcome;
            if (roll < outcome.HomeWin) return (1, 0);
            if (roll < outcome.HomeWin + outcome.Draw) return (1, 1);
            return (0, 1);
        }

        public static string ResolveKnockoutWinner(string homeId, string awayId, MatchPredictionResult prediction, (int Home, int Away) score, Random rng)
        {
            if (score.Home > score.Away)
                return homeId;
            if (score.Away > score.Home)
                return awayId;

            var pHome = KnockoutAdvanceHomeProbability(prediction);
            return rng.NextDouble() < pHome ? homeId : awayId;
        }

        public static double KnockoutAdvanceHomeProbability(MatchPredictionResult prediction)
        {
            var outcome = prediction.BestPrediction.Outcome.Normalize();
            var strengthEdge = outcome.HomeWin - outcome.AwayWin;
            var drawAdvanceShare = Math.Clamp(0.50 + 0.20 * strengthEdge, 0.40, 0.60);
            return Math.Clamp(outcome.HomeWin + outcome.Draw * drawAdvanceShare, 0.01, 0.99);
        }
    }
}
