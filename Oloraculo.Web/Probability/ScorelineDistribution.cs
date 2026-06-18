namespace Oloraculo.Web.Probability
{
    public readonly record struct FirstGoalProbabilities(double HomeFirst, double AwayFirst, double NoGoal)
    {
        public double Total => HomeFirst + AwayFirst + NoGoal;

        public FirstGoalProbabilities Normalize()
        {
            var total = Total;
            if (total <= 0 || !double.IsFinite(total))
                return new FirstGoalProbabilities(0, 0, 1);
            return new FirstGoalProbabilities(HomeFirst / total, AwayFirst / total, NoGoal / total);
        }
    }

    public class ScorelineDistribution
    {
        public int MaxGoals { get; init; }
        public double[,] Matrix { get; init; } = new double[0, 0];
        public bool IgnoreZeroProbabilityStatesForEvaluation { get; init; }

        public double Probability(int home, int away) =>
            home >= 0 &&
            away >= 0 &&
            home <= MaxGoals &&
            away <= MaxGoals
                ? Matrix[home, away]
                : 0;

        public OutcomeProbabilities ToOutcome()
        {
            double homeWin = 0, draw = 0, awayWin = 0;

            for (var h = 0; h <= MaxGoals; h++)
            {
                for (var a = 0; a <= MaxGoals; a++)
                {
                    if (h > a)
                    {
                        homeWin += Matrix[h, a];
                    }
                    else if (h == a)
                    {
                        draw += Matrix[h, a];
                    }
                    else
                    {
                        awayWin += Matrix[h, a];
                    }
                }
            }

            return new OutcomeProbabilities(homeWin, draw, awayWin).Normalize();
        }

        public double TeamNoGoalProbability(bool homeTeam) =>
            SumWhere((home, away) => homeTeam ? home == 0 : away == 0);

        public double BothTeamsToScoreProbability() =>
            SumWhere((home, away) => home > 0 && away > 0);

        public double TotalGoalsOverProbability(double line) =>
            SumWhere((home, away) => home + away > line);

        public double TotalGoalsUnderProbability(double line) =>
            SumWhere((home, away) => home + away < line);

        public double TeamTotalOverProbability(bool homeTeam, double line) =>
            SumWhere((home, away) => (homeTeam ? home : away) > line);

        public double TeamTotalUnderProbability(bool homeTeam, double line) =>
            SumWhere((home, away) => (homeTeam ? home : away) < line);

        public double TeamGoalsAtLeastProbability(bool homeTeam, int goals) =>
            goals <= 0 ? 1.0 : SumWhere((home, away) => (homeTeam ? home : away) >= goals);

        public double TeamGoalsExactlyProbability(bool homeTeam, int goals) =>
            goals < 0 ? 0.0 : SumWhere((home, away) => (homeTeam ? home : away) == goals);

        public double SpreadCoverProbability(bool homeTeam, double handicap) =>
            SumWhere((home, away) => homeTeam ? home + handicap > away : away + handicap > home);

        public double SpreadPushProbability(bool homeTeam, double handicap) =>
            SumWhere((home, away) => Math.Abs((homeTeam ? home + handicap - away : away + handicap - home)) < 1e-9);

        public double TeamWinOrNoGoalProbability(bool homeTeam) =>
            SumWhere((home, away) => homeTeam ? home > away || home == 0 : away > home || away == 0);

        public double TeamScoresAndDoesNotWinProbability(bool homeTeam) =>
            SumWhere((home, away) => homeTeam ? home > 0 && home <= away : away > 0 && away <= home);

        public (double Home, double Away) ExpectedGoals()
        {
            double homeGoals = 0, awayGoals = 0;
            for (var home = 0; home <= MaxGoals; home++)
            {
                for (var away = 0; away <= MaxGoals; away++)
                {
                    var probability = Probability(home, away);
                    homeGoals += home * probability;
                    awayGoals += away * probability;
                }
            }

            return (homeGoals, awayGoals);
        }

        public FirstGoalProbabilities FirstGoalProbabilities()
        {
            var noGoal = Probability(0, 0);
            var (homeGoals, awayGoals) = ExpectedGoals();
            var totalExpectedGoals = homeGoals + awayGoals;
            if (totalExpectedGoals <= 0)
                return new FirstGoalProbabilities(0, 0, 1).Normalize();

            var nonNoGoal = Math.Max(0, 1.0 - noGoal);
            return new FirstGoalProbabilities(
                nonNoGoal * homeGoals / totalExpectedGoals,
                nonNoGoal * awayGoals / totalExpectedGoals,
                noGoal).Normalize();
        }

        public ScorelineDistribution ReweightToOutcome(OutcomeProbabilities target)
        {
            var normalizedTarget = target.Normalize();
            var current = ToOutcome();
            var matrix = new double[MaxGoals + 1, MaxGoals + 1];

            ReweightBucket((home, away) => home > away, current.HomeWin, normalizedTarget.HomeWin);
            ReweightBucket((home, away) => home == away, current.Draw, normalizedTarget.Draw);
            ReweightBucket((home, away) => away > home, current.AwayWin, normalizedTarget.AwayWin);

            var result = new ScorelineDistribution { MaxGoals = MaxGoals, Matrix = matrix };
            return result.Normalize();

            void ReweightBucket(Func<int, int, bool> predicate, double currentMass, double targetMass)
            {
                if (targetMass <= 0)
                    return;

                var states = new List<(int Home, int Away, double Probability)>();
                for (var home = 0; home <= MaxGoals; home++)
                {
                    for (var away = 0; away <= MaxGoals; away++)
                    {
                        if (predicate(home, away))
                            states.Add((home, away, Probability(home, away)));
                    }
                }

                if (states.Count == 0)
                    return;

                if (currentMass <= 0)
                {
                    var uniform = targetMass / states.Count;
                    foreach (var state in states)
                        matrix[state.Home, state.Away] = uniform;
                    return;
                }

                var scale = targetMass / currentMass;
                foreach (var state in states)
                    matrix[state.Home, state.Away] = state.Probability * scale;
            }
        }

        public ScorelineDistribution Normalize()
        {
            var matrix = new double[MaxGoals + 1, MaxGoals + 1];
            double total = 0;
            for (var home = 0; home <= MaxGoals; home++)
            {
                for (var away = 0; away <= MaxGoals; away++)
                {
                    var value = Probability(home, away);
                    if (!double.IsFinite(value) || value < 0)
                        value = 0;
                    matrix[home, away] = value;
                    total += value;
                }
            }

            if (total <= 0)
            {
                var fallback = Uniform(MaxGoals);
                return new ScorelineDistribution
                {
                    MaxGoals = fallback.MaxGoals,
                    Matrix = fallback.Matrix,
                    IgnoreZeroProbabilityStatesForEvaluation = IgnoreZeroProbabilityStatesForEvaluation
                };
            }

            for (var home = 0; home <= MaxGoals; home++)
                for (var away = 0; away <= MaxGoals; away++)
                    matrix[home, away] /= total;

            return new ScorelineDistribution { MaxGoals = MaxGoals, Matrix = matrix, IgnoreZeroProbabilityStatesForEvaluation = IgnoreZeroProbabilityStatesForEvaluation };
        }

        public ScorelineDistribution ConditionOnMinimumScore(int homeGoals, int awayGoals)
        {
            if (homeGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(homeGoals), "Home goals cannot be negative.");
            if (awayGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(awayGoals), "Away goals cannot be negative.");

            var conditionedMaxGoals = Math.Max(MaxGoals, Math.Max(homeGoals, awayGoals));
            var matrix = new double[conditionedMaxGoals + 1, conditionedMaxGoals + 1];
            var retainedMass = 0.0;
            for (var home = homeGoals; home <= MaxGoals; home++)
            {
                for (var away = awayGoals; away <= MaxGoals; away++)
                {
                    var probability = Probability(home, away);
                    if (!double.IsFinite(probability) || probability < 0)
                        probability = 0;
                    matrix[home, away] = probability;
                    retainedMass += probability;
                }
            }

            if (retainedMass <= 0 || !double.IsFinite(retainedMass))
            {
                matrix[homeGoals, awayGoals] = 1.0;
                return new ScorelineDistribution
                {
                    MaxGoals = conditionedMaxGoals,
                    Matrix = matrix,
                    IgnoreZeroProbabilityStatesForEvaluation = true
                };
            }

            for (var home = homeGoals; home <= MaxGoals; home++)
                for (var away = awayGoals; away <= MaxGoals; away++)
                    matrix[home, away] /= retainedMass;

            return new ScorelineDistribution
            {
                MaxGoals = conditionedMaxGoals,
                Matrix = matrix,
                IgnoreZeroProbabilityStatesForEvaluation = true
            };
        }

        public (int Home, int Away) MostLikelyScoreline()
        {
            var best = (Home: 0, Away: 0, Probability: -1.0);
            for (var h = 0; h <= MaxGoals; h++)
            {
                for (var a = 0; a <= MaxGoals; a++)
                {
                    if (Matrix[h, a] > best.Probability)
                    {
                        best = (h, a, Matrix[h, a]);
                    }
                }
            }

            return (best.Home, best.Away);
        }

        private double SumWhere(Func<int, int, bool> predicate)
        {
            var sum = 0.0;
            for (var home = 0; home <= MaxGoals; home++)
            {
                for (var away = 0; away <= MaxGoals; away++)
                {
                    if (predicate(home, away))
                        sum += Matrix[home, away];
                }
            }

            return sum;
        }

        private static ScorelineDistribution Uniform(int maxGoals)
        {
            var safeMaxGoals = Math.Max(0, maxGoals);
            var matrix = new double[safeMaxGoals + 1, safeMaxGoals + 1];
            var probability = 1.0 / ((safeMaxGoals + 1) * (safeMaxGoals + 1));
            for (var home = 0; home <= safeMaxGoals; home++)
                for (var away = 0; away <= safeMaxGoals; away++)
                    matrix[home, away] = probability;

            return new ScorelineDistribution { MaxGoals = safeMaxGoals, Matrix = matrix };
        }
    }
}
