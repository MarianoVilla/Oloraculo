using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.Archive;
using Oloraculo.Web.Feeds;
using Oloraculo.Web.Feeds.Adapters;

namespace Oloraculo.Web.Tests.Archive;

public sealed class ObjectArchiveHealthProbeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-18T19:00:00Z");

    [Fact]
    public void DefaultProbe_ReturnsUnverifiedStatusNotReady()
    {
        var probe = new DefaultObjectArchiveHealthProbe(Options.Create(Config(enabled: true)), EnvComplete());

        var snapshot = probe.Probe(Now);

        Assert.True(snapshot.Configured);
        Assert.True(snapshot.Enabled);
        Assert.Null(snapshot.LastVerifiedManifestUtc);
        Assert.Equal(0, snapshot.PendingLocalBatchCount);
    }

    [Fact]
    public void Adapter_IncompleteConfigReportsMissingConfig()
    {
        var report = Adapter(FakeHealth()).Probe(Context());

        Assert.Equal(FeedAdapterState.MissingConfig, report.State);
        Assert.Equal(["OBJECT_ARCHIVE_CONFIG_INCOMPLETE"], report.Blockers);
    }

    [Fact]
    public void Adapter_DisabledArchiveReportsPlannedDisabled()
    {
        var report = Adapter(FakeHealth(enabled: false, configured: true), config: Config(enabled: false), env: EnvComplete())
            .Probe(Context());

        Assert.Equal(FeedAdapterState.Planned, report.State);
        Assert.Equal(["ARCHIVER_DISABLED"], report.Blockers);
    }

    [Fact]
    public void Adapter_ConfiguredButUnverifiedReportsPlanned()
    {
        var report = Adapter(FakeHealth(), env: EnvComplete()).Probe(Context());

        Assert.Equal(FeedAdapterState.Planned, report.State);
        Assert.Equal(["ARCHIVER_HEALTH_UNVERIFIED"], report.Blockers);
    }

    [Fact]
    public void Adapter_NoNetworkContextDoesNotCallHealthProbe()
    {
        var report = Adapter(new ThrowingObjectArchiveHealthProbe(), env: EnvComplete())
            .Probe(Context(allowNetwork: false));

        Assert.Equal(FeedAdapterState.Planned, report.State);
        Assert.Equal(["ARCHIVER_HEALTH_UNVERIFIED"], report.Blockers);
    }

    [Fact]
    public void Adapter_ListDeniedReportsDown()
    {
        var report = Adapter(FakeHealth(lastError: "AccessDenied secret=should-not-leak"), env: EnvComplete())
            .Probe(Context());

        Assert.Equal(FeedAdapterState.Down, report.State);
        Assert.Equal(["ARCHIVE_LIST_DENIED"], report.Blockers);
        Assert.Contains("<redacted", report.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("should-not-leak", report.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public void Adapter_BacklogAboveThresholdReportsBlocked()
    {
        var report = Adapter(FakeHealth(lastManifest: Now.AddSeconds(-5), pending: 2), env: EnvComplete())
            .Probe(Context());

        Assert.Equal(FeedAdapterState.Blocked, report.State);
        Assert.Equal(["ARCHIVE_BACKLOG"], report.Blockers);
    }

    [Fact]
    public void Adapter_StaleManifestReportsStale()
    {
        var report = Adapter(FakeHealth(lastManifest: Now.AddMinutes(-5)), env: EnvComplete())
            .Probe(Context());

        Assert.Equal(FeedAdapterState.Stale, report.State);
        Assert.Equal(["ARCHIVE_STALE"], report.Blockers);
    }

    [Fact]
    public void Adapter_RecentManifestAndNoBacklogReportsReady()
    {
        var report = Adapter(FakeHealth(lastManifest: Now.AddSeconds(-5)), env: EnvComplete())
            .Probe(Context());

        Assert.Equal(FeedAdapterState.Ready, report.State);
        Assert.Equal(Now.AddSeconds(-5), report.LatestRecvTsUtc);
        Assert.Null(report.Blockers);
    }

    private static ObjectArchiveFeedStatusAdapter Adapter(
        IObjectArchiveHealthProbe probe,
        OloraculoConfig? config = null,
        Func<string, string?>? env = null) =>
        new(Options.Create(config ?? Config(enabled: true)), probe, env ?? (_ => null));

    private static FeedStatusProbeContext Context(bool allowNetwork = true) => new(Now, TimeSpan.FromSeconds(30), AllowNetwork: allowNetwork);

    private static IObjectArchiveHealthProbe FakeHealth(
        DateTimeOffset? lastManifest = null,
        int pending = 0,
        string? lastError = null,
        bool enabled = true,
        bool configured = true) =>
        new FakeObjectArchiveHealthProbe(new ObjectArchiveHealthSnapshot(
            Configured: configured,
            Enabled: enabled,
            LastVerifiedManifestUtc: lastManifest,
            PendingLocalBatchCount: pending,
            LastError: lastError,
            Provider: "R2"));

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
            MaxPendingLocalBatchCount = 0
        }
    };

    private static Func<string, string?> EnvComplete() =>
        name => name switch
        {
            "TEST_BUCKET" => "bucket",
            "TEST_ENDPOINT" => "https://example.r2.test",
            "TEST_REGION" => "auto",
            "TEST_ACCESS" => "access",
            "TEST_SECRET" => "secret",
            _ => null
        };

    private sealed class FakeObjectArchiveHealthProbe : IObjectArchiveHealthProbe
    {
        private readonly ObjectArchiveHealthSnapshot _snapshot;

        public FakeObjectArchiveHealthProbe(ObjectArchiveHealthSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public ObjectArchiveHealthSnapshot Probe(DateTimeOffset asOfUtc) => _snapshot;
    }

    private sealed class ThrowingObjectArchiveHealthProbe : IObjectArchiveHealthProbe
    {
        public ObjectArchiveHealthSnapshot Probe(DateTimeOffset asOfUtc) =>
            throw new InvalidOperationException("object archive health probe should not run inline");
    }
}
