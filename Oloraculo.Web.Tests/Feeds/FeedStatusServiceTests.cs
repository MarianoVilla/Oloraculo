using System.Text.Json;
using Microsoft.Extensions.Options;
using Oloraculo.Web.Archive;
using Oloraculo.Web.Feeds;

namespace Oloraculo.Web.Tests.Feeds;

public sealed class FeedStatusServiceTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-18T03:04:05Z");

    [Fact]
    public void Snapshot_UsesCanonicalSanitizedSchema()
    {
        var present = new HashSet<string>(StringComparer.Ordinal)
        {
            "SPORTSBOOK_XAUTH",
            "DATABET_WIDGET_TOKEN",
            "ODDSPAPI_KEY",
            "GRID_KEY",
            "R2_BUCKET",
            "R2_ENDPOINT",
            "R2_REGION",
            "R2_ACCESS",
            "R2_SECRET"
        };
        var service = new FeedStatusService(
            new TestSecretPresenceReader(present),
            Options.Create(Config(enabled: true)),
            () => FixedNow);

        var snapshot = service.Snapshot();
        var databet = snapshot.Rows.Single(row => row.SourceId == "databet_sportsbook");
        var archive = snapshot.Rows.Single(row => row.SourceId == "object_archive");

        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal(FixedNow, snapshot.AsOfUtc);
        Assert.Equal(FixedNow, snapshot.GeneratedAtUtc);
        Assert.DoesNotContain(snapshot.Rows, row => row.Source.Contains("Sofa", StringComparison.OrdinalIgnoreCase));

        Assert.False(databet.Present);
        Assert.True(databet.ConfigPresent);
        Assert.True(databet.AuthPresent);
        Assert.Equal(FeedReadiness.Planned, databet.Readiness);
        Assert.Equal("PLANNED", databet.State);
        Assert.Equal(["COLLECTOR_NOT_ENABLED"], databet.Blockers);
        Assert.Equal("COLLECTOR_NOT_ENABLED", databet.Blocker);

        Assert.False(archive.Present);
        Assert.Equal(FeedReadiness.Planned, archive.Readiness);
        Assert.Equal(["ARCHIVER_HEALTH_UNVERIFIED"], archive.Blockers);

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"schema_version\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"generated_at_utc\":\"2026-06-18T03:04:05+00:00\"", json, StringComparison.Ordinal);
        Assert.Contains("\"source_id\":\"databet_sportsbook\"", json, StringComparison.Ordinal);
        Assert.Contains("\"present\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"blockers\":[\"COLLECTOR_NOT_ENABLED\"]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("R2_SECRET", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key-value", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FeedSnapshot_MatchesSharedGoldenContract()
    {
        var snapshot = new FeedStatusSnapshot
        {
            AsOfUtc = FixedNow,
            Rows =
            [
                new FeedStatusRow
                {
                    SourceId = "databet_sportsbook",
                    Source = "Databet sportsbook",
                    Role = "external live odds/state",
                    Readiness = FeedReadiness.Planned,
                    Present = false,
                    AuthPresent = true,
                    ConfigPresent = true,
                    Blocker = "COLLECTOR_NOT_ENABLED",
                    Blockers = ["COLLECTOR_NOT_ENABLED"],
                    Detail = "collector configured but not running",
                    SecretPolicy = "PRESENCE_ONLY_NO_VALUES"
                }
            ]
        };

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var fixture = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "docs",
            "source-of-truth",
            "fixtures",
            "feed_status_snapshot_v1.json")).TrimEnd('\r', '\n');

        Assert.Equal(fixture, json);
    }

    [Fact]
    public void RuntimeSnapshotEnvelope_UsesCanonicalTopLevelSchema()
    {
        var feeds = new FeedStatusSnapshot
        {
            AsOfUtc = FixedNow,
            Rows =
            [
                new FeedStatusRow
                {
                    SourceId = "object_archive",
                    Source = "R2 object archive",
                    Role = "raw object storage",
                    Readiness = FeedReadiness.Planned,
                    Present = false,
                    ConfigPresent = true,
                    AuthPresent = true,
                    Blocker = "ARCHIVER_HEALTH_UNVERIFIED",
                    Blockers = ["ARCHIVER_HEALTH_UNVERIFIED"],
                    SecretPolicy = "PRESENCE_ONLY_NO_VALUES"
                }
            ]
        };
        var archive = new ObjectArchiveReadiness(true, true, "R2", "bucket configured; endpoint host example.r2.test");

        var envelope = RuntimeStatusSnapshot.Create(FixedNow, archive, feeds);

        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal(FixedNow, envelope.GeneratedAtUtc);
        Assert.Equal("READ_ONLY_STATUS_ONLY", envelope.Mode);
        Assert.Same(feeds, envelope.Feeds);

        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"schema_version\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"generated_at_utc\":\"2026-06-18T03:04:05+00:00\"", json, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"READ_ONLY_STATUS_ONLY\"", json, StringComparison.Ordinal);
        Assert.Contains("\"feeds\":{\"schema_version\":1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"generatedAtUtc\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"schemaVersion\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_RemovesBearerHeadersAndQueryStringKeys()
    {
        var redacted = FeedStatusService.RedactError(
            "Authorization: Bearer opaque-token request failed https://feed.test/live?api_key=leaky&xauth=also-leaky");

        Assert.DoesNotContain("opaque-token", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("leaky", redacted, StringComparison.Ordinal);
        Assert.Contains("<redacted", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redaction_RemovesCommonSecretShapesAndPrivateKeyMarkers()
    {
        var github = "ghp_" + "0123456789abcdefghijk";
        var aws = "AKIA" + "1234567890ABCDEF";
        var google = "AIza" + "Sy0123456789abcdefghijklmnopqrstuvw";
        var slack = "xoxb-" + "123456789012-abcdefghijklmnop";
        var privateKeyMarker = "-----BEGIN " + "PRIVATE KEY-----";

        var redacted = FeedStatusService.RedactError($"{github} {aws} {google} {slack} {privateKeyMarker}");

        Assert.DoesNotContain(github, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(aws, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(google, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(slack, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", redacted, StringComparison.Ordinal);
        Assert.Contains("<redacted", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdapterRows_DoNotReportReadyWithoutMeasuredTimestamp()
    {
        var row = FeedStatusRow.FromAdapter(new FeedAdapterReport(
            SourceId: "fake_source",
            Source: "Fake source",
            Role: "test feed",
            State: FeedAdapterState.Ready,
            ConfigPresent: true,
            AuthPresent: true,
            RowsLastMinute: 12,
            Detail: "adapter forgot freshness timestamp"), FixedNow);

        Assert.Equal(FeedReadiness.Blocked, row.Readiness);
        Assert.False(row.Present);
        Assert.Equal(["MEASURED_DATA_MISSING"], row.Blockers);
        Assert.Equal("MEASURED_DATA_MISSING", row.Blocker);
    }

    [Fact]
    public void AdapterRows_RedactDetailsAtFinalBoundary()
    {
        var github = "ghp_" + "0123456789abcdefghijk";
        var row = FeedStatusRow.FromAdapter(new FeedAdapterReport(
            SourceId: "fake_source",
            Source: "Fake source",
            Role: "test feed",
            State: FeedAdapterState.Down,
            ConfigPresent: true,
            Detail: $"upstream echoed token=should-not-leak and {github}"), FixedNow);

        Assert.DoesNotContain("should-not-leak", row.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(github, row.Detail, StringComparison.Ordinal);
        Assert.Contains("<redacted", row.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(FeedAdapterState.MissingConfig, FeedReadiness.ConfigMissing, false, "AUTH_CONFIG_MISSING")]
    [InlineData(FeedAdapterState.Down, FeedReadiness.Blocked, false, "SOURCE_DOWN")]
    [InlineData(FeedAdapterState.Stale, FeedReadiness.Blocked, true, "STALE_SOURCE")]
    [InlineData(FeedAdapterState.Empty, FeedReadiness.Blocked, true, "EMPTY_SOURCE")]
    [InlineData(FeedAdapterState.ParseError, FeedReadiness.Blocked, true, "PARSE_ERROR")]
    [InlineData(FeedAdapterState.Ready, FeedReadiness.Ready, true, "")]
    public void AdapterRows_MapDeterministicSourceStates(
        FeedAdapterState state,
        FeedReadiness expectedReadiness,
        bool expectedPresent,
        string expectedBlocker)
    {
        var report = new FeedAdapterReport(
            SourceId: "fake_source",
            Source: "Fake source",
            Role: "test feed",
            State: state,
            ConfigPresent: state != FeedAdapterState.MissingConfig,
            AuthPresent: state == FeedAdapterState.MissingConfig ? false : null,
            LatestRecvTsUtc: state switch
            {
                FeedAdapterState.Ready => FixedNow.AddSeconds(-2),
                FeedAdapterState.Stale or FeedAdapterState.Empty or FeedAdapterState.ParseError => FixedNow.AddSeconds(-20),
                _ => null
            },
            RowsLastMinute: state switch
            {
                FeedAdapterState.Empty => 0,
                FeedAdapterState.Ready => 12,
                _ => null
            },
            JoinCoverage: state == FeedAdapterState.Ready ? 0.75 : null,
            LastError: state == FeedAdapterState.ParseError
                ? "parse failed token=should-not-leak private_key=0x" + new string('1', 64)
                : null,
            Detail: "deterministic adapter report");

        var row = FeedStatusRow.FromAdapter(report, FixedNow, staleAfter: TimeSpan.FromSeconds(10));

        Assert.Equal("fake_source", row.SourceId);
        Assert.Equal(expectedReadiness, row.Readiness);
        Assert.Equal(expectedReadiness.ToContractState(), row.State);
        Assert.Equal(expectedPresent, row.Present);
        Assert.Equal(state != FeedAdapterState.MissingConfig, row.ConfigPresent);

        if (string.IsNullOrEmpty(expectedBlocker))
            Assert.Empty(row.Blockers);
        else
            Assert.Equal([expectedBlocker], row.Blockers);

        if (state == FeedAdapterState.Stale)
            Assert.Equal(20_000, row.AgeMs);

        if (state == FeedAdapterState.ParseError)
        {
            Assert.Contains("<redacted", row.LastErrorRedacted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("should-not-leak", row.LastErrorRedacted, StringComparison.Ordinal);
            Assert.DoesNotContain("111111111111111111", row.LastErrorRedacted, StringComparison.Ordinal);
        }
    }

    private static OloraculoConfig Config(bool enabled) => new()
    {
        ObjectArchive = new ObjectArchiveConfig
        {
            Enabled = enabled,
            Provider = "R2",
            BucketEnvironmentVariable = "R2_BUCKET",
            EndpointEnvironmentVariable = "R2_ENDPOINT",
            RegionEnvironmentVariable = "R2_REGION",
            AccessKeyIdEnvironmentVariable = "R2_ACCESS",
            SecretAccessKeyEnvironmentVariable = "R2_SECRET",
            Prefix = "test-prefix"
        }
    };

    private sealed class TestSecretPresenceReader : ISecretPresenceReader
    {
        private readonly IReadOnlySet<string> _present;

        public TestSecretPresenceReader(IReadOnlySet<string> present) => _present = present;

        public bool IsPresent(string name) => _present.Contains(name);
    }
}
