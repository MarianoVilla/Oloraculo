using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Oloraculo.Web.Feeds.Adapters
{
    public sealed class DatabetSportsbookFeedStatusAdapter : IFeedStatusAdapter
    {
        private const string Id = "databet_sportsbook";
        private readonly HttpClient _http;
        private readonly OloraculoConfig _config;
        private readonly Func<string, string?> _environment;

        public DatabetSportsbookFeedStatusAdapter(HttpClient http, IOptions<OloraculoConfig> options)
            : this(http, options, Environment.GetEnvironmentVariable)
        {
        }

        internal DatabetSportsbookFeedStatusAdapter(
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
            var token = _environment(_config.DatabetSportsbookAuthEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(token))
                return FeedStatusAdapterReports.MissingConfig(Id, _config.DatabetSportsbookAuthEnvironmentVariable);

            if (!context.AllowNetwork)
                return FeedStatusAdapterReports.NetworkDisabled(Id, authPresent: true);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _config.DatabetSportsbookBaseUrl);
                request.Headers.TryAddWithoutValidation("X-Auth-Token", token);
                request.Content = GraphQlContent(_config.DatabetSportsbookHealthQuery);
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return FeedStatusAdapterReports.EntitlementDenied(Id, $"Databet sportsbook returned {(int)response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                    return FeedStatusAdapterReports.SourceDown(Id, $"Databet sportsbook returned {(int)response.StatusCode}", content);

                using var document = JsonDocument.Parse(content);
                var events = FeedStatusAdapterJson.ArrayCount(document.RootElement, "sportEvents");
                if (events <= 0)
                {
                    return FeedStatusAdapterReports.Report(
                        Id,
                        FeedAdapterState.Empty,
                        configPresent: true,
                        authPresent: true,
                        detail: "Databet sportsbook probe returned zero live events",
                        rowsLastMinute: 0,
                        latestRecvTsUtc: context.AsOfUtc,
                        blockers: ["EMPTY_SOURCE"]);
                }

                return FeedStatusAdapterReports.Report(
                    Id,
                    FeedAdapterState.Ready,
                    configPresent: true,
                    authPresent: true,
                    detail: "Databet sportsbook live event probe succeeded",
                    rowsLastMinute: events,
                    latestRecvTsUtc: context.AsOfUtc);
            }
            catch (JsonException ex)
            {
                return FeedStatusAdapterReports.ParseError(Id, ex.Message);
            }
            catch (Exception ex)
            {
                return FeedStatusAdapterReports.SourceDown(Id, "Databet sportsbook probe failed", ex.Message);
            }
        }

        private static StringContent GraphQlContent(string query)
        {
            var payload = JsonSerializer.Serialize(new { query });
            return new StringContent(payload, Encoding.UTF8, "application/json");
        }
    }
}
