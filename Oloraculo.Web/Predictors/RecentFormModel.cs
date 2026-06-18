using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Predictors
{
    public class RecentFormModel : IPredictor
    {
        public string Name => "Recent form";
        public int Priority => 3;

        public MatchPrediction Predict(MatchContext context)
        {
            if (context.AwayElo is null || context.HomeElo is null)
            {
                return new MatchPrediction
                {
                    PredictorName = Name,
                    PredictorPriority = Priority,
                    FixtureId = context.Fixture.Id,
                    HomeTeamId = context.HomeTeam.Id,
                    AwayTeamId = context.AwayTeam.Id,
                    Outcome = OutcomeProbabilities.Uniform,
                    Explanation = "Elo ratings are required for both teams to make this prediction.",
                    Degraded = true,
                };
            }

            double HomeFormDelta = FormDelta(context.HomeRecentMatchHistory, context.HomeTeam.Id);
            double AwayFormDelta = FormDelta(context.AwayRecentMatchHistory, context.AwayTeam.Id);
            double Home = context.HomeElo.Value + HomeFormDelta;
            double Away = context.AwayElo.Value + AwayFormDelta;
            double EloExpectation = ProbabilityHelper.EloExpectation(Home, Away);
            OutcomeProbabilities Outcome = ProbabilityHelper.OutcomeFromExpectation(EloExpectation, Home - Away);

            bool MissingRecentHistory = context.HomeRecentMatchHistory.Count == 0 || context.AwayRecentMatchHistory.Count == 0;

            return new MatchPrediction
            {
                PredictorName = Name,
                PredictorPriority = Priority,
                FixtureId = context.Fixture.Id,
                HomeTeamId = context.HomeTeam.Id,
                AwayTeamId = context.AwayTeam.Id,
                Outcome = Outcome,
                Explanation = $"Using Elo plus recent form when available: {context.HomeTeam.Name} delta {HomeFormDelta:F1}, " +
                $"{context.AwayTeam.Name} delta: {AwayFormDelta:F1}.",
                Drivers = new[] { "Recent results" },
                FeaturesUsed = new[] { "Recent results", "Elo ratings" },
                FeaturesMissing = MissingRecentHistory ? ["recent history for one or both teams"] : Array.Empty<string>(),
                Sources = [SourceMetadata.EloRatings, SourceMetadata.HistoricalResultsCsv],
                Degraded = MissingRecentHistory,
            };
        }

        /// <summary>
        /// Implements a basic recency bias: weights recent matches more heavily.
        /// Also, considers goal difference and points earned.
        /// NOTE: Numbers are somewhat arbitrary and may need tuning.
        /// </summary>
        /// <param name="recentMatches"></param>
        /// <param name="teamId"></param>
        /// <returns></returns>
        public static double FormDelta(IReadOnlyList<MatchResult> recentMatches, string teamId)
        {
            double Delta = 0;
            double Weight = 1.0;
            foreach(var match in recentMatches.OrderByDescending(m => m.Date))
            {
                var GoalsFor = match.HomeTeamId == teamId ? match.HomeGoals : match.AwayGoals;
                var GoalsAgainst = match.HomeTeamId == teamId ? match.AwayGoals : match.HomeGoals;
                var Points = GoalsFor > GoalsAgainst ? 3 : GoalsFor == GoalsAgainst ? 1 : 0;
                Delta += Weight * ((Points - 0.2) * 18 + Math.Clamp(GoalsFor - GoalsAgainst, -3, 3) * 8);
                Weight *= 0.8; // Exponential decay for older matches
            }
            return Math.Clamp(Delta, -100, 100);
        }
    }
}
