namespace Oloraculo.Web.Probability
{
    public sealed record TotalGoalsDecayPoint(
        double Minute,
        int CurrentGoals,
        double Line,
        double RemainingExpectedGoals,
        double OverProbability,
        double UnderProbability,
        double OverDecayPerScorelessMinute,
        double UnderGainPerScorelessMinute,
        double OverGoalJump,
        double UnderGoalJump);

    public sealed record ScorelineGoalDecayResult(
        ScorelineDistribution Distribution,
        double Minute,
        int CurrentHomeGoals,
        int CurrentAwayGoals,
        double RemainingFraction,
        double KickoffHomeExpectedGoals,
        double KickoffAwayExpectedGoals,
        double RemainingHomeExpectedGoals,
        double RemainingAwayExpectedGoals,
        double RetainedGridMass);

    public static class PoissonGoalDecayModel
    {
        public static double NormalizeBinaryPrice(decimal overPrice, decimal underPrice)
        {
            if (overPrice < 0 || underPrice < 0)
                throw new ArgumentOutOfRangeException(nameof(overPrice), "Prices cannot be negative.");
            var sum = overPrice + underPrice;
            if (sum <= 0)
                throw new ArgumentException("At least one side price must be positive.");
            return (double)(overPrice / sum);
        }

        public static double ImpliedLambdaForOver(double line, double overProbability, double maxLambda = 8.0)
        {
            return ImpliedRemainingLambdaForOver(line, currentGoals: 0, overProbability, maxLambda);
        }

        public static double ImpliedRemainingLambdaForOver(double line, int currentGoals, double overProbability, double maxLambda = 8.0)
        {
            if (line < 0)
                throw new ArgumentOutOfRangeException(nameof(line), "Line cannot be negative.");
            if (currentGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(currentGoals), "Current goals cannot be negative.");
            if (maxLambda <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxLambda), "Max lambda must be positive.");
            if (overProbability <= 0)
                return 0;
            if (overProbability >= 1)
                return maxLambda;
            if (Math.Max(0, (int)Math.Floor(line) + 1 - currentGoals) <= 0)
                return 0;

            var low = 0.0;
            var high = maxLambda;
            for (var i = 0; i < 80; i++)
            {
                var mid = (low + high) / 2.0;
                var probability = OverProbability(line, currentGoals, remainingExpectedGoals: mid);
                if (probability < overProbability)
                    low = mid;
                else
                    high = mid;
            }

            return (low + high) / 2.0;
        }

        public static double RemainingExpectedGoals(double kickoffExpectedGoals, double minute, double regulationMinutes = 90.0)
        {
            if (kickoffExpectedGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(kickoffExpectedGoals), "Expected goals cannot be negative.");
            if (regulationMinutes <= 0)
                throw new ArgumentOutOfRangeException(nameof(regulationMinutes), "Regulation minutes must be positive.");
            var remainingFraction = Math.Clamp((regulationMinutes - minute) / regulationMinutes, 0.0, 1.0);
            return kickoffExpectedGoals * remainingFraction;
        }

        public static TotalGoalsDecayPoint Point(double kickoffExpectedGoals, double line, double minute, int currentGoals, double regulationMinutes = 90.0)
        {
            if (currentGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(currentGoals), "Current goals cannot be negative.");
            var remaining = RemainingExpectedGoals(kickoffExpectedGoals, minute, regulationMinutes);
            var lambdaPerMinute = regulationMinutes > 0 ? kickoffExpectedGoals / regulationMinutes : 0.0;
            var over = OverProbability(line, currentGoals, remaining);
            var mass = MarginalMassAtThreshold(line, currentGoals, remaining);
            var overDecay = lambdaPerMinute * mass;
            var jump = mass;
            return new TotalGoalsDecayPoint(
                minute,
                currentGoals,
                line,
                remaining,
                over,
                1.0 - over,
                overDecay,
                overDecay,
                jump,
                -jump);
        }

        public static double OverProbability(double line, int currentGoals, double remainingExpectedGoals)
        {
            if (line < 0)
                throw new ArgumentOutOfRangeException(nameof(line), "Line cannot be negative.");
            if (currentGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(currentGoals), "Current goals cannot be negative.");
            if (remainingExpectedGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingExpectedGoals), "Remaining expected goals cannot be negative.");
            var requiredRemainingGoals = Math.Max(0, (int)Math.Floor(line) + 1 - currentGoals);
            if (requiredRemainingGoals <= 0)
                return 1.0;
            return 1.0 - PoissonCdf(remainingExpectedGoals, requiredRemainingGoals - 1);
        }

        public static double UnderProbability(double line, int currentGoals, double remainingExpectedGoals) =>
            1.0 - OverProbability(line, currentGoals, remainingExpectedGoals);

        public static ScorelineGoalDecayResult ScorelineDistributionFromCurrentState(
            ScorelineDistribution kickoffDistribution,
            int currentHomeGoals,
            int currentAwayGoals,
            double minute,
            double regulationMinutes = 90.0)
        {
            ArgumentNullException.ThrowIfNull(kickoffDistribution);
            if (currentHomeGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(currentHomeGoals), "Current home goals cannot be negative.");
            if (currentAwayGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(currentAwayGoals), "Current away goals cannot be negative.");
            if (regulationMinutes <= 0)
                throw new ArgumentOutOfRangeException(nameof(regulationMinutes), "Regulation minutes must be positive.");

            var (homeExpectedGoals, awayExpectedGoals) = kickoffDistribution.Normalize().ExpectedGoals();
            var remainingFraction = Math.Clamp((regulationMinutes - minute) / regulationMinutes, 0.0, 1.0);
            var remainingHome = homeExpectedGoals * remainingFraction;
            var remainingAway = awayExpectedGoals * remainingFraction;
            return BuildScorelineDistributionFromRemainingGoals(
                kickoffDistribution.MaxGoals,
                currentHomeGoals,
                currentAwayGoals,
                minute,
                regulationMinutes,
                remainingFraction,
                homeExpectedGoals,
                awayExpectedGoals,
                remainingHome,
                remainingAway);
        }

        public static ScorelineGoalDecayResult ScorelineDistributionFromCurrentStateWithRemainingTotal(
            ScorelineDistribution kickoffDistribution,
            int currentHomeGoals,
            int currentAwayGoals,
            double minute,
            double remainingTotalExpectedGoals,
            double regulationMinutes = 90.0)
        {
            ArgumentNullException.ThrowIfNull(kickoffDistribution);
            if (remainingTotalExpectedGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(remainingTotalExpectedGoals), "Remaining total expected goals cannot be negative.");
            if (regulationMinutes <= 0)
                throw new ArgumentOutOfRangeException(nameof(regulationMinutes), "Regulation minutes must be positive.");

            var (homeExpectedGoals, awayExpectedGoals) = kickoffDistribution.Normalize().ExpectedGoals();
            var totalExpectedGoals = homeExpectedGoals + awayExpectedGoals;
            var homeShare = totalExpectedGoals > 0 ? homeExpectedGoals / totalExpectedGoals : 0.5;
            var awayShare = 1.0 - homeShare;
            var remainingFraction = Math.Clamp((regulationMinutes - minute) / regulationMinutes, 0.0, 1.0);
            return BuildScorelineDistributionFromRemainingGoals(
                kickoffDistribution.MaxGoals,
                currentHomeGoals,
                currentAwayGoals,
                minute,
                regulationMinutes,
                remainingFraction,
                homeExpectedGoals,
                awayExpectedGoals,
                remainingTotalExpectedGoals * homeShare,
                remainingTotalExpectedGoals * awayShare);
        }

        private static ScorelineGoalDecayResult BuildScorelineDistributionFromRemainingGoals(
            int kickoffMaxGoals,
            int currentHomeGoals,
            int currentAwayGoals,
            double minute,
            double regulationMinutes,
            double remainingFraction,
            double kickoffHomeExpectedGoals,
            double kickoffAwayExpectedGoals,
            double remainingHome,
            double remainingAway)
        {
            if (currentHomeGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(currentHomeGoals), "Current home goals cannot be negative.");
            if (currentAwayGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(currentAwayGoals), "Current away goals cannot be negative.");
            var maxGoals = Math.Max(kickoffMaxGoals, Math.Max(currentHomeGoals, currentAwayGoals));
            var matrix = new double[maxGoals + 1, maxGoals + 1];
            var retainedMass = 0.0;

            for (var home = currentHomeGoals; home <= maxGoals; home++)
            {
                var remainingHomeGoals = home - currentHomeGoals;
                var homeMass = PoissonPmf(remainingHome, remainingHomeGoals);
                for (var away = currentAwayGoals; away <= maxGoals; away++)
                {
                    var remainingAwayGoals = away - currentAwayGoals;
                    var probability = homeMass * PoissonPmf(remainingAway, remainingAwayGoals);
                    matrix[home, away] = probability;
                    retainedMass += probability;
                }
            }

            if (retainedMass <= 0 || !double.IsFinite(retainedMass))
            {
                matrix[currentHomeGoals, currentAwayGoals] = 1.0;
                retainedMass = 1.0;
            }
            else
            {
                for (var home = currentHomeGoals; home <= maxGoals; home++)
                    for (var away = currentAwayGoals; away <= maxGoals; away++)
                        matrix[home, away] /= retainedMass;
            }

            return new ScorelineGoalDecayResult(
                new ScorelineDistribution
                {
                    MaxGoals = maxGoals,
                    Matrix = matrix,
                    IgnoreZeroProbabilityStatesForEvaluation = true
                },
                Math.Clamp(minute, 0.0, regulationMinutes),
                currentHomeGoals,
                currentAwayGoals,
                remainingFraction,
                kickoffHomeExpectedGoals,
                kickoffAwayExpectedGoals,
                remainingHome,
                remainingAway,
                retainedMass);
        }

        public static double MarginalMassAtThreshold(double line, int currentGoals, double remainingExpectedGoals)
        {
            var requiredRemainingGoals = Math.Max(0, (int)Math.Floor(line) + 1 - currentGoals);
            return requiredRemainingGoals <= 0 ? 0.0 : PoissonPmf(remainingExpectedGoals, requiredRemainingGoals - 1);
        }

        public static double PoissonPmf(double lambda, int k)
        {
            if (lambda < 0)
                throw new ArgumentOutOfRangeException(nameof(lambda), "Lambda cannot be negative.");
            if (k < 0)
                return 0;
            var factorial = 1.0;
            for (var i = 2; i <= k; i++)
                factorial *= i;
            return Math.Exp(-lambda) * Math.Pow(lambda, k) / factorial;
        }

        public static double PoissonCdf(double lambda, int k)
        {
            if (k < 0)
                return 0;
            var sum = 0.0;
            for (var i = 0; i <= k; i++)
                sum += PoissonPmf(lambda, i);
            return Math.Clamp(sum, 0.0, 1.0);
        }
    }
}
