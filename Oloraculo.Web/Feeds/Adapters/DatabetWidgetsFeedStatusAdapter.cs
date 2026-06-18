using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Oloraculo.Web.Feeds.Adapters
{
    public sealed class DatabetWidgetsFeedStatusAdapter : IFeedStatusAdapter
    {
        private const string Id = "databet_widgets";
        private readonly HttpClient _http;
        private readonly OloraculoConfig _config;
        private readonly Func<string, string?> _environment;

        public DatabetWidgetsFeedStatusAdapter(HttpClient http, IOptions<OloraculoConfig> options)
            : this(http, options, Environment.GetEnvironmentVariable)
        {
        }

        internal DatabetWidgetsFeedStatusAdapter(
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
            var token = _environment(_config.DatabetWidgetsTokenEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(token))
                return FeedStatusAdapterReports.MissingConfig(Id, _config.DatabetWidgetsTokenEnvironmentVariable);

            if (!context.AllowNetwork)
                return FeedStatusAdapterReports.NetworkDisabled(Id, authPresent: true);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _config.DatabetWidgetsBaseUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = GraphQlContent(_config.DatabetWidgetsHealthQuery);
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return FeedStatusAdapterReports.EntitlementDenied(Id, $"Databet widgets returned {(int)response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                    return FeedStatusAdapterReports.SourceDown(Id, $"Databet widgets returned {(int)response.StatusCode}", content);

                using var document = JsonDocument.Parse(content);
                var widgets = FeedStatusAdapterJson.ArrayCount(document.RootElement, "listSportEventWidgets", "widgets", "snapshots");
                if (widgets <= 0)
                {
                    return FeedStatusAdapterReports.Report(
                        Id,
                        FeedAdapterState.Empty,
                        configPresent: true,
                        authPresent: true,
                        detail: "Databet widgets probe returned zero widget snapshots",
                        rowsLastMinute: 0,
                        latestRecvTsUtc: context.AsOfUtc,
                        blockers: ["EMPTY_SOURCE"]);
                }

                return FeedStatusAdapterReports.Report(
                    Id,
                    FeedAdapterState.Ready,
                    configPresent: true,
                    authPresent: true,
                    detail: "Databet widgets snapshot probe succeeded",
                    rowsLastMinute: widgets,
                    latestRecvTsUtc: context.AsOfUtc);
            }
            catch (JsonException ex)
            {
                return FeedStatusAdapterReports.ParseError(Id, ex.Message);
            }
            catch (Exception ex)
            {
                return FeedStatusAdapterReports.SourceDown(Id, "Databet widgets probe failed", ex.Message);
            }
        }

        private static StringContent GraphQlContent(string query)
        {
            var payload = JsonSerializer.Serialize(new { query });
            return new StringContent(payload, Encoding.UTF8, "application/json");
        }
    }
}
