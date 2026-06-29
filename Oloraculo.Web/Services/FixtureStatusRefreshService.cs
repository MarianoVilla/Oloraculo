using Oloraculo.Web.Models.ApiFootballModels;

namespace Oloraculo.Web.Services
{
    public class FixtureStatusRefreshService
    {
        private readonly ApiFootballService _api;
        private readonly KnockoutBracketService _bracket;
        private readonly EvaluationService _evaluation;

        public FixtureStatusRefreshService(
            ApiFootballService api,
            KnockoutBracketService bracket,
            EvaluationService evaluation)
        {
            _api = api;
            _bracket = bracket;
            _evaluation = evaluation;
        }

        public async Task<FixtureStatusRefreshReport> RefreshAsync(CancellationToken ct = default)
        {
            var notes = new List<string>();
            var errors = new List<string>();

            var fetch = await _api.FetchFixtureRowsAsync(ct);
            notes.AddRange(fetch.Notes);
            errors.AddRange(fetch.Errors);

            var initialRefresh = await _api.RefreshFixturesAsync(fetch.Fixtures, fetch.Notes, fetch.Errors, ct);
            AddReport(initialRefresh, notes, errors);

            await _bracket.BuildAsync(upsertFixtures: true, ct);

            var knockoutRefresh = await _api.RefreshFixturesAsync(fetch.Fixtures, ct: ct);
            AddReport(knockoutRefresh, notes, errors);

            var evaluation = await _evaluation.EvaluateUnevaluatedPlayedFixturesAsync(ct);
            notes.Add(
                $"Evaluacion automatica: {evaluation.Evaluated} evaluados, " +
                $"{evaluation.SkippedAlreadyEvaluated} ya evaluados, " +
                $"{evaluation.SkippedWithoutSnapshot} sin snapshot.");

            var rebuiltAfterKnockoutResults = false;
            if (knockoutRefresh.FixturesUpdatedAsPlayed > 0)
            {
                await _bracket.BuildAsync(upsertFixtures: true, ct);
                rebuiltAfterKnockoutResults = true;
            }

            return new FixtureStatusRefreshReport
            {
                IsConfigured = fetch.IsConfigured || initialRefresh.IsConfigured || knockoutRefresh.IsConfigured,
                InitialRefresh = initialRefresh,
                KnockoutRefresh = knockoutRefresh,
                Evaluation = evaluation,
                BracketRebuiltAfterKnockoutResults = rebuiltAfterKnockoutResults,
                Notes = notes,
                Errors = errors
            };
        }

        private static void AddReport(ApiFootballRefreshReport report, List<string> notes, List<string> errors)
        {
            notes.AddRange(report.Notes);
            errors.AddRange(report.Errors);
        }
    }

    public class FixtureStatusRefreshReport
    {
        public bool IsConfigured { get; init; }
        public ApiFootballRefreshReport? InitialRefresh { get; init; }
        public ApiFootballRefreshReport? KnockoutRefresh { get; init; }
        public FixtureEvaluationRefreshReport? Evaluation { get; init; }
        public bool BracketRebuiltAfterKnockoutResults { get; init; }
        public IReadOnlyList<string> Notes { get; init; } = [];
        public IReadOnlyList<string> Errors { get; init; } = [];
    }
}
