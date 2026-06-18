using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Oloraculo.Web.Feeds
{
    public enum FeedReadiness
    {
        Ready,
        Planned,
        ConfigMissing,
        NotImplemented,
        Blocked
    }

    public enum FeedAdapterState
    {
        MissingConfig,
        Down,
        Stale,
        Empty,
        ParseError,
        Blocked,
        Planned,
        NotImplemented,
        Ready
    }

    public static class FeedReadinessExtensions
    {
        public static string ToContractState(this FeedReadiness readiness) => readiness switch
        {
            FeedReadiness.Ready => "READY",
            FeedReadiness.Planned => "PLANNED",
            FeedReadiness.ConfigMissing => "CONFIG_MISSING",
            FeedReadiness.NotImplemented => "NOT_IMPLEMENTED",
            FeedReadiness.Blocked => "BLOCKED",
            _ => "UNKNOWN"
        };
    }

    public sealed record FeedAdapterReport(
        string SourceId,
        string Source,
        string Role,
        FeedAdapterState State,
        bool ConfigPresent,
        bool? AuthPresent = null,
        DateTimeOffset? LatestRecvTsUtc = null,
        int? RowsLastMinute = null,
        double? JoinCoverage = null,
        string? LastError = null,
        string Detail = "",
        string SecretPolicy = "NO_SECRET_DISPLAY",
        IReadOnlyList<string>? Blockers = null);

    public sealed record FeedStatusRow
    {
        [JsonPropertyName("source_id")]
        public string SourceId { get; init; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; init; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; init; } = string.Empty;

        [JsonIgnore]
        public FeedReadiness Readiness { get; init; }

        [JsonPropertyName("readiness")]
        public string ReadinessCode => Readiness.ToContractState();

        [JsonPropertyName("state")]
        public string State => Readiness.ToContractState();

        [JsonPropertyName("present")]
        public bool Present { get; init; }

        [JsonPropertyName("auth_present")]
        public bool? AuthPresent { get; init; }

        [JsonPropertyName("config_present")]
        public bool ConfigPresent { get; init; }

        [JsonPropertyName("latest_recv_ts_utc")]
        public DateTimeOffset? LatestRecvTsUtc { get; init; }

        [JsonPropertyName("age_ms")]
        public double? AgeMs { get; init; }

        [JsonPropertyName("rows_last_minute")]
        public int? RowsLastMinute { get; init; }

        [JsonPropertyName("join_coverage")]
        public double? JoinCoverage { get; init; }

        [JsonPropertyName("last_error_redacted")]
        public string LastErrorRedacted { get; init; } = string.Empty;

        [JsonPropertyName("blocker")]
        public string Blocker { get; init; } = string.Empty;

        [JsonPropertyName("blockers")]
        public IReadOnlyList<string> Blockers { get; init; } = [];

        [JsonPropertyName("detail")]
        public string Detail { get; init; } = string.Empty;

        [JsonPropertyName("secret_policy")]
        public string SecretPolicy { get; init; } = "NO_SECRET_DISPLAY";

        public static FeedStatusRow FromAdapter(
            FeedAdapterReport report,
            DateTimeOffset asOfUtc,
            TimeSpan? staleAfter = null)
        {
            var ageMs = report.LatestRecvTsUtc is { } recv
                ? Math.Max(0, (asOfUtc - recv).TotalMilliseconds)
                : (double?)null;
            var staleThreshold = staleAfter ?? TimeSpan.FromSeconds(30);
            var readyMissingMeasuredData = report.State == FeedAdapterState.Ready &&
                (report.LatestRecvTsUtc is null || !report.RowsLastMinute.HasValue);
            var inferredState = readyMissingMeasuredData
                ? FeedAdapterState.Blocked
                : report.State == FeedAdapterState.Ready &&
                    ageMs is not null &&
                    ageMs > staleThreshold.TotalMilliseconds
                        ? FeedAdapterState.Stale
                        : report.State;
            var readiness = inferredState switch
            {
                FeedAdapterState.MissingConfig => FeedReadiness.ConfigMissing,
                FeedAdapterState.Planned => FeedReadiness.Planned,
                FeedAdapterState.NotImplemented => FeedReadiness.NotImplemented,
                FeedAdapterState.Ready => FeedReadiness.Ready,
                _ => FeedReadiness.Blocked
            };
            var present = inferredState is FeedAdapterState.Stale
                or FeedAdapterState.Empty
                or FeedAdapterState.ParseError
                or FeedAdapterState.Ready;
            var defaultBlockers = readyMissingMeasuredData
                ? new[] { "MEASURED_DATA_MISSING" }
                : inferredState switch
            {
                FeedAdapterState.MissingConfig => new[] { "AUTH_CONFIG_MISSING" },
                FeedAdapterState.Down => new[] { "SOURCE_DOWN" },
                FeedAdapterState.Stale => new[] { "STALE_SOURCE" },
                FeedAdapterState.Empty => new[] { "EMPTY_SOURCE" },
                FeedAdapterState.ParseError => new[] { "PARSE_ERROR" },
                FeedAdapterState.Blocked => new[] { "SOURCE_BLOCKED" },
                FeedAdapterState.Planned => new[] { "COLLECTOR_NOT_ENABLED" },
                FeedAdapterState.NotImplemented => new[] { "NOT_IMPLEMENTED" },
                _ => []
            };
            var customBlockers = report.Blockers?
                .Select(FeedStatusRedactor.RedactError)
                .Select(blocker => blocker.Trim())
                .Where(blocker => !string.IsNullOrWhiteSpace(blocker))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var blockers = customBlockers is { Length: > 0 }
                ? customBlockers
                : defaultBlockers;

            return new FeedStatusRow
            {
                SourceId = report.SourceId,
                Source = report.Source,
                Role = report.Role,
                Readiness = readiness,
                Present = present,
                AuthPresent = report.AuthPresent,
                ConfigPresent = report.ConfigPresent,
                LatestRecvTsUtc = report.LatestRecvTsUtc,
                AgeMs = ageMs,
                RowsLastMinute = report.RowsLastMinute,
                JoinCoverage = report.JoinCoverage,
                LastErrorRedacted = FeedStatusRedactor.RedactError(report.LastError),
                Blocker = blockers.FirstOrDefault() ?? string.Empty,
                Blockers = blockers,
                Detail = FeedStatusRedactor.RedactError(report.Detail),
                SecretPolicy = report.SecretPolicy
            };
        }
    }

    public sealed record FeedStatusSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        [JsonPropertyName("as_of_utc")]
        public DateTimeOffset AsOfUtc { get; init; }

        [JsonPropertyName("generated_at_utc")]
        public DateTimeOffset GeneratedAtUtc => AsOfUtc;

        [JsonPropertyName("rows")]
        public IReadOnlyList<FeedStatusRow> Rows { get; init; } = [];

        [JsonPropertyName("mode")]
        public string Mode => "SANITIZED_STATUS_ONLY";

        [JsonPropertyName("ready_count")]
        public int ReadyCount => Rows.Count(row => row.Readiness == FeedReadiness.Ready);

        [JsonPropertyName("planned_count")]
        public int PlannedCount => Rows.Count(row => row.Readiness == FeedReadiness.Planned);

        [JsonPropertyName("missing_config_count")]
        public int MissingConfigCount => Rows.Count(row => row.Readiness == FeedReadiness.ConfigMissing);

        [JsonPropertyName("not_implemented_count")]
        public int NotImplementedCount => Rows.Count(row => row.Readiness == FeedReadiness.NotImplemented);

        [JsonPropertyName("blocked_count")]
        public int BlockedCount => Rows.Count(row => row.Readiness == FeedReadiness.Blocked);
    }

    public static class FeedStatusRedactor
    {
        public static string RedactError(string? error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return string.Empty;

            var redacted = error;
            redacted = Regex.Replace(redacted, @"ghp_[A-Za-z0-9_]{20,}", "<redacted-github-token>");
            redacted = Regex.Replace(redacted, @"github_pat_[A-Za-z0-9_]{20,}", "<redacted-github-token>");
            redacted = Regex.Replace(redacted, @"AKIA[0-9A-Z]{16}", "<redacted-aws-access-key>");
            redacted = Regex.Replace(redacted, @"AIza[0-9A-Za-z_-]{35}", "<redacted-google-api-key>");
            redacted = Regex.Replace(redacted, @"xox[baprs]-[0-9A-Za-z-]{20,}", "<redacted-slack-token>");
            redacted = Regex.Replace(redacted, @"-----BEGIN (RSA |EC |OPENSSH |DSA |)?PRIVATE KEY-----", "<redacted-private-key>");
            redacted = Regex.Replace(redacted, @"(?i)\bauthorization\s*:\s*bearer\s+[^\s&]+", "Authorization: Bearer <redacted>");
            redacted = Regex.Replace(redacted, @"(?i)\bbearer\s+[^\s&]+", "Bearer <redacted>");
            redacted = Regex.Replace(redacted, @"(?i)([?&](?:api[_-]?key|xauth|token|secret|private[_-]?key)=)[^&\s]+", "$1<redacted>");
            redacted = Regex.Replace(redacted, @"(?i)(bearer|token|xauth|secret|private[_-]?key|api[_-]?key)\s*[:=]\s*\S+", "$1=<redacted>");
            redacted = Regex.Replace(redacted, @"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", "<redacted-jwt>");
            redacted = Regex.Replace(redacted, @"0x[0-9a-fA-F]{64}", "<redacted-private-key>");
            return redacted.Length > 240 ? redacted[..240] + "..." : redacted;
        }
    }
}
