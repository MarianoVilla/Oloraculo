using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Oloraculo.Web.Feeds.Adapters
{
    public sealed class GridFeedStatusAdapter : IFeedStatusAdapter
    {
        private const string Id = "grid";
        private readonly HttpClient _http;
        private readonly OloraculoConfig _config;
        private readonly Func<string, string?> _environment;

        public GridFeedStatusAdapter(HttpClient http, IOptions<OloraculoConfig> options)
            : this(http, options, Environment.GetEnvironmentVariable)
        {
        }

        internal GridFeedStatusAdapter(
            HttpClient http,
            IOptions<OloraculoConfig> options,
            Func<string, string?> environment)
        {
            _http = http;
            _config = options.Value;
            _environment = environment;
        }

        public string SourceId => Id;

        public FeedAdapterReport Probe(FeedStatusProbeContext context)
        {
            var key = _environment(_config.GridKeyEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(key))
                return FeedStatusAdapterReports.MissingConfig(Id, _config.GridKeyEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(_config.GridBaseUrl))
            {
                return FeedStatusAdapterReports.Report(
                    Id,
                    FeedAdapterState.NotImplemented,
                    configPresent: true,
                    authPresent: true,
                    detail: "GRID endpoint is not configured; no stable local probe path exists yet",
                    blockers: ["GRID_PROBE_NOT_IMPLEMENTED"]);
            }

            if (!context.AllowNetwork)
                return FeedStatusAdapterReports.NetworkDisabled(Id, authPresent: true);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, BuildHealthUri());
                request.Headers.TryAddWithoutValidation("X-Api-Key", key);
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return FeedStatusAdapterReports.EntitlementDenied(Id, $"GRID returned {(int)response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                    return FeedStatusAdapterReports.SourceDown(Id, $"GRID returned {(int)response.StatusCode}", content);

                using var document = JsonDocument.Parse(content);
                var rows = FeedStatusAdapterJson.ArrayCount(document.RootElement, "events") +
                    FeedStatusAdapterJson.ArrayCount(document.RootElement, "telemetry");
                if (rows <= 0)
                {
                    return FeedStatusAdapterReports.Report(
                        Id,
                        FeedAdapterState.Empty,
                        configPresent: true,
                        authPresent: true,
                        detail: "GRID probe returned zero event or telemetry rows",
                        rowsLastMinute: 0,
                        latestRecvTsUtc: context.AsOfUtc,
                        blockers: ["EMPTY_SOURCE"]);
                }

                return FeedStatusAdapterReports.Report(
                    Id,
                    FeedAdapterState.Ready,
                    configPresent: true,
                    authPresent: true,
                    detail: "GRID event/telemetry probe succeeded",
                    rowsLastMinute: rows,
                    latestRecvTsUtc: context.AsOfUtc);
            }
            catch (JsonException ex)
            {
                return FeedStatusAdapterReports.ParseError(Id, ex.Message);
            }
            catch (Exception ex)
            {
                return FeedStatusAdapterReports.SourceDown(Id, "GRID probe failed", ex.Message);
            }
        }

        private string BuildHealthUri()
        {
            var baseUri = _config.GridBaseUrl.EndsWith("/", StringComparison.Ordinal)
                ? _config.GridBaseUrl
                : _config.GridBaseUrl + "/";
            return new Uri(new Uri(baseUri, UriKind.Absolute), _config.GridHealthPath.TrimStart('/')).ToString();
        }
    }
}
