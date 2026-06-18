using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;
using System.Threading.Tasks;

namespace Oloraculo.Web.Predictors
{
    public class FifaRankingModel : IPredictor
    {
        public string Name => "FIFA ranking";
        public int Priority => 1;

        public MatchPrediction Predict(MatchContext context)
        {
            Rating? home = context.HomeFifaRank;
            Rating? away = context.AwayFifaRank;
            if (home is null || away is null)
            {
                return new MatchPrediction
                {
                    PredictorName = Name,
                    PredictorPriority = Priority,
                    FixtureId = context.Fixture.Id,
                    HomeTeamId = context.HomeTeam.Id,
                    AwayTeamId = context.AwayTeam.Id,
                    Outcome = OutcomeProbabilities.Uniform,
                    Explanation = "Missing FIFA ranking data for one or both teams.",
                    Degraded = true
                };
            }

            double Diff = home.Value - away.Value;
            double Expected = ProbabilityHelper.EloExpectation(home.Value, away.Value);
            OutcomeProbabilities OutcomeProbability = ProbabilityHelper.OutcomeFromExpectation(Expected, Diff);
            return new MatchPrediction
            {
                PredictorName = Name,
                PredictorPriority = Priority,
                FixtureId = context.Fixture.Id,
                HomeTeamId = context.HomeTeam.Id,
                AwayTeamId = context.AwayTeam.Id,
                Outcome = OutcomeProbability,
                Explanation = $"Based on FIFA ranking points: {context.HomeTeam.Name} {home.Value:0}, {context.AwayTeam.Name} {away.Value:0}.",
                Drivers = new[] { $"FIFA point difference: {Diff:+0;-0}" },
                FeaturesUsed = new[] { "Team A FIFA points", "Team B FIFA points" },
                FeaturesMissing = Array.Empty<string>(),
                Sources = new[] { SourceMetadata.FifaRankings },
                Degraded = false
            };
        }
    }
}
