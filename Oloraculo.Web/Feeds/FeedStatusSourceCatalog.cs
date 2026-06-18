namespace Oloraculo.Web.Feeds
{
    public sealed record FeedStatusSourceDefinition(
        string SourceId,
        string Source,
        string Role,
        string SecretPolicy);

    public static class FeedStatusSourceCatalog
    {
        public static IReadOnlyList<FeedStatusSourceDefinition> All { get; } =
        [
            new("databet_sportsbook", "Databet sportsbook", "external live odds/state", "PRESENCE_ONLY_NO_VALUES"),
            new("databet_widgets", "Databet widgets", "widget game-state/status", "PRESENCE_ONLY_NO_VALUES"),
            new("oddspapi_pinnacle", "OddsPapi/Pinnacle", "sharp consensus odds", "PRESENCE_ONLY_NO_VALUES"),
            new("grid", "GRID", "esports telemetry", "PRESENCE_ONLY_NO_VALUES"),
            new("polymarket_clob", "Polymarket CLOB", "executable book/trade hotpath", "PUBLIC_OR_STATUS_ONLY"),
            new("object_archive", "object archive", "raw/bronze/silver/gold object storage", "PRESENCE_ONLY_NO_VALUES")
        ];
    }
}
