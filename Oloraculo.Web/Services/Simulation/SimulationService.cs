using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;
using System.Diagnostics;

namespace Oloraculo.Web.Services.Simulation
{
    public class SimulationService
    {
        private readonly OloraculoDbContext _db;
        private readonly PredictionService _prediction;
        private readonly SnapshotService _snapshots;
        private readonly OloraculoConfig _config;

        public SimulationService(OloraculoDbContext db, PredictionService prediction, 
            SnapshotService snapshots, IOptions<OloraculoConfig> options)
        {
            _db = db;
            _prediction = prediction;
            _snapshots = snapshots;
            _config = options.Value;
        }

        public async Task<TournamentProjection> RunAsync(int? simulations = null, int? seed = null, bool saveSnapshot = true, CancellationToken ct = default)
        {
            var groups = await _db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
            var fixtures = await _db.Fixtures.AsNoTracking().ToListAsync(ct);
            var fifaPoints = await FifaPointsAsync(ct);
            var teams = groups.SelectMany(g => g.TeamIds).Distinct().ToList();
            var rng = new Random(seed ?? _config.SimulationSeed ?? Environment.TickCount);
            var n = simulations ?? _config.SimulationCount;
            var counters = teams.ToDictionary(t => t, _ => new Counter());
            var predictionContext = await SimulationPredictionContext.CreateAsync(_db, _config, ct);
            var sampler = new MatchSamplerCache(predictionContext.PredictPairAsync);
            var knownKnockout = await _db.KnockoutMatches.AsNoTracking().ToDictionaryAsync(m => m.MatchNumber, ct);

            for (var i = 0; i < n; i++)
            {
                var groupSlots = new Dictionary<string, GroupSlots>(StringComparer.OrdinalIgnoreCase);
                var thirds = new List<GroupStanding>();

                foreach (var group in groups)
                {
                    var table = await SimulateGroupAsync(group, fixtures, fifaPoints, sampler, rng, ct);
                    var ranked = table.Rank();
                    for (var pos = 0; pos < ranked.Count; pos++)
                        counters[ranked[pos].TeamId].GroupPoints += ranked[pos].Points;

                    counters[ranked[0].TeamId].WinGroup++;
                    groupSlots[group.Name] = new GroupSlots(ranked[0].TeamId, ranked[1].TeamId, ranked[2].TeamId);
                    thirds.Add(ranked[2]);
                }

                var bestThirds = GroupTable.RankBestThirds(thirds, fifaPoints).Take(8).ToList();
                foreach (var group in groupSlots.Values)
                {
                    counters[group.Winner].Qualify++;
                    counters[group.RunnerUp].Qualify++;
                }
                foreach (var third in bestThirds)
                    counters[third.TeamId].Qualify++;

                var thirdAssignments = WorldCup2026Bracket.AssignThirdPlaceGroups(bestThirds.Select(t => t.Group).ToList());
                await RunKnockoutAsync(groupSlots, bestThirds, thirdAssignments, sampler, rng, counters, knownKnockout, ct);
            }

            var projection = new TournamentProjection
            {
                Simulations = n,
                ModelName = "Final",
                InputSummaryHash = InputHash(groups, fixtures, n, seed ?? _config.SimulationSeed),
                Teams = teams.Select(team =>
                {
                    var group = groups.First(g => g.TeamIds.Contains(team)).Name;
                    var c = counters[team];
                    return new TeamTournamentProbability
                    {
                        TeamId = team,
                        Group = group,
                        WinGroup = c.WinGroup / (double)n,
                        Qualify = c.Qualify / (double)n,
                        ReachRoundOf16 = c.R16 / (double)n,
                        ReachQuarterFinal = c.Qf / (double)n,
                        ReachSemiFinal = c.Sf / (double)n,
                        ReachFinal = c.Final / (double)n,
                        WinTournament = c.Champion / (double)n,
                        ExpectedGroupPoints = Math.Round(c.GroupPoints / (double)n, 2)
                    };
                }).OrderByDescending(t => t.WinTournament).ToList()
            };

            if (saveSnapshot)
                await _snapshots.SaveTournamentAsync(projection, ct);
            return projection;
        }

        /// <summary>
        /// Construye un unico cuadro "mas probable" (determinista, sin azar): ordena cada grupo
        /// usando los resultados ya jugados mas el marcador mas probable de los pendientes,
        /// define clasificados y resuelve todo el mata-mata oficial 2026 hasta el campeon.
        /// </summary>
        public async Task<DeterministicProjection> ProjectMostLikelyAsync(CancellationToken ct = default)
        {
            var groups = await _db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
            var fixtures = await _db.Fixtures.AsNoTracking().ToListAsync(ct);
            var fifaPoints = await FifaPointsAsync(ct);
            var predictionContext = await SimulationPredictionContext.CreateAsync(_db, _config, ct);
            var sampler = new MatchSamplerCache(predictionContext.PredictPairAsync);

            var groupSlots = new Dictionary<string, GroupSlots>(StringComparer.OrdinalIgnoreCase);
            var rankedByGroup = new Dictionary<string, IReadOnlyList<GroupStanding>>(StringComparer.OrdinalIgnoreCase);
            var thirds = new List<GroupStanding>();

            foreach (var group in groups)
            {
                var table = new GroupTable(group, fifaPoints);
                for (var i = 0; i < group.TeamIds.Count; i++)
                {
                    for (var j = i + 1; j < group.TeamIds.Count; j++)
                    {
                        var a = group.TeamIds[i];
                        var b = group.TeamIds[j];
                        var known = KnownFixtureScore(group.Name, a, b, fixtures);
                        var score = known ?? MostLikelyScore(await sampler.GetPredictionAsync(a, b, ct));
                        table.AddMatch(new SimulatedMatch(group.Name, a, b, score.Home, score.Away, known.HasValue));
                    }
                }

                var ranked = table.Rank();
                rankedByGroup[group.Name] = ranked;
                groupSlots[group.Name] = new GroupSlots(ranked[0].TeamId, ranked[1].TeamId, ranked[2].TeamId);
                thirds.Add(ranked[2]);
            }

            var bestThirds = GroupTable.RankBestThirds(thirds, fifaPoints).Take(8).ToList();
            var qualifiedThirdGroups = bestThirds.Select(t => t.Group).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var groupProjections = groups.Select(group =>
            {
                var ranked = rankedByGroup[group.Name];
                var standings = ranked.Select((standing, index) => new DeterministicStanding(
                    index + 1,
                    standing.TeamId,
                    standing.Points,
                    standing.GoalsFor,
                    standing.GoalsAgainst,
                    standing.GoalDiff,
                    index switch
                    {
                        0 => QualificationKind.Winner,
                        1 => QualificationKind.RunnerUp,
                        2 => qualifiedThirdGroups.Contains(group.Name) ? QualificationKind.ThirdQualified : QualificationKind.ThirdOut,
                        _ => QualificationKind.Eliminated
                    })).ToList();
                return new DeterministicGroup(group.Name, standings);
            }).ToList();

            var thirdAssignments = WorldCup2026Bracket.AssignThirdPlaceGroups(bestThirds.Select(t => t.Group).ToList());
            var thirdByGroup = bestThirds.ToDictionary(t => t.Group, t => t.TeamId, StringComparer.OrdinalIgnoreCase);

            var winners = new Dictionary<int, string>();
            var ties = new List<DeterministicTie>();

            foreach (var tie in WorldCup2026Bracket.KnockoutTies)
            {
                var homeId = ResolveSlot(tie, tie.Home);
                var awayId = ResolveSlot(tie, tie.Away);
                var prediction = await sampler.GetPredictionAsync(homeId, awayId, ct);
                var (winner, home, away, penalties) = ResolveDeterministicTie(homeId, awayId, prediction);
                winners[tie.Id] = winner;
                ties.Add(new DeterministicTie(tie.Id, tie.Stage, homeId, awayId, winner, home, away, penalties));
            }

            return new DeterministicProjection(groupProjections, ties, winners[WorldCup2026Bracket.Final.Id]);

            string ResolveSlot(BracketTie tie, BracketSlot slot) =>
                slot.Kind switch
                {
                    BracketSlotKindEnum.GroupWinner => groupSlots[slot.Group!].Winner,
                    BracketSlotKindEnum.GroupRunnerUp => groupSlots[slot.Group!].RunnerUp,
                    BracketSlotKindEnum.GroupThird => thirdByGroup[thirdAssignments[tie.Id]],
                    BracketSlotKindEnum.WinnerOfTie => winners[slot.TieId!.Value],
                    _ => throw new InvalidOperationException($"Unsupported bracket slot {slot.Kind}.")
                };
        }

        private static (int Home, int Away) MostLikelyScore(MatchPredictionResult prediction)
        {
            if (prediction.BestPrediction.MostLikelyScore is { } score)
                return score;

            return prediction.BestPrediction.Outcome.TopPick switch
            {
                "Home" => (1, 0),
                "Away" => (0, 1),
                _ => (1, 1)
            };
        }

        private static (string Winner, int Home, int Away, bool Penalties) ResolveDeterministicTie(
            string homeId, string awayId, MatchPredictionResult prediction)
        {
            var score = MostLikelyScore(prediction);
            if (score.Home > score.Away)
                return (homeId, score.Home, score.Away, false);
            if (score.Away > score.Home)
                return (awayId, score.Home, score.Away, false);

            // Empate en el marcador mas probable: define el favorito por probabilidad de victoria.
            var outcome = prediction.BestPrediction.Outcome;
            var winner = outcome.HomeWin >= outcome.AwayWin ? homeId : awayId;
            return (winner, score.Home, score.Away, true);
        }

        private async Task<GroupTable> SimulateGroupAsync(
            Group group,
            IReadOnlyList<Fixture> fixtures,
            IReadOnlyDictionary<string, double> fifaPoints,
            MatchSamplerCache sampler,
            Random rng,
            CancellationToken ct)
        {
            var table = new GroupTable(group, fifaPoints);
            for (var i = 0; i < group.TeamIds.Count; i++)
            {
                for (var j = i + 1; j < group.TeamIds.Count; j++)
                {
                    var a = group.TeamIds[i];
                    var b = group.TeamIds[j];
                    var known = KnownFixtureScore(group.Name, a, b, fixtures);
                    var score = known ?? await sampler.SampleScoreAsync(a, b, rng, ct);
                    table.AddMatch(new SimulatedMatch(group.Name, a, b, score.Home, score.Away, known.HasValue));
                }
            }

            return table;
        }

        private static (int Home, int Away)? KnownFixtureScore(string group, string a, string b, IReadOnlyList<Fixture> fixtures)
        {
            var fixture = fixtures.FirstOrDefault(f =>
                string.Equals(f.Group, group, StringComparison.OrdinalIgnoreCase) &&
                (f.HomeTeamId == a && f.AwayTeamId == b || f.HomeTeamId == b && f.AwayTeamId == a));

            if (fixture is not { IsPlayed: true, HomeGoals: int homeGoals, AwayGoals: int awayGoals })
                return null;

            return fixture.HomeTeamId == a ? (homeGoals, awayGoals) : (awayGoals, homeGoals);
        }

        private async Task RunKnockoutAsync(
            IReadOnlyDictionary<string, GroupSlots> groupSlots,
            IReadOnlyList<GroupStanding> bestThirds,
            IReadOnlyDictionary<int, string> thirdAssignments,
            MatchSamplerCache sampler,
            Random rng,
            Dictionary<string, Counter> counters,
            IReadOnlyDictionary<int, KnockoutMatch> knownKnockout,
            CancellationToken ct)
        {
            var winners = new Dictionary<int, string>();
            var thirdByGroup = bestThirds.ToDictionary(t => t.Group, t => t.TeamId, StringComparer.OrdinalIgnoreCase);

            await RunRoundAsync(WorldCup2026Bracket.RoundOf32, team => counters[team].R16++, ct);
            await RunRoundAsync(WorldCup2026Bracket.RoundOf16, team => counters[team].Qf++, ct);
            await RunRoundAsync(WorldCup2026Bracket.QuarterFinals, team => counters[team].Sf++, ct);
            await RunRoundAsync(WorldCup2026Bracket.SemiFinals, team => counters[team].Final++, ct);

            var champion = await PlayTieAsync(WorldCup2026Bracket.Final, ct);
            counters[champion].Champion++;

            async Task RunRoundAsync(IReadOnlyList<BracketTie> ties, Action<string> countWinner, CancellationToken token)
            {
                foreach (var tie in ties)
                {
                    var winner = await PlayTieAsync(tie, token);
                    countWinner(winner);
                }
            }

            async Task<string> PlayTieAsync(BracketTie tie, CancellationToken token)
            {
                knownKnockout.TryGetValue(tie.Id, out var known);
                var home = known?.ConfirmedHomeTeamId ?? ResolveSlot(tie, tie.Home);
                var away = known?.ConfirmedAwayTeamId ?? ResolveSlot(tie, tie.Away);
                var winner = known is { IsPlayed: true, WinnerTeamId: not null }
                    ? known.WinnerTeamId
                    : await sampler.KnockoutWinnerAsync(home, away, rng, token);
                winners[tie.Id] = winner;
                return winner;
            }

            string ResolveSlot(BracketTie tie, BracketSlot slot) =>
                slot.Kind switch
                {
                    BracketSlotKindEnum.GroupWinner => groupSlots[slot.Group!].Winner,
                    BracketSlotKindEnum.GroupRunnerUp => groupSlots[slot.Group!].RunnerUp,
                    BracketSlotKindEnum.GroupThird => thirdByGroup[thirdAssignments[tie.Id]],
                    BracketSlotKindEnum.WinnerOfTie => winners[slot.TieId!.Value],
                    _ => throw new InvalidOperationException($"Unsupported bracket slot {slot.Kind}.")
                };
        }

        public static (int Home, int Away) SampleScoreFromPrediction(MatchPredictionResult prediction, Random rng) =>
            MatchSamplerCache.SampleScoreFromPrediction(prediction, rng);

        private async Task<IReadOnlyDictionary<string, double>> FifaPointsAsync(CancellationToken ct) =>
            (await _db.Ratings.AsNoTracking()
                .Where(r => r.Type == RatingTypeEnum.Fifa)
                .ToListAsync(ct))
                .GroupBy(r => r.TeamId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.AsOf).First().Value, StringComparer.OrdinalIgnoreCase);

        private static string InputHash(IReadOnlyList<Group> groups, IReadOnlyList<Fixture> fixtures, int simulations, int? seed)
        {
            var groupTokens = groups.Select(g => $"{g.Name}:{string.Join("-", g.TeamIds)}");
            var resultTokens = fixtures
                .Where(f => f is { IsPlayed: true, HomeGoals: not null, AwayGoals: not null })
                .OrderBy(f => f.Id)
                .Select(f => $"{f.Id}:{f.HomeGoals}-{f.AwayGoals}");
            return CryptoUtil.GetSha256($"{simulations}|{seed}|{string.Join("|", groupTokens)}|{string.Join("|", resultTokens)}");
        }

        private sealed record GroupSlots(string Winner, string RunnerUp, string Third);
        public sealed record BracketSlot(BracketSlotKindEnum Kind, string? Group = null, int? TieId = null, IReadOnlyList<string>? ThirdPlaceGroupOptions = null);

        public sealed record BracketTie(int Id, KnockoutStageEnum Stage, BracketSlot Home, BracketSlot Away);

        private sealed class Counter
        {
            public int WinGroup;
            public int Qualify;
            public int R16;
            public int Qf;
            public int Sf;
            public int Final;
            public int Champion;
            public int GroupPoints;
        }
    }

}
