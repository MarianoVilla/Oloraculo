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
            var snapshot = (await _db.Snapshots
                .Where(s => s.Kind == "match" && s.FixtureId == fixture.Id && s.HomeWin.HasValue)
                .ToListAsync(ct))
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefault();
            if (snapshot is null || snapshot.HomeWin is null || snapshot.Draw is null || snapshot.AwayWin is null)
                return 0;

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

            _db.Results.Add(new MatchResult
            {
                Id = CryptoUtil.GetSha256($"manual|{DateTimeOffset.UtcNow:O}|{fixture.HomeTeamId}|{fixture.AwayTeamId}|{homeGoals}-{awayGoals}"),
                HomeTeamId = fixture.HomeTeamId,
                AwayTeamId = fixture.AwayTeamId,
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                Date = DateTimeOffset.UtcNow,
                Tournament = "FIFA World Cup 2026",
                Neutral = fixture.NeutralVenue,
                Source = "manual"
            });
            fixture.IsPlayed = true;
            fixture.HomeGoals = homeGoals;
            fixture.AwayGoals = awayGoals;
            await _db.SaveChangesAsync(ct);
            return 1;
        }

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
}
