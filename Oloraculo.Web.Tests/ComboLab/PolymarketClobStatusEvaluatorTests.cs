using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.ComboLab.Scalp;
using Oloraculo.Web.Feeds;

namespace Oloraculo.Web.Tests.ComboLab;

public sealed class PolymarketClobStatusEvaluatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-18T18:00:00Z");

    [Fact]
    public void Evaluate_NoSampledTokensReportsEmpty()
    {
        var report = PolymarketClobStatusEvaluator.Evaluate([], Now, TimeSpan.FromSeconds(30));

        Assert.Equal(FeedAdapterState.Empty, report.State);
        Assert.Equal(["NO_CLOB_TOKENS"], report.Blockers);
    }

    [Fact]
    public void FetchFailureReportsDown()
    {
        var report = PolymarketClobStatusEvaluator.FetchFailure(Now, "Authorization: Bearer should-not-leak");

        Assert.Equal(FeedAdapterState.Down, report.State);
        Assert.Equal(["CLOB_FETCH_FAILED"], report.Blockers);
        Assert.Contains("<redacted", report.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("should-not-leak", report.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_StaleBookReportsStale()
    {
        var report = PolymarketClobStatusEvaluator.Evaluate([Sample(received: Now.AddMinutes(-2))], Now, TimeSpan.FromSeconds(30));

        Assert.Equal(FeedAdapterState.Stale, report.State);
        Assert.Equal(["STALE_CLOB"], report.Blockers);
    }

    [Fact]
    public void Evaluate_NoBookReportsEmpty()
    {
        var report = PolymarketClobStatusEvaluator.Evaluate(
            [new PolymarketClobStatusSample("token", Now, [], [], null, null, 0, 0, HasBook: false)],
            Now,
            TimeSpan.FromSeconds(30));

        Assert.Equal(FeedAdapterState.Empty, report.State);
        Assert.Equal(["NO_ORDER_BOOK"], report.Blockers);
    }

    [Fact]
    public void Evaluate_NoBidReportsBlocked()
    {
        var report = PolymarketClobStatusEvaluator.Evaluate(
            [Sample(bids: [], bestBid: null)],
            Now,
            TimeSpan.FromSeconds(30));

        Assert.Equal(FeedAdapterState.Blocked, report.State);
        Assert.Equal(["NO_BID"], report.Blockers);
    }

    [Fact]
    public void Evaluate_NoAskReportsBlocked()
    {
        var report = PolymarketClobStatusEvaluator.Evaluate(
            [Sample(asks: [], bestAsk: null)],
            Now,
            TimeSpan.FromSeconds(30));

        Assert.Equal(FeedAdapterState.Blocked, report.State);
        Assert.Equal(["NO_ASK"], report.Blockers);
    }

    [Fact]
    public void Evaluate_CrossedBookReportsBlocked()
    {
        var report = PolymarketClobStatusEvaluator.Evaluate(
            [Sample(bestBid: .55m, bestAsk: .54m)],
            Now,
            TimeSpan.FromSeconds(30));

        Assert.Equal(FeedAdapterState.Blocked, report.State);
        Assert.Equal(["CROSSED_BOOK"], report.Blockers);
    }

    [Fact]
    public void Evaluate_ThinDepthReportsBlocked()
    {
        var report = PolymarketClobStatusEvaluator.Evaluate(
            [Sample(askDepth: .25m, bidDepth: .25m)],
            Now,
            TimeSpan.FromSeconds(30),
            minimumDepthUsd: 1m);

        Assert.Equal(FeedAdapterState.Blocked, report.State);
        Assert.Equal(["INSUFFICIENT_DEPTH"], report.Blockers);
    }

    [Fact]
    public void Evaluate_FreshDepthReportsReady()
    {
        var report = PolymarketClobStatusEvaluator.Evaluate([Sample()], Now, TimeSpan.FromSeconds(30), minimumDepthUsd: 1m);

        Assert.Equal(FeedAdapterState.Ready, report.State);
        Assert.Equal("polymarket_clob", report.SourceId);
        Assert.Equal(1, report.RowsLastMinute);
        Assert.Equal(Now, report.LatestRecvTsUtc);
        Assert.Null(report.Blockers);
    }

    private static PolymarketClobStatusSample Sample(
        DateTimeOffset? received = null,
        IReadOnlyList<PolymarketBookLevel>? bids = null,
        IReadOnlyList<PolymarketBookLevel>? asks = null,
        decimal? bestBid = .48m,
        decimal? bestAsk = .50m,
        decimal askDepth = 50m,
        decimal bidDepth = 50m) =>
        new(
            "token",
            received ?? Now,
            bids ?? [new PolymarketBookLevel(bestBid ?? .48m, 100m)],
            asks ?? [new PolymarketBookLevel(bestAsk ?? .50m, 100m)],
            bestBid,
            bestAsk,
            askDepth,
            bidDepth,
            HasBook: true);
}
