using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.ComboLab.Pricing
{
    public enum TradeAction
    {
        Buy,
        Sell
    }

    public sealed record MarketQuote(
        decimal? BestBid,
        decimal? BestAsk,
        decimal? BidSize,
        decimal? AskSize,
        DateTimeOffset FetchedAtUtc);

    public sealed record ComboLeg(
        FootballContract Contract,
        TradeAction Action,
        decimal Shares,
        decimal ExecutablePrice)
    {
        public static ComboLeg Buy(FootballContract contract, decimal shares, MarketQuote quote)
        {
            var price = quote.BestAsk ?? throw new ArgumentException("Buy legs require an executable ask.", nameof(quote));
            if (quote.AskSize.HasValue && quote.AskSize.Value < shares)
                throw new ArgumentException("Buy leg shares exceed executable ask size.", nameof(shares));
            return new ComboLeg(contract, TradeAction.Buy, shares, price);
        }

        public static ComboLeg Sell(FootballContract contract, decimal shares, MarketQuote quote)
        {
            var price = quote.BestBid ?? throw new ArgumentException("Sell legs require an executable bid.", nameof(quote));
            if (quote.BidSize.HasValue && quote.BidSize.Value < shares)
                throw new ArgumentException("Sell leg shares exceed executable bid size.", nameof(shares));
            return new ComboLeg(contract, TradeAction.Sell, shares, price);
        }
    }

    public sealed record ComboStatePnl(
        int HomeGoals,
        int AwayGoals,
        double Probability,
        decimal GrossPayout,
        decimal NetPnl,
        IReadOnlyList<string> LegResults);

    public sealed record ComboEvaluation(
        decimal ExpectedPnl,
        decimal ExpectedGrossPayout,
        decimal NetCost,
        decimal Roi,
        decimal MaxLoss,
        decimal MaxProfit,
        double ProbabilityOfLoss,
        double ProbabilityNonNegative,
        decimal BreakEvenAveragePayoff,
        IReadOnlyList<ComboStatePnl> StatePnL);

    public sealed class ComboEvaluator
    {
        public ComboEvaluation Evaluate(ScorelineDistribution distribution, IReadOnlyList<ComboLeg> legs)
        {
            ArgumentNullException.ThrowIfNull(distribution);
            ArgumentNullException.ThrowIfNull(legs);
            if (legs.Count != 2)
                throw new ArgumentException("Combo Lab v1 supports exactly two legs.", nameof(legs));
            ValidateLegs(legs);

            var masks = legs
                .Select(leg => ScorelinePayoffMaskFactory.Build(leg.Contract, distribution.MaxGoals))
                .ToArray();
            var states = new List<ComboStatePnl>();
            var expectedPnl = 0m;
            var expectedGrossPayout = 0m;
            var probabilityOfLoss = 0.0;
            var probabilityNonNegative = 0.0;

            for (var home = 0; home <= distribution.MaxGoals; home++)
            {
                for (var away = 0; away <= distribution.MaxGoals; away++)
                {
                    var probability = distribution.Probability(home, away);
                    if (probability <= 0)
                        continue;

                    var grossPayout = 0m;
                    var netPnl = 0m;
                    var legResults = new List<string>(legs.Count);

                    for (var i = 0; i < legs.Count; i++)
                    {
                        var leg = legs[i];
                        var pays = masks[i].Pays(home, away);
                        var payoffPerShare = pays ? 1m : 0m;
                        var legGrossPayout = leg.Shares * payoffPerShare;
                        grossPayout += legGrossPayout;
                        netPnl += leg.Action switch
                        {
                            TradeAction.Buy => leg.Shares * (payoffPerShare - leg.ExecutablePrice),
                            TradeAction.Sell => leg.Shares * (leg.ExecutablePrice - payoffPerShare),
                            _ => throw new ArgumentOutOfRangeException(nameof(leg.Action), "Unsupported trade action.")
                        };
                        legResults.Add($"{Describe(leg.Contract)}:{(pays ? "pays" : "loses")}");
                    }

                    expectedPnl += (decimal)probability * netPnl;
                    expectedGrossPayout += (decimal)probability * grossPayout;
                    if (netPnl < 0)
                        probabilityOfLoss += probability;
                    else
                        probabilityNonNegative += probability;

                    states.Add(new ComboStatePnl(home, away, probability, grossPayout, netPnl, legResults));
                }
            }

            var netCost = legs.Sum(leg => leg.Action == TradeAction.Buy
                ? leg.Shares * leg.ExecutablePrice
                : -leg.Shares * leg.ExecutablePrice);
            var roi = netCost > 0 ? expectedPnl / netCost : 0m;
            var maxLoss = states.Count == 0 ? 0m : Math.Abs(Math.Min(0m, states.Min(state => state.NetPnl)));
            var maxProfit = states.Count == 0 ? 0m : states.Max(state => state.NetPnl);
            var breakEvenAveragePayoff = legs.Sum(leg => leg.Shares) > 0
                ? netCost / legs.Sum(leg => leg.Shares)
                : 0m;

            return new ComboEvaluation(
                expectedPnl,
                expectedGrossPayout,
                netCost,
                roi,
                maxLoss,
                maxProfit,
                probabilityOfLoss,
                probabilityNonNegative,
                breakEvenAveragePayoff,
                states);
        }

        private static void ValidateLegs(IReadOnlyList<ComboLeg> legs)
        {
            foreach (var leg in legs)
            {
                ArgumentNullException.ThrowIfNull(leg.Contract);
                if (leg.Shares <= 0)
                    throw new ArgumentOutOfRangeException(nameof(legs), "Leg shares must be positive.");
                if (leg.ExecutablePrice < 0 || leg.ExecutablePrice > 1)
                    throw new ArgumentOutOfRangeException(nameof(legs), "Executable price must be between 0 and 1.");
            }

            var fixtureId = legs[0].Contract.FixtureId;
            if (legs.Any(leg => leg.Contract.FixtureId != fixtureId))
                throw new ArgumentException("Combo Lab v1 requires both legs to resolve from the same fixture.", nameof(legs));
        }

        private static string Describe(FootballContract contract) =>
            string.IsNullOrWhiteSpace(contract.Label)
                ? $"{contract.MarketType}:{contract.Selection}"
                : contract.Label;
    }
}
