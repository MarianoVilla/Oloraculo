using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.WorldCup.Burden;

namespace Oloraculo.Web.ComboLab.Monitor
{
    public sealed record SimulationPreRegistrationCheckpoint
    {
        public const string VerdictHold = "HOLD_ANALYSIS_ONLY";

        public DateTimeOffset GeneratedAtUtc { get; init; }
        public string Verdict { get; init; } = VerdictHold;
        public required string CheckpointHash { get; init; }
        public required string ModelName { get; init; }
        public int Simulations { get; init; }
        public required string ProjectionInputHash { get; init; }
        public required string BurdenModelVersion { get; init; }
        public int BurdenReadyFixtures { get; init; }
        public int UniverseMarkets { get; init; }
        public int ComboEligibleMarkets { get; init; }
        public IReadOnlyList<string> SourceManifest { get; init; } = [];
        public IReadOnlyList<string> RequiredValidationGates { get; init; } = [];

        public static SimulationPreRegistrationCheckpoint Create(
            TournamentProjection projection,
            PolymarketFootballUniverseSummary? universe,
            WorldCupBurdenCoverageSnapshot? burdenCoverage,
            IEnumerable<string> sourceManifest,
            DateTimeOffset? generatedAtUtc = null)
        {
            ArgumentNullException.ThrowIfNull(projection);
            ArgumentNullException.ThrowIfNull(sourceManifest);

            var sources = sourceManifest
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Select(source => source.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var gates = new[]
            {
                "exact_fixture_team_market_line_join",
                "receive_time_only_inputs",
                "oos_calibration_by_market_family",
                "sharp_anchor_when_available",
                "feature_ablation_for_burden_sentiment_spatial_terms",
                "execution_cost_and_fill_gate_before_ev_claim"
            };
            var stamp = generatedAtUtc ?? DateTimeOffset.UtcNow;
            var payload = string.Join("|", new[]
            {
                stamp.ToUnixTimeMilliseconds().ToString(),
                projection.ModelName,
                projection.Simulations.ToString(),
                projection.InputSummaryHash,
                WorldCupFixtureBurdenCalculator.ModelVersion,
                (burdenCoverage?.BurdenReadyFixtures ?? 0).ToString(),
                (burdenCoverage?.TotalFixtures ?? 0).ToString(),
                (universe?.TotalMarkets ?? 0).ToString(),
                (universe?.ComboEligibleMarkets ?? 0).ToString(),
                string.Join(",", sources),
                string.Join(",", gates)
            });

            return new SimulationPreRegistrationCheckpoint
            {
                GeneratedAtUtc = stamp,
                CheckpointHash = CryptoUtil.GetSha256(payload),
                ModelName = projection.ModelName,
                Simulations = projection.Simulations,
                ProjectionInputHash = projection.InputSummaryHash,
                BurdenModelVersion = WorldCupFixtureBurdenCalculator.ModelVersion,
                BurdenReadyFixtures = burdenCoverage?.BurdenReadyFixtures ?? 0,
                UniverseMarkets = universe?.TotalMarkets ?? 0,
                ComboEligibleMarkets = universe?.ComboEligibleMarkets ?? 0,
                SourceManifest = sources,
                RequiredValidationGates = gates
            };
        }
    }
}
