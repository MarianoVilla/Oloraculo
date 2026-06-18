namespace Oloraculo.Web.Feeds
{
    public interface IFeedStatusAdapter
    {
        string SourceId { get; }

        FeedAdapterReport Probe(FeedStatusProbeContext context);
    }
}
