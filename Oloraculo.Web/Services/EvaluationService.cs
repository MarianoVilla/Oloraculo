using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;

namespace Oloraculo.Web.Services
{
    public class EvaluationService
    {
        private readonly OloraculoDbContext _db;

        public EvaluationService(OloraculoDbContext db) => _db = db;

        public async Task<int> EvaluateLatestSnapshotAsync(Fixture fixture, int homeGoals, int awayGoals, CancellationToken ct = default)
        {
            var evaluated = await TryAddEvaluationAsync(fixture, homeGoals, awayGoals, ct);
            if (!evaluated)
                return 0;

            UpsertManualResult(fixture, homeGoals, awayGoals);
            await _db.SaveChangesAsync(ct);
            return 1;
        }

        /// <summary>
        /// Guarda el resultado de un partido jugado (lo marca como jugado y persiste el marcador)
        /// aunque todavia no exista una prediccion guardada. Si hay una prediccion sin evaluar,
        /// ademas la evalua. Es idempotente: volver a guardar actualiza el marcador sin duplicarlo.
        /// </summary>
        public async Task<ManualResultReport> RecordManualResultAsync(Fixture fixture, int homeGoals, int awayGoals, CancellationToken ct = default)
        {
            var evaluated = await TryAddEvaluationAsync(fixture, homeGoals, awayGoals, ct);
            UpsertManualResult(fixture, homeGoals, awayGoals);
            await _db.SaveChangesAsync(ct);
            return new ManualResultReport(true, evaluated);
        }

        /// <summary>
        /// Deshace el resultado cargado: vuelve el partido a "no jugado", borra el marcador,
        /// el resultado manual del historico y cualquier evaluacion asociada.
        /// </summary>
        public async Task<bool> ClearResultAsync(Fixture fixture, CancellationToken ct = default)
        {
            var hadResult = fixture.IsPlayed || fixture.HomeGoals.HasValue || fixture.AwayGoals.HasValue;

            fixture.IsPlayed = false;
            fixture.HomeGoals = null;
            fixture.AwayGoals = null;

            var manualId = ManualResultId(fixture.Id);
            var existingResult = await _db.Results.FirstOrDefaultAsync(r => r.Id == manualId, ct);
            if (existingResult is not null)
                _db.Results.Remove(existingResult);

            var evaluations = await _db.Evaluations.Where(e => e.FixtureId == fixture.Id).ToListAsync(ct);
            if (evaluations.Count > 0)
                _db.Evaluations.RemoveRange(evaluations);

            await _db.SaveChangesAsync(ct);
            return hadResult || existingResult is not null || evaluations.Count > 0;
        }

        private async Task<bool> TryAddEvaluationAsync(Fixture fixture, int homeGoals, int awayGoals, CancellationToken ct)
        {
            if (await _db.Evaluations.AnyAsync(e => e.FixtureId == fixture.Id, ct))
                return false;

            var snapshot = (await _db.Snapshots
                .Where(s => s.Kind == "match" && s.FixtureId == fixture.Id && s.HomeWin.HasValue)
                .ToListAsync(ct))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();
            if (snapshot is null || snapshot.HomeWin is null || snapshot.Draw is null || snapshot.AwayWin is null)
                return false;

            var predicted = new OutcomeProbabilities(snapshot.HomeWin.Value, snapshot.Draw.Value, snapshot.AwayWin.Value).Normalize();
            var actual = OutcomeFromGoals(homeGoals, awayGoals);
            _db.Evaluations.Add(new PredictionEvaluation
            {
                ModelName = snapshot.ModelName,
                FixtureId = fixture.Id,
                HomeTeamId = fixture.HomeTeamId,
                AwayTeamId = fixture.AwayTeamId,
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                HomeWin = predicted.HomeWin,
                Draw = predicted.Draw,
                AwayWin = predicted.AwayWin,
                Actual = actual,
                BrierScore = ProbabilityHelper.BrierScore(predicted, actual),
                RankedProbabilityScore = ProbabilityHelper.RankedProbabilityScore(predicted, actual),
                LogLoss = ProbabilityHelper.LogLoss(predicted, actual),
                TopPickCorrect = predicted.TopPick == actual,
                PredictedAt = snapshot.CreatedAt
            });
            return true;
        }

        private void UpsertManualResult(Fixture fixture, int homeGoals, int awayGoals)
        {
            var manualId = ManualResultId(fixture.Id);
            var existing = _db.Results.Local.FirstOrDefault(r => r.Id == manualId)
                ?? _db.Results.FirstOrDefault(r => r.Id == manualId);

            if (existing is null)
            {
                _db.Results.Add(new MatchResult
                {
                    Id = manualId,
                    HomeTeamId = fixture.HomeTeamId,
                    AwayTeamId = fixture.AwayTeamId,
                    HomeGoals = homeGoals,
                    AwayGoals = awayGoals,
                    Date = DateTimeOffset.UtcNow,
                    Tournament = "FIFA World Cup 2026",
                    Neutral = fixture.NeutralVenue,
                    Source = "manual"
                });
            }
            else
            {
                existing.HomeTeamId = fixture.HomeTeamId;
                existing.AwayTeamId = fixture.AwayTeamId;
                existing.HomeGoals = homeGoals;
                existing.AwayGoals = awayGoals;
                existing.Neutral = fixture.NeutralVenue;
            }

            fixture.IsPlayed = true;
            fixture.HomeGoals = homeGoals;
            fixture.AwayGoals = awayGoals;
        }

        private static string ManualResultId(string fixtureId) => CryptoUtil.GetSha256($"manual|{fixtureId}");

        public async Task<FixtureEvaluationRefreshReport> EvaluateUnevaluatedPlayedFixturesAsync(CancellationToken ct = default)
        {
            var fixtures = await _db.Fixtures
                .Where(f => f.IsPlayed && f.HomeGoals.HasValue && f.AwayGoals.HasValue)
                .ToListAsync(ct);

            var evaluated = 0;
            var skippedAlreadyEvaluated = 0;
            var skippedWithoutSnapshot = 0;

            foreach (var fixture in fixtures)
            {
                var hasEvaluation = await _db.Evaluations
                    .AnyAsync(e => e.FixtureId == fixture.Id, ct);
                if (hasEvaluation)
                {
                    skippedAlreadyEvaluated++;
                    continue;
                }

                var count = await EvaluateLatestSnapshotAsync(fixture, fixture.HomeGoals!.Value, fixture.AwayGoals!.Value, ct);
                if (count == 0)
                    skippedWithoutSnapshot++;
                else
                    evaluated += count;
            }

            return new FixtureEvaluationRefreshReport(
                evaluated,
                skippedAlreadyEvaluated,
                skippedWithoutSnapshot);
        }

        public async Task<IReadOnlyList<ModelPerformanceRow>> PerformanceAsync(CancellationToken ct = default)
        {
            var rows = await _db.Evaluations.AsNoTracking().ToListAsync(ct);
            return rows.GroupBy(e => e.ModelName)
                .Select(g => new ModelPerformanceRow
                {
                    ModelName = g.Key,
                    Count = g.Count(),
                    TopPickAccuracy = g.Average(e => e.TopPickCorrect ? 1.0 : 0.0),
                    MeanBrier = g.Average(e => e.BrierScore),
                    MeanRps = g.Average(e => e.RankedProbabilityScore),
                    MeanLogLoss = g.Average(e => e.LogLoss)
                })
                .OrderBy(r => r.MeanRps)
                .ToList();
        }

        public async Task<IReadOnlyList<PredictionEvaluation>> BestCallsAsync(int take = 8, CancellationToken ct = default) =>
            await _db.Evaluations.AsNoTracking().OrderBy(e => e.RankedProbabilityScore).Take(take).ToListAsync(ct);

        public async Task<IReadOnlyList<PredictionEvaluation>> OverconfidentFailuresAsync(int take = 8, CancellationToken ct = default) =>
            await _db.Evaluations.AsNoTracking()
                .Where(e => !e.TopPickCorrect)
                .OrderByDescending(e => Math.Max(e.HomeWin, Math.Max(e.Draw, e.AwayWin)))
                .Take(take)
                .ToListAsync(ct);

        public static string OutcomeFromGoals(int homeGoals, int awayGoals) =>
            homeGoals > awayGoals ? "Home" : awayGoals > homeGoals ? "Away" : "Draw";
    }

    public sealed record FixtureEvaluationRefreshReport(
        int Evaluated,
        int SkippedAlreadyEvaluated,
        int SkippedWithoutSnapshot);

    public sealed record ManualResultReport(
        bool Saved,
        bool Evaluated);
}
