namespace Oloraculo.Web.Feeds
{
    public sealed class FeedStatusOptions
    {
        public bool EnableBackgroundProbes { get; set; } = false;

        public bool EnableInlineNetworkProbes { get; set; } = false;

        public int ProbeIntervalSeconds { get; set; } = 15;

        public int ProbeTimeoutMilliseconds { get; set; } = 2500;

        public int DefaultStaleAfterSeconds { get; set; } = 30;

        public Dictionary<string, int> StaleAfterSecondsBySource { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
