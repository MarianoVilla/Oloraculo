using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Predictors
{
    public class GoalModel : IPredictor
    {
        private const double DefaultAverageGoals = 1.25;
        private const double PriorMatches = 2.0;
        private const double GoalScale = 1.10;
        private const double LowScoreRho = 0.00;
        private const double HomeAdvantageMultiplier = 1.08;
        // Sanity-checked against bundled historical results; rerun tooling/goal_strength_calibration.py when revisiting.
        private const double GoalStrengthMinMultiplier = 0.25;
        private const double GoalStrengthMaxMultiplier = 3.5;
        private const int MinimumTeamMatches = 3;
        private const int Iterations = 8;

        private readonly IReadOnlyDictionary<string, GoalStrength> _strengths;
        private readonly double _avgGoals;
        protected readonly int _matchesUsed;
        protected readonly int _yearsWindow;

        public GoalModel(IReadOnlyList<MatchResult> results, int yearsWindow = 8)
        {
            _yearsWindow = yearsWindow;
            (_strengths, _avgGoals, _matchesUsed) = Fit(results, yearsWindow);
        }

        public virtual string Name => "Goal model (Poisson)";
        public virtual int Priority => 4;

        public virtual MatchPrediction Predict(MatchContext context)
        {
            var (homeGoals, awayGoals, degraded) = ExpectedGoals(context);
            var scoreline = BuildScoreline(homeGoals, awayGoals);
            var mostLikely = scoreline.MostLikelyScoreline();

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
                MostLikelyScore = mostLikely,
                Explanation = $"Expected goals: {context.HomeTeam.Name} {homeGoals:0.00} - {awayGoals:0.00} {context.AwayTeam.Name}, fitted with {_matchesUsed} historical results over a {_yearsWindow}-year window.",
                Drivers = [$"Most likely score: {mostLikely.Home}-{mostLikely.Away}"],
                FeaturesUsed =
                [
                    "Opponent-adjusted attacking strength",
                    "Opponent-adjusted defensive vulnerability",
                    "Dixon-Coles scoreline grid"
                ],
                FeaturesMissing = degraded ? ["sufficient goal history for both teams"] : [],
                Sources = [SourceMetadata.HistoricalResultsCsv],
                Degraded = degraded
            };
        }

        public (double Home, double Away, bool Degraded) ExpectedGoals(MatchContext context)
        {
            var hasHome = _strengths.TryGetValue(context.HomeTeamId, out var home);
            var hasAway = _strengths.TryGetValue(context.AwayTeamId, out var away);
            home ??= new GoalStrength();
            away ??= new GoalStrength();

            var degraded = !hasHome || !hasAway || home.Matches < MinimumTeamMatches || away.Matches < MinimumTeamMatches;
            var homeGoals = _avgGoals * home.Attack * away.DefenseVulnerability * GoalScale;
            var awayGoals = _avgGoals * away.Attack * home.DefenseVulnerability * GoalScale;
            if (!context.Fixture.NeutralVenue)
                homeGoals *= HomeAdvantageMultiplier;

            return (
                Math.Clamp(homeGoals, 0.1, 5.5),
                Math.Clamp(awayGoals, 0.1, 5.5),
                degraded);
        }

        public ScorelineDistribution BuildScoreline(double homeGoals, double awayGoals) =>
            ProbabilityHelper.PoissonScoreline(homeGoals, awayGoals, lowScoreRho: LowScoreRho);

        private static (IReadOnlyDictionary<string, GoalStrength> Strengths, double AvgGoals, int MatchesUsed) Fit(IReadOnlyList<MatchResult> results, int yearsWindow)
        {
            if (results.Count == 0)
                return (new Dictionary<string, GoalStrength>(), DefaultAverageGoals, 0);

            var latest = results.Max(r => r.Date);
            var cutoff = yearsWindow > 0 ? latest.AddYears(-yearsWindow) : DateTimeOffset.MinValue;
            var window = results.Where(r => r.Date >= cutoff).ToList();
            if (window.Count == 0)
                window = results.ToList();

            var teams = window.SelectMany(r => new[] { r.HomeTeamId, r.AwayTeamId }).Distinct().ToList();
            var attacks = teams.ToDictionary(t => t, _ => 1.0);
            var vulnerabilities = teams.ToDictionary(t => t, _ => 1.0);
            var matches = teams.ToDictionary(t => t, _ => 0);

            foreach (var result in window)
            {
                matches[result.HomeTeamId]++;
                matches[result.AwayTeamId]++;
            }

            var weighted = window.Select(r =>
            {
                var yearsAgo = Math.Max(0, (latest - r.Date).TotalDays / 365.25);
                return (Result: r, Weight: Math.Pow(0.75, yearsAgo));
            }).ToList();

            var totalWeight = weighted.Sum(r => r.Weight);
            var avg = totalWeight <= 0
                ? DefaultAverageGoals
                : weighted.Sum(r => r.Weight * (r.Result.HomeGoals + r.Result.AwayGoals)) / (2.0 * totalWeight);
            avg = Math.Clamp(avg, 0.6, 2.4);

            for (var iteration = 0; iteration < Iterations; iteration++)
            {
                var nextAttacks = new Dictionary<string, double>();
                var nextVulnerabilities = new Dictionary<string, double>();

                foreach (var team in teams)
                {
                    double goalsFor = 0;
                    double attackExpected = 0;
                    double goalsAgainst = 0;
                    double defenseExpected = 0;
                    double teamWeight = 0;

                    foreach (var (result, weight) in weighted)
                    {
                        if (result.HomeTeamId == team)
                        {
                            goalsFor += weight * result.HomeGoals;
                            attackExpected += weight * avg * vulnerabilities[result.AwayTeamId];
                            goalsAgainst += weight * result.AwayGoals;
                            defenseExpected += weight * avg * attacks[result.AwayTeamId];
                            teamWeight += weight;
                        }
                        else if (result.AwayTeamId == team)
                        {
                            goalsFor += weight * result.AwayGoals;
                            attackExpected += weight * avg * vulnerabilities[result.HomeTeamId];
                            goalsAgainst += weight * result.HomeGoals;
                            defenseExpected += weight * avg * attacks[result.HomeTeamId];
                            teamWeight += weight;
                        }
                    }

                    var rawAttack = attackExpected <= 0 ? 1.0 : goalsFor / attackExpected;
                    var rawVulnerability = defenseExpected <= 0 ? 1.0 : goalsAgainst / defenseExpected;
                    nextAttacks[team] = ShrinkToNeutral(rawAttack, teamWeight);
                    nextVulnerabilities[team] = ShrinkToNeutral(rawVulnerability, teamWeight);
                }

                NormalizeMean(nextAttacks);
                NormalizeMean(nextVulnerabilities);
                attacks = nextAttacks;
                vulnerabilities = nextVulnerabilities;
            }

            var map = teams.ToDictionary(team => team, team => new GoalStrength
            {
                Attack = Math.Clamp(attacks[team], GoalStrengthMinMultiplier, GoalStrengthMaxMultiplier),
                DefenseVulnerability = Math.Clamp(vulnerabilities[team], GoalStrengthMinMultiplier, GoalStrengthMaxMultiplier),
                Matches = matches[team]
            });

            return (map, avg, window.Count);

            static double ShrinkToNeutral(double value, double weight) =>
                Math.Clamp(((value * weight) + PriorMatches) / (weight + PriorMatches), GoalStrengthMinMultiplier, GoalStrengthMaxMultiplier);

            static void NormalizeMean(Dictionary<string, double> values)
            {
                var mean = values.Count == 0 ? 1.0 : values.Values.Average();
                if (mean <= 0)
                    return;

                foreach (var key in values.Keys.ToList())
                    values[key] /= mean;
            }
        }
    }

    public sealed class GoalStrength
    {
        public double Attack { get; init; } = 1;
        public double DefenseVulnerability { get; init; } = 1;
        public int Matches { get; init; }
    }
}
