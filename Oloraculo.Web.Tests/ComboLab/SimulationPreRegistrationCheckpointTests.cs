using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.ComboLab.Monitor;
using Oloraculo.Web.Models;
using Oloraculo.Web.WorldCup.Burden;

namespace Oloraculo.Web.Tests.ComboLab;

public class SimulationPreRegistrationCheckpointTests
{
    [Fact]
    public void Create_ProducesDeterministicHashForSameInputs()
    {
        var projection = Projection();
        var universe = new PolymarketFootballUniverseSummary(
            TotalMarkets: 100,
            ComboEligibleMarkets: 40,
            SourceRejectedMarkets: 2,
            FamilyCounts: new Dictionary<PolymarketFootballMarketFamily, int> { [PolymarketFootballMarketFamily.Moneyline] = 10 },
            CoverageCounts: new Dictionary<PolymarketFootballModelCoverage, int> { [PolymarketFootballModelCoverage.ScorelineGrid] = 10 });
        var burden = new WorldCupBurdenCoverageSnapshot(72, 72, 72, 72, 72, new Dictionary<string, int>());
        var stamp = new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);

        var first = SimulationPreRegistrationCheckpoint.Create(projection, universe, burden, ["b", "a", "a"], stamp);
        var second = SimulationPreRegistrationCheckpoint.Create(projection, universe, burden, ["a", "b"], stamp);

        Assert.Equal(first.CheckpointHash, second.CheckpointHash);
        Assert.Equal(SimulationPreRegistrationCheckpoint.VerdictHold, first.Verdict);
        Assert.Equal(["a", "b"], first.SourceManifest);
        Assert.Contains("sharp_anchor_when_available", first.RequiredValidationGates);
        Assert.Equal(72, first.BurdenReadyFixtures);
    }

    [Fact]
    public void Create_HashChangesWhenProjectionInputHashChanges()
    {
        var stamp = new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);
        var first = SimulationPreRegistrationCheckpoint.Create(Projection("hash-a"), null, null, ["source"], stamp);
        var second = SimulationPreRegistrationCheckpoint.Create(Projection("hash-b"), null, null, ["source"], stamp);

        Assert.NotEqual(first.CheckpointHash, second.CheckpointHash);
    }

    private static TournamentProjection Projection(string hash = "input-hash") => new()
    {
        ModelName = "Final",
        Simulations = 1000,
        InputSummaryHash = hash,
        Teams = []
    };
}
