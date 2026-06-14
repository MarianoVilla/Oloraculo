using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Services.Simulation;
using System.Globalization;
using System.Net;
using System.Text;

namespace Oloraculo.Web.Services
{
    public class ReadmeSnapshotExportService
    {
        public const string StartMarker = "<!-- oloraculo:snapshots:start -->";
        public const string EndMarker = "<!-- oloraculo:snapshots:end -->";

        private readonly OloraculoDbContext _db;
        private readonly CsvImportService _importer;
        private readonly RankingRefreshService _rankings;
        private readonly ApiFootballService _api;
        private readonly AvailabilityNewsService _availability;
        private readonly PredictionService _prediction;
        private readonly EvaluationService _evaluation;
        private readonly SnapshotService _snapshots;
        private readonly SimulationService _simulation;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ReadmeSnapshotExportService> _logger;

        public ReadmeSnapshotExportService(
            OloraculoDbContext db,
            CsvImportService importer,
            RankingRefreshService rankings,
            ApiFootballService api,
            AvailabilityNewsService availability,
            PredictionService prediction,
            EvaluationService evaluation,
            SnapshotService snapshots,
            SimulationService simulation,
            IWebHostEnvironment environment,
            ILogger<ReadmeSnapshotExportService> logger)
        {
            _db = db;
            _importer = importer;
            _rankings = rankings;
            _api = api;
            _availability = availability;
            _prediction = prediction;
            _evaluation = evaluation;
            _snapshots = snapshots;
            _simulation = simulation;
            _environment = environment;
            _logger = logger;
        }

        public async Task ExportAsync(CancellationToken ct = default)
        {
            var rankings = await _rankings.RefreshAsync(ct: ct);
            LogReport("ranking", rankings.Notes, rankings.Errors);
            if (rankings.AnyFileUpdated)
                await _importer.ImportRatingsOnlyAsync(ct);

            var api = await _api.RefreshFixturesAsync(ct);
            LogReport("API-Football fixtures", api.Notes, api.Errors);

            var availability = await _availability.RefreshAsync(ct);
            LogReport("availability", availability.Notes, availability.Errors);

            var roles = await _api.EnrichAvailabilityRolesAsync(ct);
            LogReport("availability roles", roles.Notes, roles.Errors);

            await _importer.ImportIfNeededAsync(ct);

            var evaluation = await _evaluation.EvaluateUnevaluatedPlayedFixturesAsync(ct);
            _logger.LogInformation(
                "Fixture evaluation refresh: evaluated={Evaluated}; skipped already evaluated={SkippedAlreadyEvaluated}; skipped without snapshot={SkippedWithoutSnapshot}.",
                evaluation.Evaluated,
                evaluation.SkippedAlreadyEvaluated,
                evaluation.SkippedWithoutSnapshot);

            var fixtures = await _db.Fixtures.AsNoTracking().ToListAsync(ct);
            var orderedFixtures = OrderedFixtures(fixtures).ToList();
            var predictions = await _prediction.PredictFixturesAsync(orderedFixtures, ct);
            await _snapshots.SaveFullFixtureAsync(predictions.Select(result => result.BestPrediction), ct);

            var projection = await _simulation.RunAsync(saveSnapshot: true, ct: ct);
            var names = await _db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            var availabilityClaims = await AvailabilityClaimsByFixtureAsync(orderedFixtures, ct);

            var block = RenderSnapshotBlock(projection, predictions, names, DateTimeOffset.UtcNow, availabilityClaims);
            var readmePath = Path.Combine(RepositoryRoot(), "README.md");
            var existing = File.Exists(readmePath) ? await File.ReadAllTextAsync(readmePath, ct) : "# Oloraculo";
            var updated = ReplaceSnapshotBlock(existing, block);
            await File.WriteAllTextAsync(readmePath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        }

        public static string ReplaceSnapshotBlock(string readme, string block)
        {
            var renderedBlock = $"{StartMarker}{Environment.NewLine}{block.TrimEnd()}{Environment.NewLine}{EndMarker}";
            var start = readme.IndexOf(StartMarker, StringComparison.Ordinal);
            var end = readme.IndexOf(EndMarker, StringComparison.Ordinal);

            if (start >= 0 && end > start)
            {
                end += EndMarker.Length;
                readme = readme[..start].TrimEnd() + Environment.NewLine + readme[end..].TrimStart();
            }

            var insertionIndex = SnapshotInsertionIndex(readme);
            return readme[..insertionIndex].TrimEnd() +
                Environment.NewLine + Environment.NewLine +
                renderedBlock +
                Environment.NewLine + Environment.NewLine +
                readme[insertionIndex..].TrimStart();
        }

        private static int SnapshotInsertionIndex(string readme)
        {
            var firstHeading = readme.IndexOf("# ", StringComparison.Ordinal);
            if (firstHeading < 0)
                return 0;

            var searchFrom = firstHeading + 2;
            while (searchFrom < readme.Length)
            {
                var nextLine = readme.IndexOf('\n', searchFrom);
                if (nextLine < 0)
                    return readme.Length;

                var candidate = nextLine + 1;
                if (candidate < readme.Length && readme[candidate] == '#' && candidate + 1 < readme.Length && readme[candidate + 1] == ' ')
                    return candidate;

                searchFrom = candidate;
            }

            return readme.Length;
        }

        public static string RenderSnapshotBlock(
            TournamentProjection projection,
            IReadOnlyList<MatchPredictionResult> predictions,
            IReadOnlyDictionary<string, string> teamNames,
            DateTimeOffset generatedAt,
            IReadOnlyDictionary<string, IReadOnlyList<AvailabilityClaim>>? availabilityClaimsByFixture = null)
        {
            var builder = new StringBuilder();
            builder.AppendLine("## Predicciones más recientes");
            builder.AppendLine("_A medida que se recibe nueva información y se juegan partidos reales, " +
                "el Oloráculo ajusta sus predicciones y las publica acá. A continuación vas a encontrar las más recientes._");
            builder.AppendLine();
            builder.AppendLine("### Torneo");
            builder.AppendLine();
            builder.AppendLine($"_Generado {generatedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC a través de {projection.Simulations.ToString("N0", CultureInfo.InvariantCulture)} simulaciones._");
            builder.AppendLine();
            builder.AppendLine("| Team | Group | Qualify | QF | SF | Final | Champion |");
            builder.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
            foreach (var team in projection.Teams.OrderByDescending(t => t.WinTournament).ThenBy(t => Name(teamNames, t.TeamId)).Take(16))
            {
                builder.AppendLine(
                    $"| {TeamCell(team.TeamId, Name(teamNames, team.TeamId))} | {Escape(team.Group)} | {Percent(team.Qualify, 0)} | {Percent(team.ReachQuarterFinal, 0)} | {Percent(team.ReachSemiFinal, 0)} | {Percent(team.ReachFinal, 0)} | **{Percent(team.WinTournament, 1)}** |");
            }

            builder.AppendLine();
            builder.AppendLine("### Grupos");
            builder.AppendLine();

            foreach (var group in predictions.GroupBy(p => p.Fixture.Group).OrderBy(g => g.Key))
            {
                builder.AppendLine("<details open>");
                builder.AppendLine($"<summary><strong>Group {Escape(group.Key)}</strong></summary>");
                builder.AppendLine();
                builder.AppendLine("| Match | Status | Result / Pick | Why | H | D | A |");
                builder.AppendLine("| --- | --- | --- | --- | ---: | ---: | ---: |");
                foreach (var result in OrderedPredictions(group))
                {
                    var fixture = result.Fixture;
                    var prediction = result.BestPrediction;
                    var home = TeamCell(fixture.HomeTeamId, result.HomeTeamName);
                    var away = TeamCell(fixture.AwayTeamId, result.AwayTeamName);
                    var status = StatusText(fixture);
                    var pick = ResultOrPickText(fixture, prediction);
                    var rationale = RationaleText(prediction, fixture.Id, availabilityClaimsByFixture);
                    builder.AppendLine($"| {home} vs {away} | {status} | {pick} | {rationale} | {Percent(prediction.Outcome.HomeWin, 0)} | {Percent(prediction.Outcome.Draw, 0)} | {Percent(prediction.Outcome.AwayWin, 0)} |");
                }

                builder.AppendLine();
                builder.AppendLine("</details>");
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private string RepositoryRoot()
        {
            var current = new DirectoryInfo(_environment.ContentRootPath);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Oloraculo.sln")))
                    return current.FullName;

                current = current.Parent;
            }

            return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, ".."));
        }

        private void LogReport(string label, IReadOnlyList<string> notes, IReadOnlyList<string> errors)
        {
            foreach (var note in notes)
                _logger.LogInformation("{Label}: {Note}", label, note);
            foreach (var error in errors)
                _logger.LogWarning("{Label}: {Error}", label, error);
        }

        private async Task<IReadOnlyDictionary<string, IReadOnlyList<AvailabilityClaim>>> AvailabilityClaimsByFixtureAsync(
            IEnumerable<Fixture> fixtures,
            CancellationToken ct)
        {
            var fixtureList = fixtures.ToList();
            var claimsByFixture = new Dictionary<string, IReadOnlyList<AvailabilityClaim>>(StringComparer.Ordinal);
            if (fixtureList.Count == 0)
                return claimsByFixture;

            var teamIds = fixtureList
                .SelectMany(fixture => new[] { fixture.HomeTeamId, fixture.AwayTeamId })
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var claims = await _db.AvailabilityClaims.AsNoTracking()
                .Where(claim => claim.AffectsPrediction && teamIds.Contains(claim.TeamId))
                .ToListAsync(ct);

            foreach (var fixture in fixtureList)
            {
                var fixtureClaims = claims
                    .Where(claim => claim.TeamId == fixture.HomeTeamId || claim.TeamId == fixture.AwayTeamId)
                    .OrderBy(AvailabilitySort)
                    .ThenBy(claim => claim.TeamName)
                    .ThenBy(claim => claim.Player)
                    .ToList();
                if (fixtureClaims.Count > 0)
                    claimsByFixture[fixture.Id] = fixtureClaims;
            }

            return claimsByFixture;
        }

        private static IEnumerable<Fixture> OrderedFixtures(IEnumerable<Fixture> fixtures) =>
            fixtures
                .OrderBy(fixture => fixture.KickoffUtc ?? DateTimeOffset.MaxValue)
                .ThenBy(fixture => fixture.Group)
                .ThenBy(fixture => fixture.HomeTeamId)
                .ThenBy(fixture => fixture.AwayTeamId);

        private static IEnumerable<MatchPredictionResult> OrderedPredictions(IEnumerable<MatchPredictionResult> predictions) =>
            predictions
                .OrderBy(result => result.Fixture.KickoffUtc ?? DateTimeOffset.MaxValue)
                .ThenBy(result => result.HomeTeamName)
                .ThenBy(result => result.AwayTeamName);

        private static string ResultOrPickText(Fixture fixture, MatchPrediction prediction)
        {
            var predictionText = PredictionText(prediction);
            if (fixture.IsPlayed && fixture.HomeGoals.HasValue && fixture.AwayGoals.HasValue)
                return $"**{fixture.HomeGoals}-{fixture.AwayGoals}** <br><sub>Prediction: {predictionText}</sub>";

            return predictionText;
        }

        private static string PredictionText(MatchPrediction prediction)
        {
            if (prediction.MostLikelyScore is { } score)
                return $"{score.Home}-{score.Away}";

            return prediction.Outcome.TopPick switch
            {
                "Home" => "Home win",
                "Draw" => "Draw",
                "Away" => "Away win",
                _ => "-"
            };
        }

        private static string RationaleText(
            MatchPrediction prediction,
            string fixtureId,
            IReadOnlyDictionary<string, IReadOnlyList<AvailabilityClaim>>? availabilityClaimsByFixture)
        {
            var lines = new List<string>
            {
                $"Model: {ModelText(prediction)}"
            };

            var signals = SignalsText(prediction);
            if (!string.IsNullOrWhiteSpace(signals))
                lines.Add($"Signals: {signals}");

            var availability = AvailabilityText(fixtureId, availabilityClaimsByFixture);
            if (!string.IsNullOrWhiteSpace(availability))
                lines.Add($"Bajas: {availability}");

            var missing = LimitedList(prediction.FeaturesMissing, maxItems: 3, maxItemLength: 70);
            if (!string.IsNullOrWhiteSpace(missing))
                lines.Add($"Missing: {missing}");

            return string.Join("<br>", lines.Select(line => SanitizeCellSegment(line, maxLength: 220)));
        }

        private static string ModelText(MatchPrediction prediction)
        {
            var selectedModel = prediction.Sources
                .FirstOrDefault(source =>
                    string.Equals(source.Name, "model ladder", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(source.Notes))
                ?.Notes;

            if (!string.IsNullOrWhiteSpace(selectedModel) &&
                !string.Equals(selectedModel, prediction.PredictorName, StringComparison.Ordinal))
            {
                return $"{prediction.PredictorName} ({selectedModel})";
            }

            return prediction.PredictorName;
        }

        private static string SignalsText(MatchPrediction prediction)
        {
            var signals = new List<string>();
            signals.AddRange(prediction.FeaturesUsed);
            signals.AddRange(prediction.Drivers.Where(IsSignalDriver));
            return LimitedList(signals, maxItems: 4, maxItemLength: 90);
        }

        //ToDo: this sucks, either drivers should be a richer type, or the predictor should provide a rationale text
        private static bool IsSignalDriver(string driver)
        {
            var normalized = FlattenWhitespace(driver);
            return !string.IsNullOrWhiteSpace(normalized) &&
                !normalized.StartsWith("Seleccionó ", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("Omitió ", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalized, "No se aplicó ajuste de contexto", StringComparison.OrdinalIgnoreCase);
        }

        private static string AvailabilityText(
            string fixtureId,
            IReadOnlyDictionary<string, IReadOnlyList<AvailabilityClaim>>? availabilityClaimsByFixture)
        {
            if (availabilityClaimsByFixture is null ||
                !availabilityClaimsByFixture.TryGetValue(fixtureId, out var claims))
            {
                return "";
            }

            return LimitedList(
                claims
                    .Where(claim => claim.AffectsPrediction)
                    .OrderBy(claim => claim.TeamName)
                    .ThenBy(claim => claim.Player)
                    .Select(AvailabilityClaimText),
                maxItems: 3,
                maxItemLength: 80);
        }

        private static string AvailabilityClaimText(AvailabilityClaim claim)
        {
            var team = FirstNonEmpty(claim.TeamName, claim.TeamId) ?? "Team";
            return $"{team}: {claim.Player} ({AvailabilityStatusText(claim.Status)})";
        }

        private static int AvailabilitySort(AvailabilityClaim claim)
        {
            if (claim.AffectsPrediction)
                return 0;

            return claim.Status switch
            {
                AvailabilityClaimStatus.ConfirmedOutInjury or
                    AvailabilityClaimStatus.ConfirmedOutIllness or
                    AvailabilityClaimStatus.ConfirmedOutSuspension or
                    AvailabilityClaimStatus.ConfirmedOutOther => 1,
                AvailabilityClaimStatus.Doubtful or AvailabilityClaimStatus.FitnessConcern => 2,
                AvailabilityClaimStatus.Rumor => 3,
                AvailabilityClaimStatus.Available => 4,
                _ => 5
            };
        }

        private static string AvailabilityStatusText(AvailabilityClaimStatus status) => status switch
        {
            AvailabilityClaimStatus.ConfirmedOutInjury => "injury",
            AvailabilityClaimStatus.ConfirmedOutIllness => "illness",
            AvailabilityClaimStatus.ConfirmedOutSuspension => "suspension",
            AvailabilityClaimStatus.ConfirmedOutOther => "out",
            AvailabilityClaimStatus.Doubtful => "doubtful",
            AvailabilityClaimStatus.FitnessConcern => "fitness",
            AvailabilityClaimStatus.Rumor => "rumor",
            AvailabilityClaimStatus.Available => "available",
            AvailabilityClaimStatus.NotRelevant => "not relevant",
            _ => status.ToString()
        };

        private static string LimitedList(IEnumerable<string> values, int maxItems, int maxItemLength)
        {
            var items = values
                .Select(value => PlainSegment(value, maxItemLength))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (items.Count == 0)
                return "";

            var visible = items.Take(maxItems).ToList();
            if (items.Count > maxItems)
                visible.Add($"+{items.Count - maxItems} more");

            return string.Join(", ", visible);
        }

        private static string PlainSegment(string value, int maxLength)
        {
            var flattened = FlattenWhitespace(value);
            if (flattened.Length <= maxLength)
                return flattened;

            return flattened[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
        }

        private static string SanitizeCellSegment(string value, int maxLength)
        {
            var capped = PlainSegment(value, maxLength);
            return WebUtility.HtmlEncode(capped).Replace("|", "&#124;");
        }

        private static string FlattenWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var builder = new StringBuilder(value.Length);
            var previousWasWhitespace = false;
            foreach (var character in value)
            {
                if (char.IsWhiteSpace(character))
                {
                    if (!previousWasWhitespace && builder.Length > 0)
                        builder.Append(' ');
                    previousWasWhitespace = true;
                    continue;
                }

                builder.Append(character);
                previousWasWhitespace = false;
            }

            return builder.ToString().Trim();
        }

        private static string StatusText(Fixture fixture)
        {
            if (fixture.IsPlayed)
                return string.IsNullOrWhiteSpace(fixture.Status) ? "Final" : Escape(fixture.Status);

            if (fixture.KickoffUtc.HasValue)
                return Escape(fixture.KickoffUtc.Value.UtcDateTime.ToString("MMM d HH:mm 'UTC'", CultureInfo.InvariantCulture));

            return "Scheduled";
        }

        private static string TeamCell(string teamId, string teamName)
        {
            var escaped = Escape(teamName);
            var flag = TeamFlagCatalog.CodeFor(teamId, teamName);
            return string.IsNullOrWhiteSpace(flag)
                ? escaped
                : $"<img src=\"Oloraculo.Web/wwwroot/flags/4x3/{flag}.svg\" width=\"18\" alt=\"\"> {escaped}";
        }

        private static string Name(IReadOnlyDictionary<string, string> names, string id) =>
            names.TryGetValue(id, out var name) ? name : id;

        private static string Percent(double value, int digits) =>
            value.ToString($"P{digits}", CultureInfo.InvariantCulture);

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        private static string Escape(string value) => WebUtility.HtmlEncode(value);
    }
}
