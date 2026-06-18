using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Tests.Probability;

public class PoissonGoalDecayModelTests
{
    [Fact]
    public void InfersMarketImpliedLambdaFromNormalizedOverTwoPointFive()
    {
        var overProbability = PoissonGoalDecayModel.NormalizeBinaryPrice(.49m, .52m);
        var lambda = PoissonGoalDecayModel.ImpliedLambdaForOver(2.5, overProbability);

        Assert.InRange(overProbability, .484, .486);
        Assert.InRange(lambda, 2.60, 2.62);
    }

    [Fact]
    public void InfersRemainingLambdaFromLiveOverUnderProbabilityAndCurrentGoals()
    {
        var remainingLambda = PoissonGoalDecayModel.ImpliedRemainingLambdaForOver(line: 2.5, currentGoals: 1, overProbability: .50);

        Assert.InRange(remainingLambda, 1.67, 1.69);
        Assert.InRange(PoissonGoalDecayModel.OverProbability(line: 2.5, currentGoals: 1, remainingExpectedGoals: remainingLambda), .499, .501);
    }

    [Fact]
    public void ScorelessOverTwoPointFiveDecayMatchesPoissonRemainingGoalMass()
    {
        var kickoffLambda = PoissonGoalDecayModel.ImpliedLambdaForOver(2.5, PoissonGoalDecayModel.NormalizeBinaryPrice(.49m, .52m));

        var kickoff = PoissonGoalDecayModel.Point(kickoffLambda, line: 2.5, minute: 0, currentGoals: 0);
        var minute20 = PoissonGoalDecayModel.Point(kickoffLambda, line: 2.5, minute: 20, currentGoals: 0);

        Assert.InRange(kickoff.OverProbability, .484, .486);
        Assert.InRange(minute20.RemainingExpectedGoals, 2.02, 2.04);
        Assert.InRange(minute20.OverProbability, .329, .335);
        Assert.InRange(minute20.OverDecayPerScorelessMinute, .0075, .0081);
        Assert.Equal(minute20.OverDecayPerScorelessMinute, minute20.UnderGainPerScorelessMinute, 12);
    }

    [Fact]
    public void GoalJumpUsesMarginalThresholdMass()
    {
        var kickoffLambda = 2.61;
        var minute10NoGoal = PoissonGoalDecayModel.Point(kickoffLambda, line: 2.5, minute: 10, currentGoals: 0);
        var minute10OneGoal = PoissonGoalDecayModel.Point(kickoffLambda, line: 2.5, minute: 10, currentGoals: 1);

        Assert.True(minute10OneGoal.OverProbability > minute10NoGoal.OverProbability);
        Assert.InRange(minute10NoGoal.OverGoalJump, .26, .28);
        Assert.InRange(minute10OneGoal.OverProbability, .67, .69);
        Assert.True(minute10NoGoal.UnderGoalJump < 0);
    }

    [Fact]
    public void OverSettlesWhenCurrentGoalsAlreadyClearLine()
    {
        var point = PoissonGoalDecayModel.Point(kickoffExpectedGoals: 2.61, line: 1.5, minute: 30, currentGoals: 2);

        Assert.Equal(1.0, point.OverProbability, 12);
        Assert.Equal(0.0, point.UnderProbability, 12);
        Assert.Equal(0.0, point.OverGoalJump, 12);
    }

    [Fact]
    public void ScorelineDistributionFromCurrentStateRemovesImpossibleScoresAndDecaysRemainingGoals()
    {
        var kickoff = Distribution(maxGoals: 5,
            ((0, 0), .08),
            ((1, 0), .17),
            ((1, 1), .18),
            ((2, 0), .18),
            ((2, 1), .20),
            ((3, 1), .12),
            ((3, 2), .07));

        var result = PoissonGoalDecayModel.ScorelineDistributionFromCurrentState(kickoff, currentHomeGoals: 1, currentAwayGoals: 0, minute: 60);

        Assert.True(result.Distribution.IgnoreZeroProbabilityStatesForEvaluation);
        Assert.Equal(1, result.CurrentHomeGoals);
        Assert.Equal(0, result.CurrentAwayGoals);
        Assert.InRange(result.RemainingFraction, .333, .334);
        Assert.True(result.RemainingHomeExpectedGoals < result.KickoffHomeExpectedGoals);
        Assert.True(result.RemainingAwayExpectedGoals < result.KickoffAwayExpectedGoals);
        Assert.Equal(0.0, result.Distribution.Probability(0, 0), 12);
        Assert.Equal(0.0, result.Distribution.Probability(0, 1), 12);
        Assert.True(result.Distribution.Probability(1, 0) > 0);
        Assert.InRange(result.Distribution.TotalGoalsOverProbability(1.5), .45, .65);
    }

    [Fact]
    public void ScorelineDistributionCanUseMarketImpliedRemainingTotal()
    {
        var kickoff = Distribution(maxGoals: 5,
            ((0, 0), .08),
            ((1, 0), .17),
            ((1, 1), .18),
            ((2, 0), .18),
            ((2, 1), .20),
            ((3, 1), .12),
            ((3, 2), .07));

        var modelDecay = PoissonGoalDecayModel.ScorelineDistributionFromCurrentState(kickoff, 1, 0, minute: 60);
        var anchored = PoissonGoalDecayModel.ScorelineDistributionFromCurrentStateWithRemainingTotal(kickoff, 1, 0, minute: 60, remainingTotalExpectedGoals: 2.4);

        Assert.True(anchored.RemainingHomeExpectedGoals + anchored.RemainingAwayExpectedGoals > modelDecay.RemainingHomeExpectedGoals + modelDecay.RemainingAwayExpectedGoals);
        Assert.InRange(anchored.RemainingHomeExpectedGoals + anchored.RemainingAwayExpectedGoals, 2.39, 2.41);
        Assert.Equal(0.0, anchored.Distribution.Probability(0, 0), 12);
        Assert.True(anchored.Distribution.TotalGoalsOverProbability(2.5) > modelDecay.Distribution.TotalGoalsOverProbability(2.5));
    }

    private static ScorelineDistribution Distribution(int maxGoals, params ((int Home, int Away) Score, double Probability)[] states)
    {
        var matrix = new double[maxGoals + 1, maxGoals + 1];
        foreach (var state in states)
            matrix[state.Score.Home, state.Score.Away] = state.Probability;
        return new ScorelineDistribution { MaxGoals = maxGoals, Matrix = matrix }.Normalize();
    }
}
