using Oloraculo.Web.Models;

namespace Oloraculo.Web.Helpers;

public static class PredictionScoreHelper
{
    public static bool TryPreferredScore(MatchPrediction prediction, out (int Home, int Away) score)
    {
        if (prediction.ExpectedHomeGoals is { } home && prediction.ExpectedAwayGoals is { } away &&
            double.IsFinite(home) && double.IsFinite(away))
        {
            score = (RoundedExpectedGoals(home), RoundedExpectedGoals(away));
            return true;
        }

        if (prediction.MostLikelyScore is { } mostLikely)
        {
            score = mostLikely;
            return true;
        }

        score = default;
        return false;
    }

    public static int RoundedExpectedGoals(double value) =>
        Math.Max(0, (int)Math.Round(value, MidpointRounding.AwayFromZero));
}
