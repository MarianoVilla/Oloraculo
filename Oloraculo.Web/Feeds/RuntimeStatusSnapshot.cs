using System.Text.Json.Serialization;
using Oloraculo.Web.Archive;

namespace Oloraculo.Web.Feeds
{
    public sealed record RuntimeStatusSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        [JsonPropertyName("generated_at_utc")]
        public DateTimeOffset GeneratedAtUtc { get; init; }

        [JsonPropertyName("mode")]
        public string Mode { get; init; } = "READ_ONLY_STATUS_ONLY";

        [JsonPropertyName("archive")]
        public ObjectArchiveReadiness Archive { get; init; } = new(false, false, "R2", "not evaluated");

        [JsonPropertyName("feeds")]
        public FeedStatusSnapshot Feeds { get; init; } = new();

        public static RuntimeStatusSnapshot Create(
            DateTimeOffset generatedAtUtc,
            ObjectArchiveReadiness archive,
            FeedStatusSnapshot feeds) => new()
            {
                GeneratedAtUtc = generatedAtUtc,
                Archive = archive,
                Feeds = feeds
            };
    }
}
