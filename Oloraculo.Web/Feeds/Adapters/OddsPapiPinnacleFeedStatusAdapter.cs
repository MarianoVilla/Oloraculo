using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Oloraculo.Web.Feeds.Adapters
{
    public sealed class OddsPapiPinnacleFeedStatusAdapter : IFeedStatusAdapter
    {
        private const string Id = "oddspapi_pinnacle";
        private readonly HttpClient _http;
        private readonly OloraculoConfig _config;
        private readonly Func<string, string?> _environment;

        public OddsPapiPinnacleFeedStatusAdapter(HttpClient http, IOptions<OloraculoConfig> options)
            : this(http, options, Environment.GetEnvironmentVariable)
        {
        }

        internal OddsPapiPinnacleFeedStatusAdapter(
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
            var key = _environment(_config.OddsPapiKeyEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(key))
                return FeedStatusAdapterReports.MissingConfig(Id, _config.OddsPapiKeyEnvironmentVariable);

            if (!context.AllowNetwork)
                return FeedStatusAdapterReports.NetworkDisabled(Id, authPresent: true);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, BuildHealthUri());
                request.Headers.TryAddWithoutValidation("X-Api-Key", key);
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return FeedStatusAdapterReports.EntitlementDenied(Id, $"OddsPapi returned {(int)response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                    return FeedStatusAdapterReports.SourceDown(Id, $"OddsPapi returned {(int)response.StatusCode}", content);

                using var document = JsonDocument.Parse(content);
                var events = FeedStatusAdapterJson.ArrayCount(document.RootElement, "data", "events", "fixtures");
                if (events <= 0)
                {
                    return FeedStatusAdapterReports.Report(
                        Id,
                        FeedAdapterState.Empty,
                        configPresent: true,
                        authPresent: true,
                        detail: "OddsPapi probe returned zero events",
                        rowsLastMinute: 0,
                        latestRecvTsUtc: context.AsOfUtc,
                        blockers: ["EMPTY_SOURCE"]);
                }

                if (!FeedStatusAdapterJson.HasPropertyName(document.RootElement, _config.OddsPapiBookmaker))
                {
                    return FeedStatusAdapterReports.Report(
                        Id,
                        FeedAdapterState.Empty,
                        configPresent: true,
                        authPresent: true,
                        detail: $"{_config.OddsPapiBookmaker} coverage missing in OddsPapi probe",
                        rowsLastMinute: events,
                        joinCoverage: 0,
                        latestRecvTsUtc: context.AsOfUtc,
                        blockers: ["PINNACLE_COVERAGE_MISSING"]);
                }

                return FeedStatusAdapterReports.Report(
                    Id,
                    FeedAdapterState.Ready,
                    configPresent: true,
                    authPresent: true,
                    detail: $"{_config.OddsPapiBookmaker} coverage probe succeeded",
                    rowsLastMinute: events,
                    joinCoverage: 1,
                    latestRecvTsUtc: context.AsOfUtc);
            }
            catch (JsonException ex)
            {
                return FeedStatusAdapterReports.ParseError(Id, ex.Message);
            }
            catch (Exception ex)
            {
                return FeedStatusAdapterReports.SourceDown(Id, "OddsPapi probe failed", ex.Message);
            }
        }

        private string BuildHealthUri()
        {
            var baseUrl = string.IsNullOrWhiteSpace(_config.OddsPapiBaseUrl)
                ? "https://api.oddspapi.io/v4/"
                : _config.OddsPapiBaseUrl;
            var path = string.IsNullOrWhiteSpace(_config.OddsPapiHealthPath)
                ? "odds-by-tournaments?tournamentIds=16"
                : _config.OddsPapiHealthPath.TrimStart('/');
            var uri = new Uri(new Uri(baseUrl, UriKind.Absolute), path).ToString();
            if (uri.Contains("bookmaker=", StringComparison.OrdinalIgnoreCase))
                return uri;
            var separator = uri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return uri + separator + "bookmaker=" + Uri.EscapeDataString(_config.OddsPapiBookmaker);
        }
    }
}
