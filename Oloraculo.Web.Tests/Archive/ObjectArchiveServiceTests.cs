using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.Archive;
using Oloraculo.Web.Feeds;

namespace Oloraculo.Web.Tests.Archive;

public class ObjectArchiveServiceTests
{
    [Fact]
    public void Resolve_UsesConfiguredEnvironmentVariableNamesWithoutExposingSecrets()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["TEST_BUCKET"] = "archive-bucket",
            ["TEST_ENDPOINT"] = "https://example.r2.cloudflarestorage.com",
            ["TEST_REGION"] = "auto",
            ["TEST_ACCESS"] = "access-key-value",
            ["TEST_SECRET"] = "secret-key-value"
        };
        var config = Config(enabled: true);

        var resolved = ObjectArchiveConfigResolver.Resolve(config, name => env.TryGetValue(name, out var value) ? value : null);
        var readiness = resolved.ToReadiness();

        Assert.True(resolved.IsConfigured);
        Assert.True(readiness.Enabled);
        Assert.True(readiness.Configured);
        Assert.Equal("R2", readiness.Provider);
        Assert.Contains("endpoint host example.r2.cloudflarestorage.com", readiness.Detail);
        Assert.DoesNotContain("access-key-value", readiness.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key-value", readiness.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisabledService_SkipsUploadWhenArchiveIsDisabled()
    {
        var service = new DisabledObjectArchiveService(Options.Create(Config(enabled: false)));

        var result = await service.UploadAsync(new ObjectArchivePayload(
            "test-stream",
            "payload.json",
            "{}"u8.ToArray(),
            "application/json"));

        Assert.Equal(ObjectArchiveUploadStatus.SkippedDisabled, result.Status);
        Assert.Contains("disabled", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FeedStatus_UsesArchiveConfigNamesAndPresenceOnlyPolicy()
    {
        var present = new HashSet<string>(StringComparer.Ordinal)
        {
            "TEST_BUCKET",
            "TEST_ENDPOINT",
            "TEST_REGION",
            "TEST_ACCESS",
            "TEST_SECRET"
        };
        var service = new FeedStatusService(
            new TestSecretPresenceReader(present),
            Options.Create(Config(enabled: true)),
            () => DateTimeOffset.Parse("2026-06-17T18:00:00Z"));

        var row = service.Snapshot().Rows.Single(item => item.Source.Contains("object archive", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(FeedReadiness.Planned, row.Readiness);
        Assert.True(row.AuthPresent);
        Assert.True(row.ConfigPresent);
        Assert.False(row.Present);
        Assert.Equal("ARCHIVER_HEALTH_UNVERIFIED", row.Blocker);
        Assert.Equal("PRESENCE_ONLY_NO_VALUES", row.SecretPolicy);
        Assert.DoesNotContain("TEST_SECRET", row.Detail, StringComparison.Ordinal);
    }

    private static OloraculoConfig Config(bool enabled) => new()
    {
        ObjectArchive = new ObjectArchiveConfig
        {
            Enabled = enabled,
            Provider = "R2",
            BucketEnvironmentVariable = "TEST_BUCKET",
            EndpointEnvironmentVariable = "TEST_ENDPOINT",
            RegionEnvironmentVariable = "TEST_REGION",
            AccessKeyIdEnvironmentVariable = "TEST_ACCESS",
            SecretAccessKeyEnvironmentVariable = "TEST_SECRET",
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
