using Oloraculo.Web.Feeds;

namespace Oloraculo.Web.Tests.Feeds;

public sealed class FeedStatusSourceCatalogTests
{
    [Fact]
    public void All_ReturnsSixActiveSourcesInContractOrder()
    {
        var ids = FeedStatusSourceCatalog.All.Select(source => source.SourceId).ToArray();

        Assert.Equal(
            [
                "databet_sportsbook",
                "databet_widgets",
                "oddspapi_pinnacle",
                "grid",
                "polymarket_clob",
                "object_archive"
            ],
            ids);
    }

    [Fact]
    public void All_DoesNotContainSofaSources()
    {
        Assert.DoesNotContain(
            FeedStatusSourceCatalog.All,
            source =>
                source.SourceId.Contains("sofa", StringComparison.OrdinalIgnoreCase) ||
                source.Source.Contains("Sofa", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void All_RolesMatchFeedStatusContractMeanings()
    {
        var roles = FeedStatusSourceCatalog.All.ToDictionary(
            source => source.SourceId,
            source => source.Role,
            StringComparer.Ordinal);

        Assert.Equal("external live odds/state", roles["databet_sportsbook"]);
        Assert.Equal("widget game-state/status", roles["databet_widgets"]);
        Assert.Equal("sharp consensus odds", roles["oddspapi_pinnacle"]);
        Assert.Equal("esports telemetry", roles["grid"]);
        Assert.Equal("executable book/trade hotpath", roles["polymarket_clob"]);
        Assert.Equal("raw/bronze/silver/gold object storage", roles["object_archive"]);
    }
}
