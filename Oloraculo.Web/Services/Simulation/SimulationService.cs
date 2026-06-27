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
            var groupStageCutoff = new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero);
            var knockoutResultsList = (await _db.Results
                .Where(r => r.Tournament == "FIFA World Cup")
                .AsNoTracking()
                .ToListAsync(ct))
                .Where(r => r.Date > groupStageCutoff)
                .ToList();
            var fifaPoints = await FifaPointsAsync(ct);
            var teams = groups.SelectMany(g => g.TeamIds).Distinct().ToList();
            var rng = new Random(seed ?? _config.SimulationSeed ?? Environment.TickCount);
            var n = simulations ?? _config.SimulationCount;
            var counters = teams.ToDictionary(t => t, _ => new Counter());
            var predictionContext = await SimulationPredictionContext.CreateAsync(_db, _config, ct);
            var sampler = new MatchSamplerCache(predictionContext.PredictPairAsync);

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
                await RunKnockoutAsync(groupSlots, bestThirds, thirdAssignments, sampler, rng, counters, knockoutResultsList, ct);
            }

            var projection = new TournamentProjection
            {
                Simulations = n,
                ModelName = "Final",
                InputSummaryHash = InputHash(groups, fixtures, knockoutResultsList, n, seed ?? _config.SimulationSeed),
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
            IReadOnlyList<MatchResult> knockoutResultsList,
            CancellationToken ct)
        {
            var winners = new Dictionary<int, string>();
            var thirdByGroup = bestThirds.ToDictionary(t => t.Group, t => t.TeamId, StringComparer.OrdinalIgnoreCase);
            var knockoutResults = knockoutResultsList.ToDictionary(
                r => (r.HomeTeamId, r.AwayTeamId),
                r => (r.HomeGoals, r.AwayGoals));

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
                var home = ResolveSlot(tie, tie.Home);
                var away = ResolveSlot(tie, tie.Away);

                // Use actual result if available and decisive (drawn = went to pens, simulate)
                var knownWinner = KnownKnockoutWinner(home, away, knockoutResults);
                var winner = knownWinner ?? await sampler.KnockoutWinnerAsync(home, away, rng, token);

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

        private static string? KnownKnockoutWinner(
            string homeId, string awayId,
            IReadOnlyDictionary<(string, string), (int HomeGoals, int AwayGoals)> results)
        {
            if (results.TryGetValue((homeId, awayId), out var r))
            {
                if (r.HomeGoals > r.AwayGoals) return homeId;
                if (r.AwayGoals > r.HomeGoals) return awayId;
            }
            else if (results.TryGetValue((awayId, homeId), out var rFlipped))
            {
                if (rFlipped.HomeGoals > rFlipped.AwayGoals) return awayId;
                if (rFlipped.AwayGoals > rFlipped.HomeGoals) return homeId;
            }
            return null; // empate AET → penales → simular
        }

        public static (int Home, int Away) SampleScoreFromPrediction(MatchPredictionResult prediction, Random rng) =>
            MatchSamplerCache.SampleScoreFromPrediction(prediction, rng);

        private async Task<IReadOnlyDictionary<string, double>> FifaPointsAsync(CancellationToken ct) =>
            (await _db.Ratings.AsNoTracking()
                .Where(r => r.Type == RatingTypeEnum.Fifa)
                .ToListAsync(ct))
                .GroupBy(r => r.TeamId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.AsOf).First().Value, StringComparer.OrdinalIgnoreCase);

        private static string InputHash(IReadOnlyList<Group> groups, IReadOnlyList<Fixture> fixtures, IReadOnlyList<MatchResult> knockoutResults, int simulations, int? seed)
        {
            var groupTokens = groups.Select(g => $"{g.Name}:{string.Join("-", g.TeamIds)}");
            var resultTokens = fixtures
                .Where(f => f is { IsPlayed: true, HomeGoals: not null, AwayGoals: not null })
                .OrderBy(f => f.Id)
                .Select(f => $"{f.Id}:{f.HomeGoals}-{f.AwayGoals}");
            var knockoutTokens = knockoutResults
                .OrderBy(r => r.Date).ThenBy(r => r.HomeTeamId)
                .Select(r => $"{r.HomeTeamId}:{r.AwayTeamId}:{r.HomeGoals}-{r.AwayGoals}");
            return CryptoUtil.GetSha256($"{simulations}|{seed}|{string.Join("|", groupTokens)}|{string.Join("|", resultTokens)}|{string.Join("|", knockoutTokens)}");
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
