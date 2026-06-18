using Oloraculo.Web.ComboLab.Monitor;
using Oloraculo.Web.Helpers;

namespace Oloraculo.Web.Tests.ComboLab;

public class ComboLabEvidenceLedgerServiceTests : TestFixtures
{
    [Fact]
    public async Task SaveAsync_AppendsLiveAndBatchCheckpointsAndDeduplicatesHashes()
    {
        var service = Service();
        var live = LiveComplementaryLockCheckpoint.Create(LiveSnapshot());
        var batch = LiveComplementaryBatchScanCheckpoint.Create(BatchSnapshot());

        var firstLive = await service.SaveAsync(live);
        var duplicateLive = await service.SaveAsync(live);
        var batchEntry = await service.SaveAsync(batch);
        var ledger = await service.LoadAsync();

        Assert.Equal(firstLive.EntryId, duplicateLive.EntryId);
        Assert.Equal(2, ledger.Entries.Count);
        Assert.Equal(1, ledger.LiveEntries);
        Assert.Equal(1, ledger.BatchEntries);
        Assert.Empty(ledger.Rejects);
        Assert.Contains(ledger.Entries, entry => entry.CheckpointKind == ComboLabEvidenceLedgerService.LiveCheckpointKind && entry.Summary.Contains("France vs Senegal", StringComparison.Ordinal));
        Assert.Contains(ledger.Entries, entry => entry.CheckpointKind == ComboLabEvidenceLedgerService.BatchCheckpointKind && entry.Summary.Contains("fixtures 1/2", StringComparison.Ordinal));
        Assert.Equal(CryptoUtil.GetSha256(live.PayloadJson), firstLive.PayloadHash);
        Assert.Equal(CryptoUtil.GetSha256(batch.PayloadJson), batchEntry.PayloadHash);
        Assert.DoesNotContain(ledger.Entries, entry => entry.PayloadJson.Contains("private", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(ledger.Entries, entry => entry.PayloadJson.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoadAsync_RejectsMalformedOrTamperedRowsWithoutThrowing()
    {
        var service = Service();
        Directory.CreateDirectory(Path.GetDirectoryName(service.LedgerPath)!);
        await File.WriteAllTextAsync(service.LedgerPath, "not json\n" + TamperedEntryJson());

        var ledger = await service.LoadAsync();

        Assert.Empty(ledger.Entries);
        Assert.Equal(2, ledger.Rejects.Count);
        Assert.Contains(ledger.Rejects, reject => reject.Contains("line 1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ledger.Rejects, reject => reject.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase));
    }

    private static ComboLabEvidenceLedgerService Service()
    {
        var root = NewTempRoot();
        return new ComboLabEvidenceLedgerService(new TestEnvironment(root));
    }

    private static LiveComplementaryLockSnapshot LiveSnapshot() => new(
        DateTimeOffset.Parse("2026-06-16T18:00:00Z"),
        "fixture-france-senegal",
        "France vs Senegal",
        "france-senegal",
        "event-1",
        "France vs Senegal",
        new LiveComplementaryEventState(
            "PRE_GAME",
            "PRE_GAME_ONLY",
            AllowsCandidatePricing: true,
            IsInPlay: false,
            RawScore: null,
            HomeGoals: null,
            AwayGoals: null,
            RawElapsed: null,
            ElapsedMinute: null,
            StartTimeUtc: DateTimeOffset.Parse("2026-06-16T19:00:00Z"),
            "pre-game only; no live score-state applied"),
        new LiveComplementaryGoalDecay(
            "PRE_GAME_NO_DECAY",
            Applied: false,
            Minute: null,
            RemainingFraction: 1.0,
            KickoffHomeExpectedGoals: null,
            KickoffAwayExpectedGoals: null,
            RemainingHomeExpectedGoals: null,
            RemainingAwayExpectedGoals: null,
            RetainedGridMass: 1.0,
            "pre-game distribution retained; no elapsed-minute decay applied"),
        new LiveComplementaryScoreConditioning(
            "PRE_GAME_UNCONDITIONED",
            Applied: false,
            CurrentHomeGoals: null,
            CurrentAwayGoals: null,
            OriginalStateCount: 36,
            RetainedStateCount: 36,
            RemovedImpossibleStateCount: 0,
            RetainedProbabilityMass: 1.0,
            "full pre-game score grid retained"),
        "test scoreline",
        EventMarkets: 2,
        MappedContracts: 4,
        BooksFetched: 4,
        InputHashes:
        [
            new LiveComplementaryInputHash("gamma-event-detail", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", DateTimeOffset.Parse("2026-06-16T18:00:00Z"), "france-senegal", "event-1"),
            new LiveComplementaryInputHash("clob-book", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", DateTimeOffset.Parse("2026-06-16T18:00:01Z"), "france-senegal", "event-1", "condition-1", "token-1")
        ],
        Candidates: [],
        GeneralizedCandidates: [],
        Rejects: []);

    private static LiveComplementaryBatchScanSnapshot BatchSnapshot() => new(
        DateTimeOffset.Parse("2026-06-16T18:05:00Z"),
        "WATCH_ONLY_PUBLIC_READS",
        RequestedMaxFixtures: 48,
        FixturesSeen: 2,
        FixturesScanned: 1,
        EventMatches: 1,
        EventlessFixtures: 1,
        AmbiguousEvents: 0,
        TotalEventMarkets: 2,
        TotalMappedContracts: 4,
        TotalBooksFetched: 4,
        InputHashes:
        [
            new LiveComplementaryInputHash("gamma-events-list", "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", DateTimeOffset.Parse("2026-06-16T18:04:59Z")),
            new LiveComplementaryInputHash("gamma-event-detail", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", DateTimeOffset.Parse("2026-06-16T18:05:00Z"), "france-senegal", "event-1")
        ],
        Fixtures:
        [
            new LiveComplementaryBatchFixtureRow(
                "fixture-france-senegal",
                "France vs Senegal",
                "CANDIDATES",
                "france-senegal",
                "event-1",
                "France vs Senegal",
                EventMatchScore: 24,
                EventMarkets: 2,
                MappedContracts: 4,
                BooksFetched: 4,
                TwoLegCandidates: 1,
                GeneralizedCandidates: 1,
                PositiveLocks: 0,
                GeneralizedPositiveLocks: 0,
                Rejects: 0,
                Blocker: "WATCH_ONLY")
        ],
        Candidates: [],
        Rejects: []);

    private static string TamperedEntryJson() =>
        """
        {"savedAtUtc":"2026-06-16T18:00:00+00:00","entryId":"x","checkpointKind":"live-lock","schema":"world-cup-edge-lab/live-lock-checkpoint/v1","mode":"WATCH_ONLY_PUBLIC_READS","verdict":"HOLD_WATCH_ONLY","snapshotHash":"wrong","payloadHash":"wrong","liveOrderPath":"none","summary":"bad","payloadJson":"{}"}
        """;
}
