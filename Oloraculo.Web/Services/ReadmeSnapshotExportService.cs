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

            var fixtures = await _db.Fixtures.AsNoTracking().ToListAsync(ct);
            var orderedFixtures = OrderedFixtures(fixtures).ToList();
            var predictions = await _prediction.PredictFixturesAsync(orderedFixtures, ct);
            await _snapshots.SaveFullFixtureAsync(predictions.Select(result => result.BestPrediction), ct);

            var projection = await _simulation.RunAsync(saveSnapshot: true, ct: ct);
            var names = await _db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);

            var block = RenderSnapshotBlock(projection, predictions, names, DateTimeOffset.UtcNow);
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
            DateTimeOffset generatedAt)
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
                builder.AppendLine("| Match | Status | Result / Pick | H | D | A |");
                builder.AppendLine("| --- | --- | --- | ---: | ---: | ---: |");
                foreach (var result in OrderedPredictions(group))
                {
                    var fixture = result.Fixture;
                    var prediction = result.BestPrediction;
                    var home = TeamCell(fixture.HomeTeamId, result.HomeTeamName);
                    var away = TeamCell(fixture.AwayTeamId, result.AwayTeamName);
                    var status = StatusText(fixture);
                    var pick = ResultOrPickText(fixture, prediction);
                    builder.AppendLine($"| {home} vs {away} | {status} | {pick} | {Percent(prediction.Outcome.HomeWin, 0)} | {Percent(prediction.Outcome.Draw, 0)} | {Percent(prediction.Outcome.AwayWin, 0)} |");
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

        private static string Escape(string value) => WebUtility.HtmlEncode(value);
    }
}
