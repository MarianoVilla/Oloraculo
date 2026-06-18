namespace Oloraculo.Web.Feeds
{
    public sealed record FeedStatusProbeContext(
        DateTimeOffset AsOfUtc,
        TimeSpan StaleAfter,
        bool AllowNetwork);
}
