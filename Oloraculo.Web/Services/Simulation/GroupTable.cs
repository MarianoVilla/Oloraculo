using Oloraculo.Web.Models;

namespace Oloraculo.Web.Services.Simulation
{
    public sealed record SimulatedMatch(string Group, string TeamA, string TeamB, int GoalsA, int GoalsB, bool KnownResult = false);

    public sealed class GroupStanding
    {
        public GroupStanding(string group, string teamId)
        {
            Group = group;
            TeamId = teamId;
        }

        public string Group { get; }
        public string TeamId { get; }
        public int Points { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
        public int GoalDiff => GoalsFor - GoalsAgainst;
    }

    public sealed class GroupTable
    {
        private readonly string _group;
        private readonly Dictionary<string, GroupStanding> _standings;
        private readonly List<SimulatedMatch> _matches = [];
        private readonly IReadOnlyDictionary<string, double> _fifaPoints;

        public GroupTable(Group group, IReadOnlyDictionary<string, double> fifaPoints)
        {
            _group = group.Name;
            _fifaPoints = fifaPoints;
            _standings = group.TeamIds.ToDictionary(teamId => teamId, teamId => new GroupStanding(group.Name, teamId));
        }

        public IReadOnlyList<SimulatedMatch> Matches => _matches;

        public void AddMatch(SimulatedMatch match)
        {
            _matches.Add(match);
            var a = _standings[match.TeamA];
            var b = _standings[match.TeamB];
            a.GoalsFor += match.GoalsA;
            a.GoalsAgainst += match.GoalsB;
            b.GoalsFor += match.GoalsB;
            b.GoalsAgainst += match.GoalsA;

            if (match.GoalsA > match.GoalsB)
                a.Points += 3;
            else if (match.GoalsB > match.GoalsA)
                b.Points += 3;
            else
            {
                a.Points++;
                b.Points++;
            }
        }

        public IReadOnlyList<GroupStanding> Rank() =>
            _standings.Values
                .GroupBy(s => (s.Points, s.GoalDiff, s.GoalsFor))
                .OrderByDescending(g => g.Key.Points)
                .ThenByDescending(g => g.Key.GoalDiff)
                .ThenByDescending(g => g.Key.GoalsFor)
                .SelectMany(g => RankTied(g.ToList()))
                .ToList();

        public static IReadOnlyList<GroupStanding> RankBestThirds(IEnumerable<GroupStanding> thirds, IReadOnlyDictionary<string, double> fifaPoints) =>
            thirds
                .OrderByDescending(t => t.Points)
                .ThenByDescending(t => t.GoalDiff)
                .ThenByDescending(t => t.GoalsFor)
                .ThenByDescending(t => FifaPoints(fifaPoints, t.TeamId))
                .ThenBy(t => t.TeamId, StringComparer.Ordinal)
                .ToList();

        private IReadOnlyList<GroupStanding> RankTied(IReadOnlyList<GroupStanding> tied)
        {
            if (tied.Count <= 1)
                return tied;

            foreach (var criterion in new[] { TieCriterion.HeadToHeadPoints, TieCriterion.HeadToHeadGoalDiff, TieCriterion.HeadToHeadGoalsFor, TieCriterion.TeamConduct, TieCriterion.FifaRanking })
            {
                var groups = tied
                    .GroupBy(team => CriterionValue(team, tied, criterion))
                    .OrderByDescending(g => g.Key)
                    .ToList();

                if (groups.Count <= 1)
                    continue;

                return groups.SelectMany(g => RankTied(g.ToList())).ToList();
            }

            return tied
                .OrderByDescending(t => FifaPoints(_fifaPoints, t.TeamId))
                .ThenBy(t => t.TeamId, StringComparer.Ordinal)
                .ToList();
        }

        private double CriterionValue(GroupStanding team, IReadOnlyCollection<GroupStanding> tied, TieCriterion criterion)
        {
            var tiedIds = tied.Select(t => t.TeamId).ToHashSet(StringComparer.Ordinal);
            return criterion switch
            {
                TieCriterion.HeadToHeadPoints => HeadToHead(team.TeamId, tiedIds).Points,
                TieCriterion.HeadToHeadGoalDiff => HeadToHead(team.TeamId, tiedIds).GoalDiff,
                TieCriterion.HeadToHeadGoalsFor => HeadToHead(team.TeamId, tiedIds).GoalsFor,
                TieCriterion.TeamConduct => 0,
                TieCriterion.FifaRanking => FifaPoints(_fifaPoints, team.TeamId),
                _ => 0
            };
        }

        private (int Points, int GoalsFor, int GoalsAgainst, int GoalDiff) HeadToHead(string teamId, IReadOnlySet<string> tiedIds)
        {
            var points = 0;
            var goalsFor = 0;
            var goalsAgainst = 0;

            foreach (var match in _matches)
            {
                if (!tiedIds.Contains(match.TeamA) || !tiedIds.Contains(match.TeamB))
                    continue;

                int gf;
                int ga;
                if (match.TeamA == teamId)
                {
                    gf = match.GoalsA;
                    ga = match.GoalsB;
                }
                else if (match.TeamB == teamId)
                {
                    gf = match.GoalsB;
                    ga = match.GoalsA;
                }
                else
                {
                    continue;
                }

                goalsFor += gf;
                goalsAgainst += ga;
                if (gf > ga) points += 3;
                else if (gf == ga) points++;
            }

            return (points, goalsFor, goalsAgainst, goalsFor - goalsAgainst);
        }

        private static double FifaPoints(IReadOnlyDictionary<string, double> fifaPoints, string teamId) =>
            fifaPoints.TryGetValue(teamId, out var points) ? points : double.NegativeInfinity;

        private enum TieCriterion
        {
            HeadToHeadPoints,
            HeadToHeadGoalDiff,
            HeadToHeadGoalsFor,
            TeamConduct,
            FifaRanking
        }
    }

}
