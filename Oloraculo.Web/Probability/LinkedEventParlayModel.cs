namespace Oloraculo.Web.Probability
{
    [Flags]
    public enum SameGameLegMask
    {
        None = 0,
        LegA = 1,
        LegB = 2,
        LegC = 4,
        All = LegA | LegB | LegC
    }

    public sealed record LinkedEventComponent(
        string Name,
        SameGameLegMask Affects,
        double LambdaPer90)
    {
        public LinkedEventComponent Scale(double exposureMultiplier) =>
            this with { LambdaPer90 = Math.Max(0, LambdaPer90 * Math.Max(0, exposureMultiplier)) };
    }

    public sealed record LinkedEventParlayResult(
        double AllHitProbability,
        double IndependentProductProbability,
        double CorrelationLift,
        double FairDecimalOdds,
        IReadOnlyList<LinkedEventComponent> Components);

    public static class LinkedEventParlayModel
    {
        public static LinkedEventParlayResult ThreeLegAllOnePlus(
            IReadOnlyList<LinkedEventComponent> components,
            double exposureMultiplier = 1.0)
        {
            ArgumentNullException.ThrowIfNull(components);
            if (!double.IsFinite(exposureMultiplier) || exposureMultiplier < 0)
                throw new ArgumentOutOfRangeException(nameof(exposureMultiplier), "Exposure multiplier must be finite and non-negative.");

            var scaled = components
                .Where(component => component.Affects != SameGameLegMask.None && component.LambdaPer90 > 0 && double.IsFinite(component.LambdaPer90))
                .Select(component => component.Scale(exposureMultiplier))
                .ToList();

            var allHit = InclusionExclusionAllHit(scaled);
            var independent = IndependentProduct(scaled);
            return new LinkedEventParlayResult(
                allHit,
                independent,
                allHit - independent,
                allHit <= 0 ? double.PositiveInfinity : 1.0 / allHit,
                scaled);
        }

        private static double InclusionExclusionAllHit(IReadOnlyList<LinkedEventComponent> components)
        {
            var legAZero = ZeroProbability(components, SameGameLegMask.LegA);
            var legBZero = ZeroProbability(components, SameGameLegMask.LegB);
            var legCZero = ZeroProbability(components, SameGameLegMask.LegC);
            var legABZero = ZeroProbability(components, SameGameLegMask.LegA | SameGameLegMask.LegB);
            var legACZero = ZeroProbability(components, SameGameLegMask.LegA | SameGameLegMask.LegC);
            var legBCZero = ZeroProbability(components, SameGameLegMask.LegB | SameGameLegMask.LegC);
            var allZero = ZeroProbability(components, SameGameLegMask.All);

            return Math.Clamp(
                1.0 - legAZero - legBZero - legCZero + legABZero + legACZero + legBCZero - allZero,
                0,
                1);
        }

        private static double IndependentProduct(IReadOnlyList<LinkedEventComponent> components)
        {
            var lambdaA = LambdaForLeg(components, SameGameLegMask.LegA);
            var lambdaB = LambdaForLeg(components, SameGameLegMask.LegB);
            var lambdaC = LambdaForLeg(components, SameGameLegMask.LegC);
            return OnePlus(lambdaA) * OnePlus(lambdaB) * OnePlus(lambdaC);
        }

        private static double ZeroProbability(IReadOnlyList<LinkedEventComponent> components, SameGameLegMask zeroLegs)
        {
            var lambdaTouchingZeroLegs = components
                .Where(component => (component.Affects & zeroLegs) != SameGameLegMask.None)
                .Sum(component => component.LambdaPer90);
            return Math.Exp(-lambdaTouchingZeroLegs);
        }

        private static double LambdaForLeg(IReadOnlyList<LinkedEventComponent> components, SameGameLegMask leg) =>
            components
                .Where(component => (component.Affects & leg) != SameGameLegMask.None)
                .Sum(component => component.LambdaPer90);

        private static double OnePlus(double lambda) =>
            1.0 - Math.Exp(-Math.Max(0, lambda));
    }
}
