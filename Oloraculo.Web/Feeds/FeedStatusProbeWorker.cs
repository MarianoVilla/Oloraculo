using Microsoft.Extensions.Options;

namespace Oloraculo.Web.Feeds
{
    public sealed class FeedStatusProbeWorker : BackgroundService
    {
        private readonly IReadOnlyList<IFeedStatusAdapter> _adapters;
        private readonly IFeedStatusHealthStore _healthStore;
        private readonly FeedStatusOptions _options;
        private readonly ILogger<FeedStatusProbeWorker> _logger;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

        public FeedStatusProbeWorker(
            IEnumerable<IFeedStatusAdapter> adapters,
            IFeedStatusHealthStore healthStore,
            IOptions<FeedStatusOptions> options,
            ILogger<FeedStatusProbeWorker> logger)
            : this(adapters, healthStore, options, logger, () => DateTimeOffset.UtcNow)
        {
        }

        internal FeedStatusProbeWorker(
            IEnumerable<IFeedStatusAdapter> adapters,
            IFeedStatusHealthStore healthStore,
            IOptions<FeedStatusOptions> options,
            ILogger<FeedStatusProbeWorker> logger,
            Func<DateTimeOffset> clock,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        {
            _adapters = adapters.ToArray();
            _healthStore = healthStore;
            _options = options.Value;
            _logger = logger;
            _clock = clock;
            _delayAsync = delayAsync ?? Task.Delay;
        }

        public async Task ProbeOnceAsync(CancellationToken cancellationToken)
        {
            if (!_options.EnableBackgroundProbes)
                return;

            foreach (var adapter in _adapters)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                await ProbeAdapterAsync(adapter, cancellationToken);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await ProbeOnceAsync(stoppingToken);

                await _delayAsync(ProbeInterval(), stoppingToken);
            }
        }

        private async Task ProbeAdapterAsync(IFeedStatusAdapter adapter, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, _options.ProbeTimeoutMilliseconds)));

            var asOf = _clock();
            var context = new FeedStatusProbeContext(
                asOf,
                StaleAfterFor(adapter.SourceId),
                AllowNetwork: true);

            var probeTask = Task.Run(() => adapter.Probe(context), timeoutCts.Token);
            var completed = await Task.WhenAny(probeTask, Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token));

            if (completed != probeTask)
            {
                _healthStore.Upsert(DownReport(adapter.SourceId, asOf, "probe timed out", "PROBE_TIMEOUT"));
                return;
            }

            try
            {
                _healthStore.Upsert(await probeTask);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var redacted = FeedStatusRedactor.RedactError(ex.Message);
                _logger.LogWarning("Feed status probe failed for {SourceId}: {Error}", adapter.SourceId, redacted);
                _healthStore.Upsert(DownReport(adapter.SourceId, asOf, redacted, "SOURCE_DOWN"));
            }
        }

        private TimeSpan ProbeInterval() => TimeSpan.FromSeconds(Math.Max(1, _options.ProbeIntervalSeconds));

        private TimeSpan StaleAfterFor(string sourceId)
        {
            if (_options.StaleAfterSecondsBySource.TryGetValue(sourceId, out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);

            return TimeSpan.FromSeconds(Math.Max(1, _options.DefaultStaleAfterSeconds));
        }

        private static FeedAdapterReport DownReport(string sourceId, DateTimeOffset asOf, string error, string blocker)
        {
            var definition = FeedStatusSourceCatalog.All.FirstOrDefault(source =>
                string.Equals(source.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));

            return new FeedAdapterReport(
                SourceId: sourceId,
                Source: definition?.Source ?? sourceId,
                Role: definition?.Role ?? "feed status probe",
                State: FeedAdapterState.Down,
                ConfigPresent: true,
                LastError: FeedStatusRedactor.RedactError(error),
                Detail: $"probe failed at {asOf:O}",
                SecretPolicy: definition?.SecretPolicy ?? "NO_SECRET_DISPLAY",
                Blockers: [blocker]);
        }
    }
}
