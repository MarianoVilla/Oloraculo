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
    public class OfficialBracketService
    {
        public const string KnockoutFixturePrefix = "ko:";
        public const string PendingMessage = "Todavia no hay cruces oficiales publicados.";

        private readonly OloraculoDbContext _db;
        private readonly PredictionService _prediction;

        public OfficialBracketService(OloraculoDbContext db, PredictionService prediction)
        {
            _db = db;
            _prediction = prediction;
        }

        public async Task<BracketProjection> BuildAsync(CancellationToken ct = default)
        {
            var generatedAt = DateTimeOffset.UtcNow;
            var officialFixtureIds = await _db.ApiMappings.AsNoTracking()
                .Where(m => m.LocalFixtureId.StartsWith(KnockoutFixturePrefix))
                .Select(m => m.LocalFixtureId)
                .ToListAsync(ct);
            var officialIdSet = officialFixtureIds.ToHashSet(StringComparer.Ordinal);
            var fixtures = (await _db.Fixtures.AsNoTracking()
                .Where(f => officialIdSet.Contains(f.Id))
                .ToListAsync(ct))
                .OrderBy(StageSortFromFixture)
                .ThenBy(f => TieIdFromFixtureId(f.Id))
                .ThenBy(f => f.KickoffUtc ?? DateTimeOffset.MaxValue)
                .ToList();

            if (fixtures.Count == 0)
            {
                return new BracketProjection
                {
                    GeneratedAt = generatedAt,
                    ModelName = "Cuadro oficial",
                    InputSummaryHash = CryptoUtil.GetSha256($"official-bracket|empty|{generatedAt:O}"),
                    PendingMessage = PendingMessage,
                    Ties = []
                };
            }

            var latestSnapshots = await LatestSnapshotIdsAsync(ct);
            var evaluatedFixtures = await _db.Evaluations.AsNoTracking()
                .Select(e => e.FixtureId)
                .Distinct()
                .ToHashSetAsync(StringComparer.Ordinal, ct);
            var predictors = await _prediction.BuildPredictorsAsync(ct);
            var ties = new List<BracketTieProjection>();
            var winners = new Dictionary<int, string>();

            foreach (var fixture in fixtures)
            {
                var projection = await ProjectOfficialFixtureAsync(fixture, latestSnapshots, evaluatedFixtures, predictors, ct);
                ties.Add(projection);
                var advancing = projection.ActualWinnerTeamId ?? projection.PredictedWinnerTeamId;
                if (!string.IsNullOrWhiteSpace(advancing))
                    winners[projection.TieId] = advancing;
            }

            foreach (var tie in WorldCup2026Bracket.KnockoutTies.Where(t => !ties.Any(existing => existing.TieId == t.Id)))
            {
                if (!TryResolveProjectedTie(tie, winners, out var home, out var away))
                    continue;

                var projection = await ProjectSyntheticTieAsync(tie, home, away, predictors, ct);
                ties.Add(projection);
                if (!string.IsNullOrWhiteSpace(projection.PredictedWinnerTeamId))
                    winners[tie.Id] = projection.PredictedWinnerTeamId;
            }

            ties = ties.OrderBy(t => StageSort(t.StageLabel)).ThenBy(t => t.TieId).ToList();
            return new BracketProjection
            {
                GeneratedAt = generatedAt,
                ModelName = "Cuadro oficial",
                InputSummaryHash = InputHash(fixtures, ties),
                Ties = ties
            };
        }

        public static string FixtureId(int tieId) => $"{KnockoutFixturePrefix}{tieId}";

        public static int? TieIdFromFixtureId(string fixtureId) =>
            fixtureId.StartsWith(KnockoutFixturePrefix, StringComparison.Ordinal) &&
            int.TryParse(fixtureId[KnockoutFixturePrefix.Length..], out var id)
                ? id
                : null;

        public static string StageLabel(KnockoutStageEnum stage) => stage switch
        {
            KnockoutStageEnum.RoundOf32 => "16avos",
            KnockoutStageEnum.RoundOf16 => "Octavos",
            KnockoutStageEnum.QuarterFinal => "Cuartos",
            KnockoutStageEnum.SemiFinal => "Semis",
            KnockoutStageEnum.Final => "Final",
            _ => stage.ToString()
        };

        public static KnockoutStageEnum? StageFromRound(string? round)
        {
            if (string.IsNullOrWhiteSpace(round))
                return null;

            var normalized = NormalizeRound(round);
            if (normalized.Contains("roundof32") || normalized.Contains("32"))
                return KnockoutStageEnum.RoundOf32;
            if (normalized.Contains("roundof16") || normalized.Contains("16"))
                return KnockoutStageEnum.RoundOf16;
            if (normalized.Contains("quarter"))
                return KnockoutStageEnum.QuarterFinal;
            if (normalized.Contains("semi"))
                return KnockoutStageEnum.SemiFinal;
            if (normalized.Contains("final") && !normalized.Contains("semi"))
                return KnockoutStageEnum.Final;
            return null;
        }

        public static IReadOnlyList<int> TieIdsForStage(KnockoutStageEnum stage) => stage switch
        {
            KnockoutStageEnum.RoundOf32 => Enumerable.Range(73, 16).ToList(),
            KnockoutStageEnum.RoundOf16 => Enumerable.Range(89, 8).ToList(),
            KnockoutStageEnum.QuarterFinal => Enumerable.Range(97, 4).ToList(),
            KnockoutStageEnum.SemiFinal => Enumerable.Range(101, 2).ToList(),
            KnockoutStageEnum.Final => [104],
            _ => []
        };

        public static KnockoutStageEnum? StageFromTieId(int tieId) => tieId switch
        {
            >= 73 and <= 88 => KnockoutStageEnum.RoundOf32,
            >= 89 and <= 96 => KnockoutStageEnum.RoundOf16,
            >= 97 and <= 100 => KnockoutStageEnum.QuarterFinal,
            >= 101 and <= 102 => KnockoutStageEnum.SemiFinal,
            104 => KnockoutStageEnum.Final,
            _ => null
        };

        public static string? ActualWinner(Fixture fixture)
        {
            if (!fixture.IsPlayed || !fixture.HomeGoals.HasValue || !fixture.AwayGoals.HasValue)
                return null;
            if (fixture.HomeGoals > fixture.AwayGoals)
                return fixture.HomeTeamId;
            if (fixture.AwayGoals > fixture.HomeGoals)
                return fixture.AwayTeamId;
            return string.IsNullOrWhiteSpace(fixture.WinnerTeamId) ? null : fixture.WinnerTeamId;
        }

        private async Task<BracketTieProjection> ProjectOfficialFixtureAsync(
            Fixture fixture,
            IReadOnlyDictionary<string, int> latestSnapshots,
            IReadOnlySet<string> evaluatedFixtures,
            IReadOnlyList<IPredictor> predictors,
            CancellationToken ct)
        {
            var tieId = TieIdFromFixtureId(fixture.Id) ?? 0;
            var stage = StageFromTieId(tieId);
            var projection = new BracketTieProjection
            {
                TieId = tieId,
                FixtureId = fixture.Id,
                StageLabel = stage.HasValue ? StageLabel(stage.Value) : fixture.Status ?? "KO",
                IsOfficialFixture = true,
                HomeSlotLabel = fixture.HomeTeamId,
                AwaySlotLabel = fixture.AwayTeamId,
                HomeTeamId = fixture.HomeTeamId,
                AwayTeamId = fixture.AwayTeamId,
                IsPlayed = fixture.IsPlayed,
                ActualHomeGoals = fixture.HomeGoals,
                ActualAwayGoals = fixture.AwayGoals,
                ActualWinnerTeamId = ActualWinner(fixture),
                LatestSnapshotId = latestSnapshots.TryGetValue(fixture.Id, out var snapshotId) ? snapshotId : null,
                HasEvaluation = evaluatedFixtures.Contains(fixture.Id)
            };

            return await AddPredictionAsync(projection, fixture, predictors, ct);
        }

        private async Task<BracketTieProjection> ProjectSyntheticTieAsync(
            BracketTie tie,
            string homeTeamId,
            string awayTeamId,
            IReadOnlyList<IPredictor> predictors,
            CancellationToken ct)
        {
            var fixture = new Fixture
            {
                Id = $"projected:{tie.Id}",
                Group = "KO",
                HomeTeamId = homeTeamId,
                AwayTeamId = awayTeamId,
                NeutralVenue = true,
                Source = "official-bracket-projection"
            };
            var projection = new BracketTieProjection
            {
                TieId = tie.Id,
                FixtureId = null,
                StageLabel = StageLabel(tie.Stage),
                IsOfficialFixture = false,
                HomeSlotLabel = $"G{tie.Home.TieId}",
                AwaySlotLabel = $"G{tie.Away.TieId}",
                HomeTeamId = homeTeamId,
                AwayTeamId = awayTeamId
            };

            return await AddPredictionAsync(projection, fixture, predictors, ct);
        }

        private async Task<BracketTieProjection> AddPredictionAsync(
            BracketTieProjection projection,
            Fixture fixture,
            IReadOnlyList<IPredictor> predictors,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(fixture.HomeTeamId) || string.IsNullOrWhiteSpace(fixture.AwayTeamId))
            {
                projection.Error = "El cruce todavia no tiene equipos resueltos.";
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

        private static bool TryResolveProjectedTie(BracketTie tie, IReadOnlyDictionary<int, string> winners, out string home, out string away)
        {
            home = "";
            away = "";
            if (tie.Home.Kind != BracketSlotKindEnum.WinnerOfTie || tie.Away.Kind != BracketSlotKindEnum.WinnerOfTie ||
                !tie.Home.TieId.HasValue || !tie.Away.TieId.HasValue)
            {
                return false;
            }

            if (!winners.TryGetValue(tie.Home.TieId.Value, out home) || !winners.TryGetValue(tie.Away.TieId.Value, out away))
                return false;
            return true;
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

        private static int StageSortFromFixture(Fixture fixture) =>
            StageFromTieId(TieIdFromFixtureId(fixture.Id) ?? 0) is { } stage ? StageSort(StageLabel(stage)) : 99;

        private static int StageSort(string stage) => stage switch
        {
            "16avos" => 0,
            "Octavos" => 1,
            "Cuartos" => 2,
            "Semis" => 3,
            "Final" => 4,
            _ => 99
        };

        private static string InputHash(IReadOnlyList<Fixture> fixtures, IReadOnlyList<BracketTieProjection> ties)
        {
            var fixtureTokens = fixtures.Select(f => $"{f.Id}:{f.HomeTeamId}-{f.AwayTeamId}:{f.HomeGoals}-{f.AwayGoals}:{f.WinnerTeamId}");
            var tieTokens = ties.Select(t => $"{t.TieId}:{t.HomeTeamId}-{t.AwayTeamId}:{t.PredictedWinnerTeamId}:{t.ActualWinnerTeamId}");
            return CryptoUtil.GetSha256(string.Join("|", fixtureTokens.Concat(tieTokens)));
        }

        private static string NormalizeRound(string value) =>
            new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}
