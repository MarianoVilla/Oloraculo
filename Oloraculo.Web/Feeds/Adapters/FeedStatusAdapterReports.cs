namespace Oloraculo.Web.Feeds.Adapters
{
    internal static class FeedStatusAdapterReports
    {
        public static FeedAdapterReport Report(
            string sourceId,
            FeedAdapterState state,
            bool configPresent,
            bool? authPresent,
            string detail,
            string? lastError = null,
            int? rowsLastMinute = null,
            double? joinCoverage = null,
            DateTimeOffset? latestRecvTsUtc = null,
            IReadOnlyList<string>? blockers = null)
        {
            var definition = FeedStatusSourceCatalog.All.First(source =>
                string.Equals(source.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));

            return new FeedAdapterReport(
                SourceId: sourceId,
                Source: definition.Source,
                Role: definition.Role,
                State: state,
                ConfigPresent: configPresent,
                AuthPresent: authPresent,
                LatestRecvTsUtc: latestRecvTsUtc,
                RowsLastMinute: rowsLastMinute,
                JoinCoverage: joinCoverage,
                LastError: FeedStatusRedactor.RedactError(lastError),
                Detail: FeedStatusRedactor.RedactError(detail),
                SecretPolicy: definition.SecretPolicy,
                Blockers: blockers);
        }

        public static FeedAdapterReport MissingConfig(string sourceId, string envName) =>
            Report(
                sourceId,
                FeedAdapterState.MissingConfig,
                configPresent: false,
                authPresent: false,
                detail: $"{envName} is absent; value is never displayed",
                blockers: ["AUTH_CONFIG_MISSING"]);

        public static FeedAdapterReport NetworkDisabled(string sourceId, bool authPresent, string blocker = "COLLECTOR_NOT_ENABLED") =>
            Report(
                sourceId,
                FeedAdapterState.Planned,
                configPresent: true,
                authPresent: authPresent,
                detail: "background source probe is disabled for inline status reads",
                blockers: [blocker]);

        public static FeedAdapterReport EntitlementDenied(string sourceId, string detail) =>
            Report(
                sourceId,
                FeedAdapterState.Down,
                configPresent: true,
                authPresent: true,
                detail: detail,
                blockers: ["ENTITLEMENT_DENIED"]);

        public static FeedAdapterReport SourceDown(string sourceId, string detail, string? error = null) =>
            Report(
                sourceId,
                FeedAdapterState.Down,
                configPresent: true,
                authPresent: true,
                detail: detail,
                lastError: error,
                blockers: ["SOURCE_DOWN"]);

        public static FeedAdapterReport ParseError(string sourceId, string error) =>
            Report(
                sourceId,
                FeedAdapterState.ParseError,
                configPresent: true,
                authPresent: true,
                detail: "source response could not be parsed",
                lastError: error,
                latestRecvTsUtc: DateTimeOffset.UtcNow,
                blockers: ["PARSE_ERROR"]);
    }
}
