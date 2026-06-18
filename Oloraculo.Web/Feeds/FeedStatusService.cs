using Microsoft.Extensions.Options;
using Oloraculo.Web.Archive;

namespace Oloraculo.Web.Feeds
{
    public interface ISecretPresenceReader
    {
        bool IsPresent(string name);
    }

    public sealed class EnvironmentSecretPresenceReader : ISecretPresenceReader
    {
        public bool IsPresent(string name) => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
    }

    public sealed class FeedStatusService
    {
        private readonly ISecretPresenceReader _secretReader;
        private readonly OloraculoConfig _config;
        private readonly IReadOnlyDictionary<string, IFeedStatusAdapter> _adapters;
        private readonly IFeedStatusHealthStore _healthStore;
        private readonly FeedStatusOptions _feedStatusOptions;
        private readonly Func<DateTimeOffset> _clock;

        public FeedStatusService()
            : this(new EnvironmentSecretPresenceReader(), Microsoft.Extensions.Options.Options.Create(new OloraculoConfig()), () => DateTimeOffset.UtcNow)
        {
        }

        public FeedStatusService(ISecretPresenceReader secretReader, Func<DateTimeOffset>? clock = null)
            : this(secretReader, Microsoft.Extensions.Options.Options.Create(new OloraculoConfig()), clock)
        {
        }

        public FeedStatusService(ISecretPresenceReader secretReader, IOptions<OloraculoConfig> options, Func<DateTimeOffset>? clock = null)
            : this(
                secretReader,
                options,
                [],
                new InMemoryFeedStatusHealthStore(),
                Microsoft.Extensions.Options.Options.Create(new FeedStatusOptions()),
                clock)
        {
        }

        public FeedStatusService(
            ISecretPresenceReader secretReader,
            IOptions<OloraculoConfig> options,
            IEnumerable<IFeedStatusAdapter> adapters,
            IFeedStatusHealthStore healthStore,
            IOptions<FeedStatusOptions> feedStatusOptions,
            Func<DateTimeOffset>? clock = null)
        {
            _secretReader = secretReader;
            _config = options.Value;
            _adapters = adapters
                .GroupBy(adapter => adapter.SourceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            _healthStore = healthStore;
            _feedStatusOptions = feedStatusOptions.Value;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        public FeedStatusSnapshot Snapshot()
        {
            var asOf = _clock();
            return new FeedStatusSnapshot
            {
                AsOfUtc = asOf,
                Rows = FeedStatusSourceCatalog.All.Select(source => RowFor(source, asOf)).ToArray()
            };
        }

        public static string RedactError(string? error)
        {
            return FeedStatusRedactor.RedactError(error);
        }

        private FeedStatusRow RowFor(FeedStatusSourceDefinition source, DateTimeOffset asOf)
        {
            var staleAfter = StaleAfterFor(source.SourceId);
            if (_healthStore.TryGet(source.SourceId, out var cachedReport))
                return FeedStatusRow.FromAdapter(cachedReport, asOf, staleAfter);

            if (_adapters.TryGetValue(source.SourceId, out var adapter))
            {
                var report = adapter.Probe(new FeedStatusProbeContext(asOf, staleAfter, AllowNetwork: false));
                return FeedStatusRow.FromAdapter(report, asOf, staleAfter);
            }

            return source.SourceId switch
            {
                "databet_sportsbook" => SecretBacked(source, "SPORTSBOOK_XAUTH", "XAUTH configured; adapter contract ready, live collector not enabled in Oloraculo yet"),
                "databet_widgets" => SecretBacked(source, "DATABET_WIDGET_TOKEN", "Widget token configured; per-session token must never be displayed"),
                "oddspapi_pinnacle" => SecretBacked(source, "ODDSPAPI_KEY", "key configured; adapter contract ready, collector pending"),
                "grid" => SecretBacked(source, "GRID_KEY", "key configured; entitlement/result-count status only"),
                "polymarket_clob" => PublicPlanned(source, "public CLOB access; Rust hotpath owns live freshness once collector is enabled"),
                "object_archive" => R2Archive(source),
                _ => PublicPlanned(source, "source status probe is not implemented")
            };
        }

        private TimeSpan StaleAfterFor(string sourceId)
        {
            if (_feedStatusOptions.StaleAfterSecondsBySource.TryGetValue(sourceId, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromSeconds(Math.Max(1, _feedStatusOptions.DefaultStaleAfterSeconds));
        }

        private FeedStatusRow SecretBacked(FeedStatusSourceDefinition source, string envName, string readyDetail)
        {
            var present = _secretReader.IsPresent(envName);
            return new FeedStatusRow
            {
                SourceId = source.SourceId,
                Source = source.Source,
                Role = source.Role,
                Readiness = present ? FeedReadiness.Planned : FeedReadiness.ConfigMissing,
                Present = false,
                AuthPresent = present,
                ConfigPresent = present,
                LatestRecvTsUtc = null,
                AgeMs = null,
                RowsLastMinute = null,
                JoinCoverage = null,
                Blocker = present ? "COLLECTOR_NOT_ENABLED" : "AUTH_CONFIG_MISSING",
                Blockers = [present ? "COLLECTOR_NOT_ENABLED" : "AUTH_CONFIG_MISSING"],
                Detail = present ? readyDetail : "credential/config absent or not loaded; value is never displayed",
                SecretPolicy = source.SecretPolicy
            };
        }

        private static FeedStatusRow PublicPlanned(FeedStatusSourceDefinition source, string detail) => new()
        {
            SourceId = source.SourceId,
            Source = source.Source,
            Role = source.Role,
            Readiness = FeedReadiness.Planned,
            Present = false,
            AuthPresent = null,
            ConfigPresent = true,
            Blocker = "LIVE_COLLECTOR_PENDING",
            Blockers = ["LIVE_COLLECTOR_PENDING"],
            Detail = detail,
            SecretPolicy = source.SecretPolicy
        };

        private FeedStatusRow R2Archive(FeedStatusSourceDefinition source)
        {
            var archive = _config.ObjectArchive ?? new ObjectArchiveConfig();
            var bucket = !string.IsNullOrWhiteSpace(archive.Bucket) || _secretReader.IsPresent(archive.BucketEnvironmentVariable);
            var endpoint = !string.IsNullOrWhiteSpace(archive.Endpoint) || _secretReader.IsPresent(archive.EndpointEnvironmentVariable);
            var region = !string.IsNullOrWhiteSpace(archive.Region) || _secretReader.IsPresent(archive.RegionEnvironmentVariable);
            var key = _secretReader.IsPresent(archive.AccessKeyIdEnvironmentVariable);
            var secret = _secretReader.IsPresent(archive.SecretAccessKeyEnvironmentVariable);
            var endpointRequired = !string.Equals(archive.Provider, "S3", StringComparison.OrdinalIgnoreCase) || endpoint;
            var complete = bucket && key && secret && region && (!endpointRequired || endpoint);
            var readiness = ObjectArchiveConfigResolver.Resolve(_config).ToReadiness();
            return new FeedStatusRow
            {
                SourceId = source.SourceId,
                Source = $"{readiness.Provider} object archive",
                Role = source.Role,
                Readiness = complete ? FeedReadiness.Planned : FeedReadiness.ConfigMissing,
                Present = false,
                AuthPresent = key && secret,
                ConfigPresent = complete,
                Blocker = complete && archive.Enabled ? "ARCHIVER_HEALTH_UNVERIFIED" : complete ? "ARCHIVER_DISABLED" : "OBJECT_ARCHIVE_CONFIG_INCOMPLETE",
                Blockers = [complete && archive.Enabled ? "ARCHIVER_HEALTH_UNVERIFIED" : complete ? "ARCHIVER_DISABLED" : "OBJECT_ARCHIVE_CONFIG_INCOMPLETE"],
                Detail = complete
                    ? $"{readiness.Detail}; upload/list health is not measured by feed status yet"
                    : "requires bucket, endpoint, access key id, and secret access key; values are never displayed",
                SecretPolicy = source.SecretPolicy
            };
        }
    }
}
