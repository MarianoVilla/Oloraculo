using Oloraculo.Web.ComboLab.Contracts;

namespace Oloraculo.Web.ComboLab.Pricing
{
    public sealed record ScorelineState(int HomeGoals, int AwayGoals, double Probability);

    public sealed class ScorelinePayoffMask
    {
        private readonly Func<int, int, bool> _pays;

        public ScorelinePayoffMask(FootballContract contract, Func<int, int, bool> pays)
        {
            Contract = contract;
            _pays = pays;
        }

        public FootballContract Contract { get; }

        public bool Pays(int homeGoals, int awayGoals) => _pays(homeGoals, awayGoals);
    }

    public static class ScorelinePayoffMaskFactory
    {
        public static ScorelinePayoffMask Build(FootballContract contract, int maxGoals)
        {
            ArgumentNullException.ThrowIfNull(contract);
            if (string.IsNullOrWhiteSpace(contract.FixtureId))
                throw new ArgumentException("FixtureId is required.", nameof(contract));
            if (contract.Period != PeriodScope.Regulation90)
                throw new ArgumentException("Only Regulation90 scoreline contracts are supported in Combo Lab v1.", nameof(contract));
            if (maxGoals < 0)
                throw new ArgumentOutOfRangeException(nameof(maxGoals), "Max goals must be non-negative.");

            return contract.MarketType switch
            {
                FootballMarketType.Moneyline => Moneyline(contract),
                FootballMarketType.Spread => Spread(contract),
                FootballMarketType.TeamTotal => TeamTotal(contract),
                FootballMarketType.MatchTotal => MatchTotal(contract),
                FootballMarketType.BothTeamsToScore => BothTeamsToScore(contract),
                FootballMarketType.ExactScore => ExactScore(contract, maxGoals),
                _ => throw new ArgumentOutOfRangeException(nameof(contract), "Unsupported market type.")
            };
        }

        private static ScorelinePayoffMask Moneyline(FootballContract contract) => contract.Selection switch
        {
            ContractSelection.Home => new ScorelinePayoffMask(contract, (home, away) => home > away),
            ContractSelection.Draw => new ScorelinePayoffMask(contract, (home, away) => home == away),
            ContractSelection.Away => new ScorelinePayoffMask(contract, (home, away) => away > home),
            ContractSelection.NotHome => new ScorelinePayoffMask(contract, (home, away) => home <= away),
            ContractSelection.NotDraw => new ScorelinePayoffMask(contract, (home, away) => home != away),
            ContractSelection.NotAway => new ScorelinePayoffMask(contract, (home, away) => away <= home),
            _ => throw new ArgumentException("Moneyline contracts require Home, Draw, Away, or their complements.", nameof(contract))
        };

        private static ScorelinePayoffMask Spread(FootballContract contract)
        {
            var line = RequireNonIntegerSignedLine(contract, "Spread");
            var team = contract.Team ?? throw new ArgumentException("Spread contracts require a team side.", nameof(contract));
            return contract.Selection switch
            {
                ContractSelection.Yes => new ScorelinePayoffMask(contract, (home, away) => CoversSpread(team, line, home, away)),
                ContractSelection.No => new ScorelinePayoffMask(contract, (home, away) => !CoversSpread(team, line, home, away)),
                _ => throw new ArgumentException("Spread contracts require Yes or No selection.", nameof(contract))
            };
        }

        private static ScorelinePayoffMask TeamTotal(FootballContract contract)
        {
            var line = RequireNonIntegerLine(contract, "Team total");
            var team = contract.Team ?? throw new ArgumentException("Team total contracts require a team side.", nameof(contract));
            return contract.Selection switch
            {
                ContractSelection.Over => new ScorelinePayoffMask(contract, (home, away) => TeamGoals(team, home, away) > line),
                ContractSelection.Under => new ScorelinePayoffMask(contract, (home, away) => TeamGoals(team, home, away) < line),
                _ => throw new ArgumentException("Team total contracts require Over or Under selection.", nameof(contract))
            };
        }

        private static ScorelinePayoffMask MatchTotal(FootballContract contract)
        {
            var line = RequireNonIntegerLine(contract, "Match total");
            return contract.Selection switch
            {
                ContractSelection.Over => new ScorelinePayoffMask(contract, (home, away) => home + away > line),
                ContractSelection.Under => new ScorelinePayoffMask(contract, (home, away) => home + away < line),
                _ => throw new ArgumentException("Match total contracts require Over or Under selection.", nameof(contract))
            };
        }

        private static ScorelinePayoffMask BothTeamsToScore(FootballContract contract) => contract.Selection switch
        {
            ContractSelection.Yes => new ScorelinePayoffMask(contract, (home, away) => home > 0 && away > 0),
            ContractSelection.No => new ScorelinePayoffMask(contract, (home, away) => home == 0 || away == 0),
            _ => throw new ArgumentException("BTTS contracts require Yes or No selection.", nameof(contract))
        };

        private static ScorelinePayoffMask ExactScore(FootballContract contract, int maxGoals)
        {
            var exactHome = contract.ExactHomeGoals ?? throw new ArgumentException("Exact score contracts require home goals.", nameof(contract));
            var exactAway = contract.ExactAwayGoals ?? throw new ArgumentException("Exact score contracts require away goals.", nameof(contract));
            if (exactHome < 0 || exactAway < 0)
                throw new ArgumentOutOfRangeException(nameof(contract), "Exact score goals must be non-negative.");
            if (exactHome > maxGoals || exactAway > maxGoals)
                throw new ArgumentOutOfRangeException(nameof(contract), "Exact score lies outside the modelled scoreline grid.");

            return contract.Selection switch
            {
                ContractSelection.Yes => new ScorelinePayoffMask(contract, (home, away) => home == exactHome && away == exactAway),
                ContractSelection.No => new ScorelinePayoffMask(contract, (home, away) => home != exactHome || away != exactAway),
                _ => throw new ArgumentException("Exact score contracts require Yes or No selection.", nameof(contract))
            };
        }

        private static decimal RequireNonIntegerLine(FootballContract contract, string marketName)
        {
            var line = contract.Line ?? throw new ArgumentException($"{marketName} contracts require a line.", nameof(contract));
            if (line < 0)
                throw new ArgumentOutOfRangeException(nameof(contract), $"{marketName} line must be non-negative.");
            if (line == decimal.Truncate(line))
                throw new ArgumentException($"{marketName} integer lines need explicit push/settlement handling and are unsupported in v1.", nameof(contract));
            return line;
        }

        private static decimal RequireNonIntegerSignedLine(FootballContract contract, string marketName)
        {
            var line = contract.Line ?? throw new ArgumentException($"{marketName} contracts require a line.", nameof(contract));
            if (line == decimal.Truncate(line))
                throw new ArgumentException($"{marketName} integer lines need explicit push/settlement handling and are unsupported in v1.", nameof(contract));
            return line;
        }

        private static int TeamGoals(TeamSide team, int homeGoals, int awayGoals) =>
            team == TeamSide.Home ? homeGoals : awayGoals;

        private static int OpponentGoals(TeamSide team, int homeGoals, int awayGoals) =>
            team == TeamSide.Home ? awayGoals : homeGoals;

        private static bool CoversSpread(TeamSide team, decimal line, int homeGoals, int awayGoals) =>
            TeamGoals(team, homeGoals, awayGoals) + line > OpponentGoals(team, homeGoals, awayGoals);
    }
}
