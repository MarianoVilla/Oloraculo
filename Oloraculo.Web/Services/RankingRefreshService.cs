using CsvHelper;
using Microsoft.Extensions.Options;
using Oloraculo.Web.Models;
using Oloraculo.Web.Models.CsvModels;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Oloraculo.Web.Services
{
    public class RankingRefreshService
    {
        private readonly HttpClient _http;
        private readonly IWebHostEnvironment _environment;
        private readonly OloraculoConfig _config;

        public RankingRefreshService(HttpClient http, IWebHostEnvironment environment, IOptions<OloraculoConfig> options)
        {
            _http = http;
            _environment = environment;
            _config = options.Value;
        }

        public async Task<RankingRefreshReport> RefreshAsync(DateOnly? today = null, CancellationToken ct = default)
        {
            var updatedFiles = new List<string>();
            var notes = new List<string>();
            var errors = new List<string>();
            var dataDirectory = Path.Combine(_environment.ContentRootPath, "Data");
            Directory.CreateDirectory(dataDirectory);

            int fifaRows = 0;
            int eloRows = 0;
            DateOnly? fifaDate = null;
            DateOnly? eloDate = null;

            try
            {
                var raw = await _http.GetStringAsync(_config.FifaRankingsRawUrl, ct);
                var rows = ParseFifaRankings(raw);
                var csv = ToFifaCsv(rows);
                await WriteAtomicAsync(Path.Combine(dataDirectory, OloraculoDataFiles.FifaRankingsCsv), csv, ct);
                fifaRows = rows.Count;
                fifaDate = DateOnly.ParseExact(rows[0].RankingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                updatedFiles.Add(OloraculoDataFiles.FifaRankingsCsv);
                notes.Add($"FIFA rankings updated: {fifaRows} rows dated {fifaDate:yyyy-MM-dd}.");
            }
            catch (Exception ex)
            {
                errors.Add($"Could not update FIFA rankings: {ex.Message}");
            }

            var lookbackDays = Math.Max(0, _config.EloRefreshMaxLookbackDays);
            var startDate = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var eloFailures = new List<string>();

            for (var offset = 0; offset <= lookbackDays; offset++)
            {
                var date = startDate.AddDays(-offset);
                var url = BuildEloUrl(_config.EloRankingsBaseUrl, date);
                try
                {
                    var html = await _http.GetStringAsync(url, ct);
                    var rows = ParseEloRankings(html, date, url);
                    var csv = ToEloCsv(rows);
                    await WriteAtomicAsync(Path.Combine(dataDirectory, OloraculoDataFiles.EloCsv), csv, ct);
                    eloRows = rows.Count;
                    eloDate = date;
                    updatedFiles.Add(OloraculoDataFiles.EloCsv);
                    notes.Add($"Elo rankings updated: {eloRows} rows dated {eloDate:yyyy-MM-dd}.");
                    break;
                }
                catch (Exception ex)
                {
                    eloFailures.Add($"{date:yyyy-MM-dd}: {ex.Message}");
                }
            }

            if (eloRows == 0)
            {
                var sample = eloFailures.Count == 0 ? "No dates were attempted." : string.Join(" | ", eloFailures.Take(3));
                errors.Add($"Could not refresh Elo rankings after {lookbackDays + 1} date attempts. {sample}");
            }

            return new RankingRefreshReport
            {
                FifaRows = fifaRows,
                EloRows = eloRows,
                FifaRankingDate = fifaDate,
                EloRatingDate = eloDate,
                UpdatedFiles = updatedFiles,
                Notes = notes,
                Errors = errors
            };
        }

        public static IReadOnlyList<FifaCsvRow> ParseFifaRankings(string raw)
        {
            var date = ParseFifaUpdatedDate(raw);
            var rowRegex = new Regex(
                @"\{\s*""(?<team>[^""]+)""\s*,\s*(?<rank>\d+)\s*,\s*(?<change>-?\d+)\s*,\s*(?<points>\d+(?:\.\d+)?)\s*\}",
                RegexOptions.Compiled);

            var rows = rowRegex
                .Matches(raw)
                .Select(match => new FifaCsvRow
                {
                    Rank = int.Parse(match.Groups["rank"].Value, CultureInfo.InvariantCulture),
                    Team = match.Groups["team"].Value.Trim(),
                    RankChangeSincePrevious = int.Parse(match.Groups["change"].Value, CultureInfo.InvariantCulture),
                    Points = decimal.Parse(match.Groups["points"].Value, CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture),
                    RankingDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                })
                .OrderBy(row => row.Rank)
                .ToList();

            if (rows.Count == 0)
                throw new InvalidOperationException("Could not parse any FIFA ranking rows. The source format may have changed.");

            return rows;
        }

        public static IReadOnlyList<EloCsvRow> ParseEloRankings(string html, DateOnly date, string sourceUrl)
        {
            var text = HtmlToPlainText(html);
            var headingIndex = text.IndexOf("World football Elo ratings as on", StringComparison.OrdinalIgnoreCase);
            if (headingIndex < 0)
                throw new InvalidOperationException("Could not find the Elo rating header. The source format may have changed.");

            var relevantText = text[headingIndex..];
            var footerIndex = relevantText.IndexOf("About International-football.net", StringComparison.OrdinalIgnoreCase);
            if (footerIndex >= 0)
                relevantText = relevantText[..footerIndex];

            relevantText = Regex.Replace(relevantText, @"\s+", " ");
            var rowRegex = new Regex(
                @"(?<rank>\d+)\s*\.\s+(?:Image:\s+[^0-9]+?\s+)?(?<team>.+?)\s+(?<rating>\d{3,4})(?=\s+\d+\s*\.|\s*$)",
                RegexOptions.Compiled);

            var rows = rowRegex
                .Matches(relevantText)
                .Select(match => new EloCsvRow
                {
                    Rank = int.Parse(match.Groups["rank"].Value, CultureInfo.InvariantCulture),
                    Team = NormalizeTeamName(match.Groups["team"].Value),
                    Elo = int.Parse(match.Groups["rating"].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                    RatingDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Source = sourceUrl,
                    SourceOriginal = "https://www.eloratings.net/"
                })
                .Where(row => !string.IsNullOrWhiteSpace(row.Team))
                .OrderBy(row => row.Rank)
                .ToList();

            if (rows.Count == 0)
                throw new InvalidOperationException("Could not parse any Elo ranking rows. The source format may have changed.");

            return rows;
        }

        public static string ToFifaCsv(IEnumerable<FifaCsvRow> rows)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.WriteField("rank");
            csv.WriteField("team");
            csv.WriteField("rank_change_since_previous");
            csv.WriteField("points");
            csv.WriteField("ranking_date");
            csv.NextRecord();

            foreach (var row in rows)
            {
                csv.WriteField(row.Rank);
                csv.WriteField(row.Team);
                csv.WriteField(row.RankChangeSincePrevious);
                csv.WriteField(row.Points);
                csv.WriteField(row.RankingDate);
                csv.NextRecord();
            }

            return writer.ToString();
        }

        public static string ToEloCsv(IEnumerable<EloCsvRow> rows)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csv.WriteField("rank");
            csv.WriteField("team");
            csv.WriteField("elo_rating");
            csv.WriteField("rating_date");
            csv.WriteField("source");
            csv.WriteField("source_original");
            csv.NextRecord();

            foreach (var row in rows)
            {
                csv.WriteField(row.Rank);
                csv.WriteField(row.Team);
                csv.WriteField(row.Elo);
                csv.WriteField(row.RatingDate);
                csv.WriteField(row.Source);
                csv.WriteField(row.SourceOriginal);
                csv.NextRecord();
            }

            return writer.ToString();
        }

        public static string BuildEloUrl(string baseUrl, DateOnly date)
        {
            var separator = baseUrl.Contains('?') ? '&' : '?';
            return $"{baseUrl}{separator}day={date.Day:00}&month={date.Month:00}&year={date.Year}";
        }

        private static DateOnly ParseFifaUpdatedDate(string raw)
        {
            var match = Regex.Match(
                raw,
                @"data\.updated\s*=\s*\{\s*day\s*=\s*(?<day>\d+)\s*,\s*month\s*=\s*'(?<month>[^']+)'\s*,\s*year\s*=\s*(?<year>\d+)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                throw new InvalidOperationException("Could not parse the FIFA ranking update date.");

            var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            var month = DateTime.ParseExact(match.Groups["month"].Value, "MMMM", CultureInfo.InvariantCulture).Month;
            var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
            return new DateOnly(year, month, day);
        }

        private static string HtmlToPlainText(string html)
        {
            var text = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", " ");
            return WebUtility.HtmlDecode(text);
        }

        private static string NormalizeTeamName(string value)
        {
            var team = Regex.Replace(value, @"\s+", " ").Trim();
            var parts = team.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length % 2 == 0)
            {
                var half = parts.Length / 2;
                var left = string.Join(' ', parts.Take(half));
                var right = string.Join(' ', parts.Skip(half));
                if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                    return left;
            }

            return team;
        }

        private static async Task WriteAtomicAsync(string path, string contents, CancellationToken ct)
        {
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
                File.Move(tempPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }

}
