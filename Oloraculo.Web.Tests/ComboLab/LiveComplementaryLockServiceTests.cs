using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oloraculo.Web.Components.Shared;
using Oloraculo.Web.ComboLab.Monitor;
using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Services;

namespace Oloraculo.Web.Tests.ComboLab;

public class LiveComplementaryLockServiceTests : TestFixtures
{
    [Fact]
    public async Task RefreshAsync_MapsEventFetchesBooksAndGeneratesWatchOnlyCandidates()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/slug/france-senegal?include_chat=false&include_template=false"] = EventJson(),
            [BooksBatchRequest()] = BookBatchJson(
                ("btts-yes", .44m, .46m),
                ("btts-no", .45m, .47m),
                ("over-25", .48m, .50m),
                ("under-25", .47m, .52m))
        });

        var snapshot = await service.RefreshAsync("fixture-france-senegal", "france-senegal", sharesPerLeg: 5m, executionBuffer: .02m);

        Assert.Equal("France vs Senegal", snapshot.FixtureLabel);
        Assert.Equal("France vs Senegal", snapshot.EventTitle);
        Assert.Equal(2, snapshot.EventMarkets);
        Assert.Equal(4, snapshot.MappedContracts);
        Assert.Equal(4, snapshot.BooksFetched);
        Assert.Contains(snapshot.InputHashes, input => input.Source == "gamma-event-detail" && input.PayloadHash.Length == 64 && input.EventSlug == "france-senegal");
        Assert.Contains(snapshot.InputHashes, input => input.Source == "clob-book" && input.PayloadHash.Length == 64 && input.TokenId == "btts-yes");
        Assert.Equal("FRESH", snapshot.InputFreshness.Status);
        Assert.Equal(5, snapshot.InputFreshness.InputCount);
        Assert.Equal(1, snapshot.InputFreshness.GammaInputCount);
        Assert.Equal(4, snapshot.InputFreshness.ClobBookInputCount);
        Assert.InRange(snapshot.InputFreshness.MaxAgeSeconds, 0, 120);
        Assert.Contains("public input hashes", snapshot.InputFreshness.Detail);
        Assert.Equal("PRE_GAME", snapshot.EventState.Phase);
        Assert.Equal("PRE_GAME_ONLY", snapshot.EventState.TimeMode);
        Assert.Equal("PRE_GAME_NO_DECAY", snapshot.GoalDecay.Mode);
        Assert.False(snapshot.GoalDecay.Applied);
        Assert.Equal("PRE_GAME_UNCONDITIONED", snapshot.ScoreConditioning.Mode);
        Assert.False(snapshot.ScoreConditioning.Applied);
        Assert.Contains("scoreline", snapshot.DistributionSource, StringComparison.OrdinalIgnoreCase);
        var positive = Assert.Single(snapshot.Candidates, candidate => candidate.Verdict == "PositiveLock" && candidate.Structure.Contains("BothTeamsToScore", StringComparison.Ordinal));
        Assert.Equal("WATCH_ONLY: needs live depth/fill/fee proof", positive.Blocker);
        Assert.Equal("PRE_GAME_ONLY", positive.TimeMode);
        Assert.Contains("full pre-game score grid", positive.TimeGate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(snapshot.Candidates, candidate => candidate.Blocker.Contains("coverage gap", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.GeneralizedCandidates, candidate => candidate.StrategyKind == "total-band" && candidate.Structure.Contains("exact total complement", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snapshot.CandidateBlockerCounts, rollup => rollup.Blocker == "WATCH_ONLY: needs live depth/fill/fee proof" && rollup.Count >= 1);
        Assert.Contains(snapshot.CandidateBlockerCounts, rollup => rollup.Blocker.Contains("coverage gap", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(snapshot.RejectSourceDetailCounts);
    }

    [Fact]
    public async Task InputFreshnessSummary_RendersFreshnessFromLiveScanSnapshot()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/slug/france-senegal?include_chat=false&include_template=false"] = EventJson(),
            [BooksBatchRequest()] = BookBatchJson(
                ("btts-yes", .44m, .46m),
                ("btts-no", .45m, .47m),
                ("over-25", .48m, .50m),
                ("under-25", .47m, .52m))
        });

        var snapshot = await service.RefreshAsync("fixture-france-senegal", "france-senegal", sharesPerLeg: 5m, executionBuffer: .02m);
        var html = await RenderFreshnessSummaryAsync(snapshot.InputFreshness);

        Assert.Contains("input freshness", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FRESH", html, StringComparison.Ordinal);
        Assert.Contains("max input age", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Input freshness summary", html, StringComparison.Ordinal);
        Assert.Contains("5 public input hashes", html, StringComparison.Ordinal);
        Assert.Contains("gamma 1", html, StringComparison.Ordinal);
        Assert.Contains("clob books 4", html, StringComparison.Ordinal);
        Assert.Contains("oldest/newest", html, StringComparison.Ordinal);
        Assert.Contains("UTC", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshAsync_TagsInPlayCandidatesWhenScoreAndElapsedParse()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/slug/france-senegal?include_chat=false&include_template=false"] = EventJsonWithExtra("""
            "live": true,
            "score": "1-0",
            "elapsed": "45+2",
            """),
            [BooksBatchRequest()] = BookBatchJson(
                ("btts-yes", .44m, .46m),
                ("btts-no", .45m, .47m),
                ("over-25", .48m, .50m),
                ("under-25", .47m, .52m))
        });

        var snapshot = await service.RefreshAsync("fixture-france-senegal", "france-senegal", sharesPerLeg: 5m, executionBuffer: .02m);

        Assert.Equal("IN_PLAY", snapshot.EventState.Phase);
        Assert.Equal("IN_PLAY_COMPATIBLE", snapshot.EventState.TimeMode);
        Assert.Equal(1, snapshot.EventState.HomeGoals);
        Assert.Equal(0, snapshot.EventState.AwayGoals);
        Assert.Equal(47, snapshot.EventState.ElapsedMinute);
        Assert.Equal("CURRENT_MINUTE_GOAL_DECAY_MARKET_ANCHORED", snapshot.GoalDecay.Mode);
        Assert.True(snapshot.GoalDecay.Applied);
        Assert.InRange(snapshot.GoalDecay.RemainingFraction!.Value, .47, .48);
        Assert.True(snapshot.GoalDecay.RemainingHomeExpectedGoals < snapshot.GoalDecay.KickoffHomeExpectedGoals);
        Assert.Equal("CURRENT_SCORE_CONDITIONED", snapshot.ScoreConditioning.Mode);
        Assert.True(snapshot.ScoreConditioning.Applied);
        Assert.True(snapshot.ScoreConditioning.RetainedStateCount < snapshot.ScoreConditioning.OriginalStateCount);
        Assert.Contains("market-anchored live total 2.5", snapshot.DistributionSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("minute-decayed score 1-0", snapshot.DistributionSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current score conditioned 1-0", snapshot.DistributionSource, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("market-implied live total anchor line 2.5", snapshot.GoalDecay.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("normalized over", snapshot.GoalDecay.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(snapshot.Candidates);
        Assert.All(snapshot.Candidates, candidate => Assert.Equal("IN_PLAY_COMPATIBLE", candidate.TimeMode));
        Assert.All(snapshot.Candidates, candidate => Assert.Contains("market-implied live total anchor", candidate.TimeGate, StringComparison.OrdinalIgnoreCase));
        Assert.All(snapshot.Candidates, candidate => Assert.Contains("conditioned terminal states", candidate.TimeGate, StringComparison.OrdinalIgnoreCase));
        Assert.All(snapshot.GeneralizedCandidates, candidate => Assert.Equal("IN_PLAY_COMPATIBLE", candidate.TimeMode));
    }

    [Fact]
    public async Task RefreshAsync_FallsBackToModelDecayWhenPairedTotalBookAnchorIsMissing()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/slug/france-senegal?include_chat=false&include_template=false"] = EventJsonWithExtra("""
            "live": true,
            "score": "1-0",
            "elapsed": "47",
            """),
            [BooksBatchRequest()] = BookBatchJson(
                ("btts-yes", .44m, .46m),
                ("btts-no", .45m, .47m),
                ("over-25", .48m, null),
                ("under-25", .47m, .52m))
        });

        var snapshot = await service.RefreshAsync("fixture-france-senegal", "france-senegal", sharesPerLeg: 5m, executionBuffer: .02m);

        Assert.Equal("CURRENT_MINUTE_GOAL_DECAY", snapshot.GoalDecay.Mode);
        Assert.Contains("no paired live O/U anchor found", snapshot.GoalDecay.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("minute-decayed score 1-0", snapshot.DistributionSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshAsync_BlocksInPlayCandidatesWhenScoreStateIsMissing()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/slug/france-senegal?include_chat=false&include_template=false"] = EventJsonWithExtra("""
            "live": true,
            "score": "1-0",
            """)
        });

        var snapshot = await service.RefreshAsync("fixture-france-senegal", "france-senegal", sharesPerLeg: 5m, executionBuffer: .02m);

        Assert.Equal("IN_PLAY_STATE_MISSING", snapshot.EventState.Phase);
        Assert.Equal("BLOCKED_IN_PLAY_STATE_MISSING", snapshot.EventState.TimeMode);
        Assert.Equal("BLOCKED_IN_PLAY_STATE_MISSING", snapshot.ScoreConditioning.Mode);
        Assert.False(snapshot.EventState.AllowsCandidatePricing);
        Assert.Empty(snapshot.Candidates);
        Assert.Empty(snapshot.GeneralizedCandidates);
        Assert.Equal(0, snapshot.BooksFetched);
        Assert.Contains(snapshot.Rejects, reject => reject.Source == LiveComplementaryLockRejectSource.Event && reject.Detail.Contains("missing parseable score", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RefreshAsync_RecordsBookAndCandidateRejectsWithoutThrowing()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/slug/france-senegal?include_chat=false&include_template=false"] = EventJson(),
            [BooksBatchRequest()] = BookBatchJson(
                ("btts-yes", .44m, null),
                ("btts-no", .45m, .47m),
                ("over-25", .48m, .50m),
                ("under-25", .47m, .52m))
        });

        var snapshot = await service.RefreshAsync("fixture-france-senegal", "france-senegal", sharesPerLeg: 5m, executionBuffer: .02m);

        Assert.Contains(snapshot.Rejects, reject => reject.Source == LiveComplementaryLockRejectSource.Book && reject.Detail == PolymarketRejectReason.NoAsk.ToString() && reject.TokenId == "btts-yes");
        Assert.Contains(snapshot.Rejects, reject => reject.Source == LiveComplementaryLockRejectSource.Candidate && reject.Detail.Contains("MissingExecutableAsk", StringComparison.Ordinal));
        Assert.Contains(snapshot.RejectSourceDetailCounts, rollup => rollup.Source == LiveComplementaryLockRejectSource.Book && rollup.Detail == PolymarketRejectReason.NoAsk.ToString() && rollup.Count == 1);
        Assert.Contains(snapshot.RejectSourceDetailCounts, rollup => rollup.Source == LiveComplementaryLockRejectSource.Candidate && rollup.Detail.Contains("MissingExecutableAsk", StringComparison.Ordinal) && rollup.Count >= 1);
    }

    [Fact]
    public async Task RefreshAsync_ReturnsFixtureRejectWhenFixtureIsMissing()
    {
        await using var db = await NewDb();
        var service = Service(db, new Dictionary<string, string>());

        var snapshot = await service.RefreshAsync("missing-fixture", "france-senegal");

        Assert.Empty(snapshot.Candidates);
        Assert.Single(snapshot.Rejects, reject => reject.Source == LiveComplementaryLockRejectSource.Fixture);
        Assert.Equal("NO_INPUTS", snapshot.InputFreshness.Status);
        Assert.Equal(0, snapshot.InputFreshness.InputCount);
    }

    [Fact]
    public async Task SuggestEventAsync_ReturnsClearTopFixtureEventMatch()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&series_id=11433&include_children=true&include_best_lines=true&limit=100"] = """
            {"data":[
              {"id":"event-1","slug":"fifwc-fra-sen-2026-06-16","title":"France vs Senegal","active":true,"closed":false},
              {"id":"event-2","slug":"fifwc-fra-bra-2026-06-16","title":"France vs Brazil","active":true,"closed":false}
            ]}
            """
        });

        var suggestion = await service.SuggestEventAsync("fixture-france-senegal");

        Assert.True(suggestion.HasSuggestion);
        Assert.Equal("fifwc-fra-sen-2026-06-16", suggestion.EventSlug);
        Assert.Equal("event-1", suggestion.EventId);
        Assert.Empty(suggestion.Rejects);
        Assert.Contains(suggestion.Matches, match => match.EventSlug == "fifwc-fra-sen-2026-06-16" && match.Score > 0);
    }

    [Fact]
    public async Task SuggestEventAsync_RejectsAmbiguousTopMatches()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&series_id=11433&include_children=true&include_best_lines=true&limit=100"] = """
            {"data":[
              {"id":"event-1","slug":"fifwc-fra-sen-a","title":"France vs Senegal","active":true,"closed":false},
              {"id":"event-2","slug":"fifwc-fra-sen-b","title":"France vs Senegal","active":true,"closed":false}
            ]}
            """
        });

        var suggestion = await service.SuggestEventAsync("fixture-france-senegal");

        Assert.False(suggestion.HasSuggestion);
        Assert.Contains(suggestion.Rejects, reject => reject.Source == LiveComplementaryLockRejectSource.Event && reject.Detail.Contains("Ambiguous", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BatchScanAsync_MapsFixturesAndRanksWatchOnlyGeneralizedCandidates()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&series_id=11433&include_children=true&include_best_lines=true&limit=100"] = """
            {"data":[
              {"id":"event-1","slug":"france-senegal","title":"France vs Senegal","active":true,"closed":false}
            ]}
            """,
            ["https://gamma.test/events/slug/france-senegal?include_chat=false&include_template=false"] = EventJson(),
            [BooksBatchRequest()] = BookBatchJson(
                ("btts-yes", .44m, .46m),
                ("btts-no", .45m, .47m),
                ("over-25", .48m, .50m),
                ("under-25", .47m, .52m))
        });

        var snapshot = await service.BatchScanAsync(new LiveComplementaryBatchScanOptions
        {
            MaxFixtures = 1,
            MaxCandidatesPerFixture = 4,
            MaxTotalCandidates = 4,
            SharesPerLeg = 5m,
            ExecutionBuffer = .02m
        });

        Assert.Equal("WATCH_ONLY_PUBLIC_READS", snapshot.Mode);
        Assert.Equal(1, snapshot.FixturesSeen);
        Assert.Equal(1, snapshot.FixturesScanned);
        Assert.Equal(1, snapshot.EventMatches);
        Assert.Equal(4, snapshot.TotalBooksFetched);
        Assert.Contains(snapshot.InputHashes, input => input.Source == "gamma-events-list" && input.PayloadHash.Length == 64);
        Assert.Contains(snapshot.InputHashes, input => input.Source == "gamma-event-detail" && input.EventSlug == "france-senegal");
        Assert.Contains(snapshot.InputHashes, input => input.Source == "clob-book" && input.TokenId == "under-25");
        Assert.Equal("FRESH", snapshot.InputFreshness.Status);
        Assert.Equal(6, snapshot.InputFreshness.InputCount);
        Assert.Equal(2, snapshot.InputFreshness.GammaInputCount);
        Assert.Equal(4, snapshot.InputFreshness.ClobBookInputCount);
        Assert.InRange(snapshot.InputFreshness.MaxAgeSeconds, 0, 120);
        var fixtureRow = Assert.Single(snapshot.Fixtures);
        Assert.Equal("CANDIDATES", fixtureRow.Status);
        Assert.Equal("france-senegal", fixtureRow.EventSlug);
        Assert.Contains(snapshot.FixtureScanStatusCounts, rollup => rollup.Status == "CANDIDATES" && rollup.Count == 1);
        Assert.NotEmpty(snapshot.Candidates);
        Assert.NotEmpty(snapshot.CandidateBlockerCounts);
        Assert.All(snapshot.Candidates, candidate =>
        {
            Assert.Equal("fixture-france-senegal", candidate.FixtureId);
            Assert.Equal("france-senegal", candidate.EventSlug);
            Assert.False(string.IsNullOrWhiteSpace(candidate.Blocker));
        });

        var checkpoint = LiveComplementaryBatchScanCheckpoint.Create(snapshot);
        Assert.Equal("HOLD_WATCH_ONLY", checkpoint.Verdict);
        Assert.Equal(64, checkpoint.SnapshotHash.Length);
        Assert.Contains("world-cup-edge-lab/batch-lock-checkpoint/v1", checkpoint.PayloadJson);
        Assert.Contains("WATCH_ONLY_PUBLIC_READS", checkpoint.PayloadJson);
        Assert.Contains("LiveOrderPath", checkpoint.PayloadJson);
        Assert.DoesNotContain("private", checkpoint.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", checkpoint.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(checkpoint.RequiredValidationGates, gate => gate.Contains("authenticated fills", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InputFreshnessSummary_RendersFreshnessFromBatchScanSnapshot()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&series_id=11433&include_children=true&include_best_lines=true&limit=100"] = """
            {"data":[
              {"id":"event-1","slug":"france-senegal","title":"France vs Senegal","active":true,"closed":false}
            ]}
            """,
            ["https://gamma.test/events/slug/france-senegal?include_chat=false&include_template=false"] = EventJson(),
            [BooksBatchRequest()] = BookBatchJson(
                ("btts-yes", .44m, .46m),
                ("btts-no", .45m, .47m),
                ("over-25", .48m, .50m),
                ("under-25", .47m, .52m))
        });

        var snapshot = await service.BatchScanAsync(new LiveComplementaryBatchScanOptions
        {
            MaxFixtures = 1,
            MaxCandidatesPerFixture = 4,
            MaxTotalCandidates = 4,
            SharesPerLeg = 5m,
            ExecutionBuffer = .02m
        });
        var html = await RenderFreshnessSummaryAsync(snapshot.InputFreshness, "batch input freshness", "Batch input freshness summary");

        Assert.Contains("batch input freshness", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FRESH", html, StringComparison.Ordinal);
        Assert.Contains("max input age", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Batch input freshness summary", html, StringComparison.Ordinal);
        Assert.Contains("6 public input hashes", html, StringComparison.Ordinal);
        Assert.Contains("gamma 2", html, StringComparison.Ordinal);
        Assert.Contains("clob books 4", html, StringComparison.Ordinal);
        Assert.Contains("oldest/newest", html, StringComparison.Ordinal);
        Assert.Contains("UTC", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatchScanAsync_ExposesEventlessFixturesWithoutBookFetches()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/keyset?closed=false&series_id=11433&include_children=true&include_best_lines=true&limit=100"] = """
            {"data":[
              {"id":"event-1","slug":"france-brazil","title":"France vs Brazil","active":true,"closed":false}
            ]}
            """
        });

        var snapshot = await service.BatchScanAsync(new LiveComplementaryBatchScanOptions
        {
            MaxFixtures = 1,
            MaxCandidatesPerFixture = 4,
            MaxTotalCandidates = 4
        });

        Assert.Equal(1, snapshot.FixturesSeen);
        Assert.Equal(0, snapshot.FixturesScanned);
        Assert.Equal(1, snapshot.EventlessFixtures);
        Assert.Equal(0, snapshot.TotalBooksFetched);
        var fixtureRow = Assert.Single(snapshot.Fixtures);
        Assert.Equal("NO_EVENT_MATCH", fixtureRow.Status);
        Assert.Contains("No Polymarket World Cup event matched", fixtureRow.Blocker);
        Assert.Contains(snapshot.Rejects, reject => reject.Source == LiveComplementaryLockRejectSource.Event);
        Assert.Contains(snapshot.FixtureScanStatusCounts, rollup => rollup.Status == "NO_EVENT_MATCH" && rollup.Count == 1);
        Assert.Contains(snapshot.RejectSourceDetailCounts, rollup => rollup.Source == LiveComplementaryLockRejectSource.Event && rollup.Count == 1);
    }

    [Fact]
    public async Task LiveLockCheckpointHashIsDeterministicForSameSnapshot()
    {
        await using var db = await FixtureDb();
        var service = Service(db, new Dictionary<string, string>
        {
            ["https://gamma.test/events/slug/france-senegal?include_chat=false&include_template=false"] = EventJson(),
            [BooksBatchRequest()] = BookBatchJson(
                ("btts-yes", .44m, .46m),
                ("btts-no", .45m, .47m),
                ("over-25", .48m, .50m),
                ("under-25", .47m, .52m))
        });
        var snapshot = await service.RefreshAsync("fixture-france-senegal", "france-senegal", sharesPerLeg: 5m, executionBuffer: .02m);

        var first = LiveComplementaryLockCheckpoint.Create(snapshot);
        var second = LiveComplementaryLockCheckpoint.Create(snapshot);

        Assert.Equal("HOLD_WATCH_ONLY", first.Verdict);
        Assert.Equal(first.SnapshotHash, second.SnapshotHash);
        Assert.Equal(64, first.SnapshotHash.Length);
        Assert.Equal(CryptoUtil.GetSha256(first.PayloadJson), first.SnapshotHash);
        Assert.Contains("world-cup-edge-lab/live-lock-checkpoint/v1", first.PayloadJson);
        Assert.Contains("WATCH_ONLY_PUBLIC_READS", first.PayloadJson);
        Assert.Contains("LiveOrderPath", first.PayloadJson);
        Assert.Contains("GeneralizedCandidates", first.PayloadJson);
        Assert.DoesNotContain("private", first.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", first.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("approval", first.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(first.RequiredValidationGates, gate => gate.Contains("authenticated fills", StringComparison.OrdinalIgnoreCase));
    }

    private static LiveComplementaryLockService Service(OloraculoDbContext db, IReadOnlyDictionary<string, string> responses)
    {
        var options = Options.Create(new OloraculoConfig
        {
            PolymarketGammaBaseUrl = "https://gamma.test",
            PolymarketClobBaseUrl = "https://clob.test",
            PolymarketComboRfqBaseUrl = "https://combo.test",
            RecentResultCount = 8,
            GoalModelYearsWindow = 3
        });
        var markets = new PolymarketMarketDataService(new HttpClient(new FakeHttpMessageHandler(responses)), options);
        return new LiveComplementaryLockService(db, markets, new PredictionService(db, options));
    }

    private static async Task<string> RenderFreshnessSummaryAsync(
        LiveComplementaryInputFreshness freshness,
        string statusLabel = "input freshness",
        string summaryLabel = "Input freshness summary")
    {
        using var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(serviceProvider, serviceProvider.GetRequiredService<ILoggerFactory>());

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(InputFreshnessSummary.Freshness)] = freshness,
                [nameof(InputFreshnessSummary.StatusLabel)] = statusLabel,
                [nameof(InputFreshnessSummary.SummaryLabel)] = summaryLabel
            });
            var output = await renderer.RenderComponentAsync<InputFreshnessSummary>(parameters);
            return output.ToHtmlString();
        });
    }

    private static async Task<OloraculoDbContext> FixtureDb()
    {
        var db = await NewDb();
        db.Teams.AddRange(
            new Team { Id = "france", Name = "France" },
            new Team { Id = "senegal", Name = "Senegal" });
        db.Fixtures.Add(new Fixture
        {
            Id = "fixture-france-senegal",
            Group = "I",
            HomeTeamId = "france",
            AwayTeamId = "senegal",
            NeutralVenue = true,
            KickoffUtc = DateTimeOffset.Parse("2026-06-16T19:00:00Z")
        });
        db.Results.AddRange(
            Result("france", "senegal", 2, 1),
            Result("france", "senegal", 1, 0),
            Result("senegal", "france", 1, 1));
        await db.SaveChangesAsync();
        return db;
    }

    private static string EventJson() => EventJsonWithExtra("");

    private static string EventJsonWithExtra(string extraFields) =>
        $$"""
        {
          "id":"event-1",
          "slug":"france-senegal",
          "title":"France vs Senegal",
          "active":true,
          "closed":false,
          {{extraFields}}
          "markets":[
            {"id":"m-btts","slug":"france-senegal-btts","question":"Will both teams score?","description":"Full-time regulation market.","conditionId":"condition-btts","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"btts-yes\",\"btts-no\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","marketType":"both teams to score","sportsMarketType":"btts"},
            {"id":"m-total","slug":"france-senegal-total-2pt5","question":"Total: Over 2.5 Goals","description":"Full-time regulation market.","conditionId":"condition-total","outcomes":"[\"Yes\",\"No\"]","clobTokenIds":"[\"over-25\",\"under-25\"]","active":true,"closed":false,"enableOrderBook":true,"orderMinSize":"5","orderPriceMinTickSize":"0.01","marketType":"total goals","sportsMarketType":"totals","line":"2.5"}
          ]
        }
        """;

    private static string BookJson(decimal bid, decimal? ask) => ask is null
        ? $$"""
          {"market":"condition","bids":[{"price":"{{bid}}","size":"100"}],"asks":[],"min_order_size":"5","tick_size":"0.01"}
          """
        : $$"""
          {"market":"condition","bids":[{"price":"{{bid}}","size":"100"}],"asks":[{"price":"{{ask.Value}}","size":"100"}],"min_order_size":"5","tick_size":"0.01"}
          """;

    private static string BooksBatchRequest() =>
        "POST https://clob.test/books\n[{\"token_id\":\"btts-yes\"},{\"token_id\":\"btts-no\"},{\"token_id\":\"over-25\"},{\"token_id\":\"under-25\"}]";

    private static string BookBatchJson(params (string TokenId, decimal Bid, decimal? Ask)[] books) =>
        $"[{string.Join(",", books.Select(book => BatchBookJson(book.TokenId, book.Bid, book.Ask)))}]";

    private static string BatchBookJson(string tokenId, decimal bid, decimal? ask) => ask is null
        ? $$"""
          {"asset_id":"{{tokenId}}","market":"condition","bids":[{"price":"{{bid}}","size":"100"}],"asks":[],"min_order_size":"5","tick_size":"0.01"}
          """
        : $$"""
          {"asset_id":"{{tokenId}}","market":"condition","bids":[{"price":"{{bid}}","size":"100"}],"asks":[{"price":"{{ask.Value}}","size":"100"}],"min_order_size":"5","tick_size":"0.01"}
          """;
}
