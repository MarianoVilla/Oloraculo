using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;
using Oloraculo.Web.Predictors;
using Oloraculo.Web.Services.Simulation;
using static Oloraculo.Web.Services.Simulation.SimulationService;

namespace Oloraculo.Web.Services
{
    public class KnockoutBracketService
    {
        public const string KnockoutFixturePrefix = "ko:";

        private readonly OloraculoDbContext _db;
        private readonly PredictionService _prediction;

        public KnockoutBracketService(OloraculoDbContext db, PredictionService prediction)
        {
            _db = db;
            _prediction = prediction;
        }

        public async Task<BracketProjection> BuildAsync(bool upsertFixtures = true, CancellationToken ct = default)
        {
            var generatedAt = DateTimeOffset.UtcNow;
            var groups = await _db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
            var fixtures = await _db.Fixtures.AsNoTracking().ToListAsync(ct);
            var fifaPoints = await FifaPointsAsync(ct);
            var predictors = await _prediction.BuildPredictorsAsync(ct);
            var (groupSlots, bestThirds) = await ResolveGroupSlotsAsync(groups, fixtures, fifaPoints, predictors, ct);
            var thirdAssignments = WorldCup2026Bracket.AssignThirdPlaceGroups(bestThirds.Select(t => t.Group).ToList());
            var thirdByGroup = bestThirds.ToDictionary(t => t.Group, t => t.TeamId, StringComparer.OrdinalIgnoreCase);
            var knockoutFixtures = upsertFixtures
                ? await _db.Fixtures.Where(f => f.Id.StartsWith(KnockoutFixturePrefix)).ToDictionaryAsync(f => f.Id, StringComparer.Ordinal, ct)
                : fixtures.Where(f => f.Id.StartsWith(KnockoutFixturePrefix)).ToDictionary(f => f.Id, StringComparer.Ordinal);
            var latestSnapshots = await LatestSnapshotIdsAsync(ct);
            var evaluatedFixtures = await _db.Evaluations.AsNoTracking()
                .Select(e => e.FixtureId)
                .Distinct()
                .ToHashSetAsync(StringComparer.Ordinal, ct);

            var winners = new Dictionary<int, string>();
            var ties = new List<BracketTieProjection>();

            foreach (var tie in WorldCup2026Bracket.KnockoutTies)
            {
                var resolvedHome = ResolveSlot(tie, tie.Home);
                var resolvedAway = ResolveSlot(tie, tie.Away);
                var fixture = upsertFixtures
                    ? UpsertKnockoutFixture(knockoutFixtures, tie, resolvedHome, resolvedAway)
                    : SnapshotFixture(knockoutFixtures, tie, resolvedHome, resolvedAway);
                var projection = await ProjectTieAsync(tie, fixture, latestSnapshots, evaluatedFixtures, predictors, ct);
                ties.Add(projection);

                var advancing = projection.ActualWinnerTeamId ?? projection.PredictedWinnerTeamId;
                if (!string.IsNullOrWhiteSpace(advancing))
                    winners[tie.Id] = advancing;
            }

            if (upsertFixtures)
                await _db.SaveChangesAsync(ct);

            return new BracketProjection
            {
                GeneratedAt = generatedAt,
                ModelName = "Cuadro",
                InputSummaryHash = InputHash(groups, fixtures, ties),
                Ties = ties
            };

            string? ResolveSlot(BracketTie tie, BracketSlot slot) =>
                slot.Kind switch
                {
                    BracketSlotKindEnum.GroupWinner => groupSlots.TryGetValue(slot.Group!, out var slots) ? slots.Winner : null,
                    BracketSlotKindEnum.GroupRunnerUp => groupSlots.TryGetValue(slot.Group!, out var slots) ? slots.RunnerUp : null,
                    BracketSlotKindEnum.GroupThird => thirdAssignments.TryGetValue(tie.Id, out var group) && thirdByGroup.TryGetValue(group, out var third) ? third : null,
                    BracketSlotKindEnum.WinnerOfTie => slot.TieId.HasValue && winners.TryGetValue(slot.TieId.Value, out var winner) ? winner : null,
                    _ => null
                };
        }

        public static string FixtureId(int tieId) => $"{KnockoutFixturePrefix}{tieId}";

        public static string StageLabel(KnockoutStageEnum stage) => stage switch
        {
            KnockoutStageEnum.RoundOf32 => "16avos",
            KnockoutStageEnum.RoundOf16 => "Octavos",
            KnockoutStageEnum.QuarterFinal => "Cuartos",
            KnockoutStageEnum.SemiFinal => "Semis",
            KnockoutStageEnum.Final => "Final",
            _ => stage.ToString()
        };

        public static string SlotLabel(BracketSlot slot) => slot.Kind switch
        {
            BracketSlotKindEnum.GroupWinner => $"1{slot.Group}",
            BracketSlotKindEnum.GroupRunnerUp => $"2{slot.Group}",
            BracketSlotKindEnum.GroupThird => $"3 {string.Join("/", slot.ThirdPlaceGroupOptions ?? [])}",
            BracketSlotKindEnum.WinnerOfTie => $"G{slot.TieId}",
            _ => "-"
        };

        private async Task<(IReadOnlyDictionary<string, GroupSlots> GroupSlots, IReadOnlyList<GroupStanding> BestThirds)> ResolveGroupSlotsAsync(
            IReadOnlyList<Group> groups,
            IReadOnlyList<Fixture> fixtures,
            IReadOnlyDictionary<string, double> fifaPoints,
            IReadOnlyList<IPredictor> predictors,
            CancellationToken ct)
        {
            var groupSlots = new Dictionary<string, GroupSlots>(StringComparer.OrdinalIgnoreCase);
            var thirds = new List<GroupStanding>();

            foreach (var group in groups)
            {
                var table = new GroupTable(group, fifaPoints);
                for (var i = 0; i < group.TeamIds.Count; i++)
                {
                    for (var j = i + 1; j < group.TeamIds.Count; j++)
                    {
                        var home = group.TeamIds[i];
                        var away = group.TeamIds[j];
                        var score = await ScoreForGroupPairAsync(group.Name, home, away, fixtures, predictors, ct);
                        table.AddMatch(new SimulatedMatch(group.Name, home, away, score.Home, score.Away, score.Known));
                    }
                }

                var ranked = table.Rank();
                groupSlots[group.Name] = new GroupSlots(ranked[0].TeamId, ranked[1].TeamId, ranked[2].TeamId);
                thirds.Add(ranked[2]);
            }

            var bestThirds = GroupTable.RankBestThirds(thirds, fifaPoints).Take(8).ToList();
            return (groupSlots, bestThirds);
        }

        private async Task<(int Home, int Away, bool Known)> ScoreForGroupPairAsync(
            string group,
            string home,
            string away,
            IReadOnlyList<Fixture> fixtures,
            IReadOnlyList<IPredictor> predictors,
            CancellationToken ct)
        {
            var fixture = fixtures.FirstOrDefault(f =>
                string.Equals(f.Group, group, StringComparison.OrdinalIgnoreCase) &&
                (f.HomeTeamId == home && f.AwayTeamId == away || f.HomeTeamId == away && f.AwayTeamId == home));
            fixture ??= new Fixture
            {
                Id = Fixture.GenerateFixtureId(group, home, away),
                Group = group,
                HomeTeamId = home,
                AwayTeamId = away,
                NeutralVenue = true
            };

            (int Home, int Away) score;
            var known = fixture is { IsPlayed: true, HomeGoals: int homeGoals, AwayGoals: int awayGoals };
            if (known)
            {
                score = (fixture.HomeGoals!.Value, fixture.AwayGoals!.Value);
            }
            else
            {
                var result = await _prediction.PredictAsync(fixture, predictors, ct);
                score = PredictionScore(result.BestPrediction);
            }

            return fixture.HomeTeamId == home
                ? (score.Home, score.Away, known)
                : (score.Away, score.Home, known);
        }

        private Fixture UpsertKnockoutFixture(
            Dictionary<string, Fixture> fixtures,
            BracketTie tie,
            string? resolvedHome,
            string? resolvedAway)
        {
            var fixtureId = FixtureId(tie.Id);
            var home = resolvedHome ?? "";
            var away = resolvedAway ?? "";
            if (!fixtures.TryGetValue(fixtureId, out var fixture))
            {
                fixture = SnapshotFixture(fixtures, tie, home, away);
                _db.Fixtures.Add(fixture);
                fixtures[fixtureId] = fixture;
                return fixture;
            }

            fixture.Group = "KO";
            fixture.Status = StageLabel(tie.Stage);
            fixture.Source = "derived knockout bracket";
            if (!fixture.IsPlayed)
            {
                fixture.HomeTeamId = home;
                fixture.AwayTeamId = away;
                fixture.HomeGoals = null;
                fixture.AwayGoals = null;
                fixture.WinnerTeamId = null;
            }

            return fixture;
        }

        private static Fixture SnapshotFixture(
            Dictionary<string, Fixture> fixtures,
            BracketTie tie,
            string? resolvedHome,
            string? resolvedAway)
        {
            var fixtureId = FixtureId(tie.Id);
            if (fixtures.TryGetValue(fixtureId, out var fixture))
                return fixture;

            return new Fixture
            {
                Id = fixtureId,
                Group = "KO",
                HomeTeamId = resolvedHome ?? "",
                AwayTeamId = resolvedAway ?? "",
                NeutralVenue = true,
                Status = StageLabel(tie.Stage),
                Source = "derived knockout bracket"
            };
        }

        private async Task<BracketTieProjection> ProjectTieAsync(
            BracketTie tie,
            Fixture fixture,
            IReadOnlyDictionary<string, int> latestSnapshots,
            IReadOnlySet<string> evaluatedFixtures,
            IReadOnlyList<IPredictor> predictors,
            CancellationToken ct)
        {
            var projection = new BracketTieProjection
            {
                TieId = tie.Id,
                FixtureId = fixture.Id,
                Stage = tie.Stage.ToString(),
                StageLabel = StageLabel(tie.Stage),
                HomeSlotLabel = SlotLabel(tie.Home),
                AwaySlotLabel = SlotLabel(tie.Away),
                HomeTeamId = string.IsNullOrWhiteSpace(fixture.HomeTeamId) ? null : fixture.HomeTeamId,
                AwayTeamId = string.IsNullOrWhiteSpace(fixture.AwayTeamId) ? null : fixture.AwayTeamId,
                IsPlayed = fixture is { IsPlayed: true, HomeGoals: not null, AwayGoals: not null },
                ActualHomeGoals = fixture.HomeGoals,
                ActualAwayGoals = fixture.AwayGoals,
                ActualWinnerTeamId = ActualWinner(fixture),
                LatestSnapshotId = latestSnapshots.TryGetValue(fixture.Id, out var snapshotId) ? snapshotId : null,
                HasEvaluation = evaluatedFixtures.Contains(fixture.Id)
            };

            if (string.IsNullOrWhiteSpace(fixture.HomeTeamId) || string.IsNullOrWhiteSpace(fixture.AwayTeamId))
            {
                projection.Error = "El cruce todavía no tiene equipos resueltos.";
                return projection;
            }

            var result = await _prediction.PredictAsync(fixture, predictors, ct);
            var prediction = result.BestPrediction;
            var (homeAdvance, awayAdvance) = AdvanceProbabilities(prediction.Outcome);
            var score = PredictionScore(prediction);
            projection.Prediction = prediction;
            projection.PredictionModelName = prediction.PredictorName;
            projection.HomeAdvanceProbability = homeAdvance;
            projection.AwayAdvanceProbability = awayAdvance;
            projection.PredictedHomeGoals = score.Home;
            projection.PredictedAwayGoals = score.Away;
            projection.PredictedWinnerTeamId = homeAdvance >= awayAdvance ? fixture.HomeTeamId : fixture.AwayTeamId;
            return projection;
        }

        private async Task<IReadOnlyDictionary<string, int>> LatestSnapshotIdsAsync(CancellationToken ct)
        {
            var snapshots = await _db.Snapshots.AsNoTracking()
                .Where(s => s.Kind == "match" && s.FixtureId != null && s.FixtureId.StartsWith(KnockoutFixturePrefix))
                .Select(s => new { s.FixtureId, s.Id, s.CreatedAt })
                .ToListAsync(ct);

            return snapshots
                .GroupBy(s => s.FixtureId!, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).First().Id,
                    StringComparer.Ordinal);
        }

        private async Task<IReadOnlyDictionary<string, double>> FifaPointsAsync(CancellationToken ct) =>
            (await _db.Ratings.AsNoTracking()
                .Where(r => r.Type == RatingTypeEnum.Fifa)
                .ToListAsync(ct))
                .GroupBy(r => r.TeamId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.AsOf).First().Value, StringComparer.OrdinalIgnoreCase);

        private static (double Home, double Away) AdvanceProbabilities(OutcomeProbabilities outcome)
        {
            var normalized = outcome.Normalize();
            var decisive = normalized.HomeWin + normalized.AwayWin;
            return decisive > 0
                ? (normalized.HomeWin / decisive, normalized.AwayWin / decisive)
                : (.5, .5);
        }

        private static (int Home, int Away) PredictionScore(MatchPrediction prediction)
        {
            if (prediction.ExpectedHomeGoals.HasValue && prediction.ExpectedAwayGoals.HasValue)
                return (RoundedExpectedGoals(prediction.ExpectedHomeGoals.Value), RoundedExpectedGoals(prediction.ExpectedAwayGoals.Value));

            if (prediction.MostLikelyScore is { } score)
                return score;

            return prediction.Outcome.TopPick switch
            {
                "Home" => (1, 0),
                "Away" => (0, 1),
                _ => (1, 1)
            };
        }

        private static int RoundedExpectedGoals(double value) =>
            Math.Max(0, (int)Math.Round(value, MidpointRounding.AwayFromZero));

        private static string? ActualWinner(Fixture fixture)
        {
            if (fixture is not { IsPlayed: true, HomeGoals: int homeGoals, AwayGoals: int awayGoals })
                return null;
            if (homeGoals > awayGoals)
                return fixture.HomeTeamId;
            if (awayGoals > homeGoals)
                return fixture.AwayTeamId;
            if (string.Equals(fixture.WinnerTeamId, fixture.HomeTeamId, StringComparison.Ordinal) ||
                string.Equals(fixture.WinnerTeamId, fixture.AwayTeamId, StringComparison.Ordinal))
            {
                return fixture.WinnerTeamId;
            }

            return null;
        }

        private static string InputHash(
            IReadOnlyList<Group> groups,
            IReadOnlyList<Fixture> fixtures,
            IReadOnlyList<BracketTieProjection> ties)
        {
            var groupTokens = groups.Select(g => $"{g.Name}:{string.Join("-", g.TeamIds)}");
            var resultTokens = fixtures
                .Where(f => f is { IsPlayed: true, HomeGoals: not null, AwayGoals: not null })
                .OrderBy(f => f.Id)
                .Select(f => $"{f.Id}:{f.HomeGoals}-{f.AwayGoals}:{f.WinnerTeamId}");
            var tieTokens = ties.Select(t => $"{t.TieId}:{t.HomeTeamId}-{t.AwayTeamId}:{t.PredictedWinnerTeamId}:{t.ActualWinnerTeamId}");
            return CryptoUtil.GetSha256($"{string.Join("|", groupTokens)}|{string.Join("|", resultTokens)}|{string.Join("|", tieTokens)}");
        }

        private sealed record GroupSlots(string Winner, string RunnerUp, string Third);
    }
}
