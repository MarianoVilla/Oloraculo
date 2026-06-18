using System.Collections.Concurrent;

namespace Oloraculo.Web.Feeds
{
    public interface IFeedStatusHealthStore
    {
        bool TryGet(string sourceId, out FeedAdapterReport report);

        void Upsert(FeedAdapterReport report);
    }

    public sealed class InMemoryFeedStatusHealthStore : IFeedStatusHealthStore
    {
        private readonly ConcurrentDictionary<string, FeedAdapterReport> _reports = new(StringComparer.OrdinalIgnoreCase);

        public bool TryGet(string sourceId, out FeedAdapterReport report) => _reports.TryGetValue(sourceId, out report!);

        public void Upsert(FeedAdapterReport report)
        {
            if (string.IsNullOrWhiteSpace(report.SourceId))
                return;

            _reports[report.SourceId] = report;
        }
    }
}
