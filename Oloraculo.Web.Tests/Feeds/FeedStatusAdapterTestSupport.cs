using System.Net;
using Oloraculo.Web.Feeds;

namespace Oloraculo.Web.Tests.Feeds;

internal static class FeedStatusAdapterTestSupport
{
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-18T17:00:00Z");

    public static FeedStatusProbeContext NetworkContext =>
        new(Now, TimeSpan.FromSeconds(30), AllowNetwork: true);

    public static FeedStatusProbeContext NoNetworkContext =>
        new(Now, TimeSpan.FromSeconds(30), AllowNetwork: false);

    public static Func<string, string?> Env(IReadOnlyDictionary<string, string> values) =>
        name => values.TryGetValue(name, out var value) ? value : null;

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string content) => new(statusCode)
    {
        Content = new StringContent(content)
    };
}

internal sealed class CapturingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

    public CapturingHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<string> RequestUris { get; } = [];
    public List<string> RequestBodies { get; } = [];
    public List<string> AuthorizationHeaders { get; } = [];
    public List<string> XAuthHeaders { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

        RequestUris.Add(request.RequestUri?.ToString() ?? string.Empty);
        RequestBodies.Add(body);
        if (request.Headers.Authorization is { } authorization)
            AuthorizationHeaders.Add($"{authorization.Scheme} <redacted>");
        if (request.Headers.TryGetValues("X-Auth-Token", out var xauth))
            XAuthHeaders.AddRange(xauth.Select(_ => "<redacted>"));

        return Task.FromResult(_responder(request, body));
    }
}
