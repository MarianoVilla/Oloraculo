using Microsoft.Extensions.Options;
using Oloraculo.Web.Archive;

namespace Oloraculo.Web.Feeds.Adapters
{
    public sealed class ObjectArchiveFeedStatusAdapter : IFeedStatusAdapter
    {
        private const string Id = "object_archive";
        private readonly OloraculoConfig _config;
        private readonly IObjectArchiveHealthProbe _probe;
        private readonly Func<string, string?> _environment;

        public ObjectArchiveFeedStatusAdapter(IOptions<OloraculoConfig> options, IObjectArchiveHealthProbe probe)
            : this(options, probe, Environment.GetEnvironmentVariable)
        {
        }

        internal ObjectArchiveFeedStatusAdapter(
            IOptions<OloraculoConfig> options,
            IObjectArchiveHealthProbe probe,
            Func<string, string?> environment)
        {
            _config = options.Value;
            _probe = probe;
            _environment = environment;
        }

        public string SourceId => Id;

        public FeedAdapterReport Probe(FeedStatusProbeContext context)
        {
            var resolved = ObjectArchiveConfigResolver.Resolve(_config, _environment);
            if (!resolved.IsConfigured)
            {
                return Report(
                    FeedAdapterState.MissingConfig,
                    resolved,
                    detail: resolved.ToReadiness().Detail,
                    blockers: ["OBJECT_ARCHIVE_CONFIG_INCOMPLETE"]);
            }

            if (!resolved.Enabled)
            {
                return Report(
                    FeedAdapterState.Planned,
                    resolved,
                    detail: "object archive is disabled by config",
                    blockers: ["ARCHIVER_DISABLED"]);
            }

            if (!context.AllowNetwork)
            {
                return Report(
                    FeedAdapterState.Planned,
                    resolved,
                    detail: "object archive config is present but manifest health is unverified",
                    blockers: ["ARCHIVER_HEALTH_UNVERIFIED"]);
            }

            var health = _probe.Probe(context.AsOfUtc);
            if (!string.IsNullOrWhiteSpace(health.LastError))
            {
                return Report(
                    FeedAdapterState.Down,
                    resolved,
                    detail: "object archive manifest/list health check failed",
                    lastError: health.LastError,
                    blockers: ["ARCHIVE_LIST_DENIED"]);
            }

            var archiveConfig = _config.ObjectArchive ?? new ObjectArchiveConfig();
            if (health.PendingLocalBatchCount > Math.Max(0, archiveConfig.MaxPendingLocalBatchCount))
            {
                return Report(
                    FeedAdapterState.Blocked,
                    resolved,
                    latestRecvTsUtc: health.LastVerifiedManifestUtc,
                    rows: health.PendingLocalBatchCount,
                    detail: "object archive local backlog is above threshold",
                    blockers: ["ARCHIVE_BACKLOG"]);
            }

            if (!health.LastVerifiedManifestUtc.HasValue)
            {
                return Report(
                    FeedAdapterState.Planned,
                    resolved,
                    detail: "object archive config is present but manifest health is unverified",
                    blockers: ["ARCHIVER_HEALTH_UNVERIFIED"]);
            }

            if ((context.AsOfUtc - health.LastVerifiedManifestUtc.Value) > context.StaleAfter)
            {
                return Report(
                    FeedAdapterState.Stale,
                    resolved,
                    latestRecvTsUtc: health.LastVerifiedManifestUtc,
                    rows: health.PendingLocalBatchCount,
                    detail: "object archive last verified manifest is stale",
                    blockers: ["ARCHIVE_STALE"]);
            }

            return Report(
                FeedAdapterState.Ready,
                resolved,
                latestRecvTsUtc: health.LastVerifiedManifestUtc,
                rows: health.PendingLocalBatchCount,
                detail: "object archive manifest health verified recently");
        }

        private static FeedAdapterReport Report(
            FeedAdapterState state,
            ResolvedObjectArchiveConfig config,
            string detail,
            string? lastError = null,
            DateTimeOffset? latestRecvTsUtc = null,
            int? rows = null,
            IReadOnlyList<string>? blockers = null)
        {
            var definition = FeedStatusSourceCatalog.All.First(source => source.SourceId == Id);
            return new FeedAdapterReport(
                Id,
                $"{config.Provider} object archive",
                definition.Role,
                state,
                ConfigPresent: config.IsConfigured,
                AuthPresent: config.HasCredentials,
                LatestRecvTsUtc: latestRecvTsUtc,
                RowsLastMinute: rows,
                LastError: FeedStatusRedactor.RedactError(lastError),
                Detail: FeedStatusRedactor.RedactError(detail),
                SecretPolicy: definition.SecretPolicy,
                Blockers: blockers);
        }
    }
}
