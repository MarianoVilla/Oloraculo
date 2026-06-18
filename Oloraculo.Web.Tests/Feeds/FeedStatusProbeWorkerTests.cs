using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oloraculo.Web.Feeds;

namespace Oloraculo.Web.Tests.Feeds;

public sealed class FeedStatusProbeWorkerTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-06-18T13:00:00Z");

    [Fact]
    public async Task ProbeOnceAsync_DisabledOptionPerformsNoProbes()
    {
        var store = new InMemoryFeedStatusHealthStore();
        var adapter = new CountingAdapter("polymarket_clob", _ => ReadyReport("polymarket_clob"));
        var worker = Worker([adapter], store, new FeedStatusOptions { EnableBackgroundProbes = false });

        await worker.ProbeOnceAsync(CancellationToken.None);

        Assert.Equal(0, adapter.ProbeCount);
        Assert.False(store.TryGet("polymarket_clob", out _));
    }

    [Fact]
    public async Task ProbeOnceAsync_EnabledOptionWritesReadyReportToStore()
    {
        var store = new InMemoryFeedStatusHealthStore();
        var adapter = new CountingAdapter("polymarket_clob", context =>
        {
            Assert.True(context.AllowNetwork);
            return ReadyReport("polymarket_clob");
        });
        var worker = Worker([adapter], store, new FeedStatusOptions { EnableBackgroundProbes = true });

        await worker.ProbeOnceAsync(CancellationToken.None);

        Assert.True(store.TryGet("polymarket_clob", out var report));
        Assert.Equal(FeedAdapterState.Ready, report.State);
        Assert.Equal(1, adapter.ProbeCount);
    }

    [Fact]
    public async Task ProbeOnceAsync_ThrowingAdapterBecomesDownWithRedactedError()
    {
        var store = new InMemoryFeedStatusHealthStore();
        var logger = new CapturingLogger<FeedStatusProbeWorker>();
        var adapter = new CountingAdapter(
            "polymarket_clob",
            _ => throw new InvalidOperationException("failed with Authorization: Bearer leaked-token"));
        var worker = Worker([adapter], store, new FeedStatusOptions { EnableBackgroundProbes = true }, logger);

        await worker.ProbeOnceAsync(CancellationToken.None);

        Assert.True(store.TryGet("polymarket_clob", out var report));
        Assert.Equal(FeedAdapterState.Down, report.State);
        Assert.Equal(["SOURCE_DOWN"], report.Blockers);
        Assert.Contains("<redacted", report.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("leaked-token", report.LastError, StringComparison.Ordinal);
        Assert.Contains(logger.Messages, message => message.Contains("<redacted", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("leaked-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProbeOnceAsync_TimeoutBecomesDownWithProbeTimeoutBlocker()
    {
        var store = new InMemoryFeedStatusHealthStore();
        var adapter = new CountingAdapter("polymarket_clob", _ =>
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(200));
            return ReadyReport("polymarket_clob");
        });
        var worker = Worker(
            [adapter],
            store,
            new FeedStatusOptions
            {
                EnableBackgroundProbes = true,
                ProbeTimeoutMilliseconds = 10
            });

        await worker.ProbeOnceAsync(CancellationToken.None);

        Assert.True(store.TryGet("polymarket_clob", out var report));
        Assert.Equal(FeedAdapterState.Down, report.State);
        Assert.NotNull(report.Blockers);
        Assert.Contains(report.Blockers!, blocker => blocker is "PROBE_TIMEOUT" or "SOURCE_DOWN");
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfiguredProbeIntervalBetweenProbeCycles()
    {
        var store = new InMemoryFeedStatusHealthStore();
        var adapter = new CountingAdapter("polymarket_clob", _ => ReadyReport("polymarket_clob"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observedDelay = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = Worker(
            [adapter],
            store,
            new FeedStatusOptions
            {
                EnableBackgroundProbes = true,
                ProbeIntervalSeconds = 3
            },
            delayAsync: (delay, _) =>
            {
                observedDelay.TrySetResult(delay);
                cts.Cancel();
                return Task.CompletedTask;
            });

        await worker.StartAsync(cts.Token);
        var delay = await observedDelay.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(3), delay);
        Assert.True(adapter.ProbeCount >= 1);
    }

    private static FeedStatusProbeWorker Worker(
        IEnumerable<IFeedStatusAdapter> adapters,
        IFeedStatusHealthStore store,
        FeedStatusOptions options,
        ILogger<FeedStatusProbeWorker>? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        return new FeedStatusProbeWorker(
            adapters,
            store,
            Options.Create(options),
            logger ?? NullLogger<FeedStatusProbeWorker>.Instance,
            () => FixedNow,
            delayAsync);
    }

    private static FeedAdapterReport ReadyReport(string sourceId) => new(
        SourceId: sourceId,
        Source: "Fake source",
        Role: "test feed",
        State: FeedAdapterState.Ready,
        ConfigPresent: true,
        LatestRecvTsUtc: FixedNow,
        Detail: "ready");

    private sealed class CountingAdapter : IFeedStatusAdapter
    {
        private readonly Func<FeedStatusProbeContext, FeedAdapterReport> _probe;

        public CountingAdapter(string sourceId, Func<FeedStatusProbeContext, FeedAdapterReport> probe)
        {
            SourceId = sourceId;
            _probe = probe;
        }

        public string SourceId { get; }

        public int ProbeCount { get; private set; }

        public FeedAdapterReport Probe(FeedStatusProbeContext context)
        {
            ProbeCount++;
            return _probe(context);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
                Messages.Add(exception.ToString());
        }
    }
}
