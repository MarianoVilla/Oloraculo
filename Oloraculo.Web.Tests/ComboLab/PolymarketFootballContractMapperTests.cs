using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Mapping;
using Oloraculo.Web.ComboLab.Markets;

namespace Oloraculo.Web.Tests.ComboLab;

public class PolymarketFootballContractMapperTests
{
    private static readonly FootballFixtureMappingContext Fixture = new(
        FixtureId: "fixture-france-senegal",
        HomeTeamId: "france",
        AwayTeamId: "senegal",
        HomeTeamName: "France",
        AwayTeamName: "Senegal");

    private static readonly PolymarketEventIdentity EventIdentity = new(
        EventSlug: "france-senegal",
        EventId: "event-1",
        EventTitle: "France vs Senegal");

    private static readonly FootballFixtureMappingContext IraqNorwayFixture = new(
        FixtureId: "fixture-irq-nor",
        HomeTeamId: "irq",
        AwayTeamId: "nor",
        HomeTeamName: "Iraq",
        AwayTeamName: "Norway");

    [Fact]
    public void MapsThreeWayMoneylineAndPreservesTokenIdentity()
    {
        var mapping = Map(Market(
            question: "France vs Senegal moneyline",
            marketType: "moneyline",
            tokens:
            [
                Token("France", "token-france"),
                Token("Draw", "token-draw"),
                Token("Senegal", "token-senegal")
            ]));

        Assert.Empty(mapping.MarketRejects);
        Assert.True(mapping.HasMappedContracts);
        AssertToken(mapping, "France", FootballMarketType.Moneyline, ContractSelection.Home, tokenId: "token-france");
        AssertToken(mapping, "Draw", FootballMarketType.Moneyline, ContractSelection.Draw, tokenId: "token-draw");
        var away = AssertToken(mapping, "Senegal", FootballMarketType.Moneyline, ContractSelection.Away, tokenId: "token-senegal");
        Assert.Equal("france-senegal", away.Identity.EventSlug);
        Assert.Equal("condition-1", away.Identity.ConditionId);
        Assert.Equal("Senegal", away.Identity.OutcomeSide);
    }

    [Fact]
    public void MapsBttsYesNo()
    {
        var mapping = Map(Market(
            question: "Will both teams score?",
            marketType: "both teams to score",
            tokens: [Token("Yes", "token-yes"), Token("No", "token-no")]));

        AssertToken(mapping, "Yes", FootballMarketType.BothTeamsToScore, ContractSelection.Yes);
        AssertToken(mapping, "No", FootballMarketType.BothTeamsToScore, ContractSelection.No);
    }

    [Fact]
    public void MapsBinaryPolymarketMoneylineSeries()
    {
        var mapping = Map(Market(
            question: "Norway",
            marketType: "moneyline",
            sportsMarketType: "moneyline",
            slug: "fifwc-irq-nor-2026-06-16-nor",
            tokens: [Token("Yes", "token-nor-yes"), Token("No", "token-nor-no")]), IraqNorwayFixture);

        var yes = AssertToken(mapping, "Yes", FootballMarketType.Moneyline, ContractSelection.Away, fixtureId: "fixture-irq-nor", tokenId: "token-nor-yes");
        Assert.Equal(TeamSide.Away, yes.Team);
        var no = AssertToken(mapping, "No", FootballMarketType.Moneyline, ContractSelection.NotAway, fixtureId: "fixture-irq-nor", tokenId: "token-nor-no");
        Assert.Equal(TeamSide.Away, no.Team);
    }

    [Fact]
    public void MapsRealWorldCupSpreadAndTotalFromSportsMarketTypeAndLine()
    {
        var spread = Map(Market(
            question: "Spread: Norway (-1.5)",
            marketType: "spread",
            sportsMarketType: "spreads",
            slug: "fifwc-irq-nor-2026-06-16-spread-away-1pt5",
            line: -1.5m,
            tokens: [Token("Yes", "token-spread-yes"), Token("No", "token-spread-no")]), IraqNorwayFixture);
        var total = Map(Market(
            question: "Total: Over 2.5 Goals",
            marketType: "total goals",
            sportsMarketType: "totals",
            slug: "fifwc-irq-nor-2026-06-16-total-2pt5",
            line: 2.5m,
            tokens: [Token("Yes", "token-over-yes"), Token("No", "token-over-no")]), IraqNorwayFixture);

        var spreadYes = AssertToken(spread, "Yes", FootballMarketType.Spread, ContractSelection.Yes, fixtureId: "fixture-irq-nor", tokenId: "token-spread-yes");
        Assert.Equal(TeamSide.Away, spreadYes.Team);
        Assert.Equal(-1.5m, spreadYes.Line);
        var under = AssertToken(total, "No", FootballMarketType.MatchTotal, ContractSelection.Under, fixtureId: "fixture-irq-nor", tokenId: "token-over-no");
        Assert.Equal(2.5m, under.Line);
    }

    [Fact]
    public void MapsMatchTotalAndTeamTotalHalfLines()
    {
        var matchTotal = Map(Market(
            question: "Will there be over 2.5 total goals in France vs Senegal?",
            marketType: "total goals",
            tokens: [Token("Yes", "token-match-yes"), Token("No", "token-match-no")]));
        var homeTeamTotal = Map(Market(
            question: "Will France score over 0.5 total goals?",
            marketType: "team total goals",
            tokens: [Token("Yes", "token-home-over"), Token("No", "token-home-under")]));

        var over25 = AssertToken(matchTotal, "Yes", FootballMarketType.MatchTotal, ContractSelection.Over);
        Assert.Equal(2.5m, over25.Line);
        Assert.Null(over25.Team);
        var under05 = AssertToken(homeTeamTotal, "No", FootballMarketType.TeamTotal, ContractSelection.Under);
        Assert.Equal(.5m, under05.Line);
        Assert.Equal(TeamSide.Home, under05.Team);
    }

    [Fact]
    public void MapsBinaryExactScoreQuestion()
    {
        var mapping = Map(Market(
            question: "Will the correct score be France 2-1 Senegal?",
            marketType: "correct score",
            tokens: [Token("Yes", "token-score-yes"), Token("No", "token-score-no")]));

        var yes = AssertToken(mapping, "Yes", FootballMarketType.ExactScore, ContractSelection.Yes);
        var no = AssertToken(mapping, "No", FootballMarketType.ExactScore, ContractSelection.No);
        Assert.Equal(2, yes.ExactHomeGoals);
        Assert.Equal(1, yes.ExactAwayGoals);
        Assert.Equal(2, no.ExactHomeGoals);
        Assert.Equal(1, no.ExactAwayGoals);
    }

    [Fact]
    public void RejectsBinarySoccerMoneylineWithoutDraw()
    {
        var mapping = Map(Market(
            question: "France vs Senegal moneyline",
            marketType: "moneyline",
            tokens: [Token("France", "token-france"), Token("Senegal", "token-senegal")]));

        Assert.False(mapping.HasMappedContracts);
        Assert.All(mapping.Tokens, token => AssertReject(token, FootballContractMapRejectReason.UnsupportedBinaryMoneyline));
    }

    [Fact]
    public void RejectsAmbiguousTeamTotalSubject()
    {
        var mapping = Map(Market(
            question: "France and Senegal team total goals over 0.5",
            marketType: "team total goals",
            tokens: [Token("Yes", "token-yes"), Token("No", "token-no")]));

        Assert.False(mapping.HasMappedContracts);
        Assert.All(mapping.Tokens, token => AssertReject(token, FootballContractMapRejectReason.AmbiguousTeam));
    }

    [Fact]
    public void RejectsMissingTokenWhileMappingOtherTokens()
    {
        var mapping = Map(Market(
            question: "France vs Senegal moneyline",
            marketType: "moneyline",
            tokens:
            [
                Token("France", "token-france"),
                Token("Draw", ""),
                Token("Senegal", "token-senegal")
            ]));

        Assert.NotNull(mapping.Tokens.Single(token => token.Outcome == "France").Contract);
        var draw = mapping.Tokens.Single(token => token.Outcome == "Draw");
        Assert.Null(draw.Contract);
        AssertReject(draw, FootballContractMapRejectReason.MissingTokenId);
    }

    [Fact]
    public void PropagatesMarketLevelRejectsAndBlocksContracts()
    {
        var mapping = Map(Market(
            question: "Will France score over 0.5 total goals in the first half?",
            conditionId: null,
            marketType: "team total goals",
            tokens: [Token("Yes", "token-yes"), Token("No", "token-no")]));

        Assert.False(mapping.HasMappedContracts);
        Assert.Contains(mapping.MarketRejects, reject => reject.Reason == FootballContractMapRejectReason.MissingConditionId);
        Assert.Contains(mapping.MarketRejects, reject => reject.Reason == FootballContractMapRejectReason.UnsupportedSettlementScope);
        Assert.All(mapping.Tokens, token =>
        {
            AssertReject(token, FootballContractMapRejectReason.MissingConditionId);
            AssertReject(token, FootballContractMapRejectReason.UnsupportedSettlementScope);
            AssertReject(token, FootballContractMapRejectReason.UnsupportedMarketType);
        });
    }

    [Fact]
    public void RejectsUnsupportedExactScoreOtherBucket()
    {
        var mapping = Map(Market(
            question: "Correct score France vs Senegal",
            marketType: "correct score",
            tokens: [Token("France 1-0 Senegal", "token-10"), Token("Any Other Score", "token-other")]));

        AssertToken(mapping, "France 1-0 Senegal", FootballMarketType.ExactScore, ContractSelection.Yes);
        var other = mapping.Tokens.Single(token => token.Outcome == "Any Other Score");
        Assert.Null(other.Contract);
        AssertReject(other, FootballContractMapRejectReason.UnsupportedExactScoreOther);
    }

    [Fact]
    public void RejectsPlayerPropsUntilPlayerModelExists()
    {
        var mapping = Map(Market(
            question: "Will Kylian Mbappe record over 2.5 shots?",
            marketType: "player shots",
            sportsMarketType: "soccer_player_shots",
            tokens: [Token("Yes", "token-yes"), Token("No", "token-no")],
            line: 2.5m));

        Assert.False(mapping.HasMappedContracts);
        Assert.Contains(mapping.MarketRejects, reject => reject.Reason == FootballContractMapRejectReason.UnsupportedSettlementScope);
        Assert.All(mapping.Tokens, token => AssertReject(token, FootballContractMapRejectReason.UnsupportedMarketType));
    }

    private static FootballContractMarketMapping Map(PolymarketMarketSnapshot market) =>
        new PolymarketFootballContractMapper().Map(market, Fixture, EventIdentity);

    private static FootballContractMarketMapping Map(PolymarketMarketSnapshot market, FootballFixtureMappingContext fixture) =>
        new PolymarketFootballContractMapper().Map(market, fixture, EventIdentity);

    private static PolymarketMarketSnapshot Market(
        string question,
        string marketType,
        IReadOnlyList<PolymarketOutcomeToken> tokens,
        string? conditionId = "condition-1",
        string? sportsMarketType = null,
        string? slug = null,
        decimal? line = null) => new()
        {
            MarketId = "market-1",
            Slug = slug ?? question.ToLowerInvariant().Replace(' ', '-').Replace('?', '-'),
            Question = question,
            Description = "Full-time regulation market.",
            ConditionId = conditionId,
            Active = true,
            Closed = false,
            EnableOrderBook = true,
            OrderMinSize = 5m,
            OrderPriceMinTickSize = .01m,
            Category = "Sports",
            MarketType = marketType,
            SportsMarketType = sportsMarketType,
            Line = line,
            Tokens = tokens
        };

    private static PolymarketOutcomeToken Token(string outcome, string tokenId) =>
        new(outcome, tokenId, OutcomePrice: null);

    private static FootballContract AssertToken(
        FootballContractMarketMapping mapping,
        string outcome,
        FootballMarketType marketType,
        ContractSelection selection,
        string? fixtureId = "fixture-france-senegal",
        string? tokenId = null)
    {
        var token = mapping.Tokens.Single(token => token.Outcome == outcome);
        var contract = Assert.IsType<FootballContract>(token.Contract);
        Assert.Equal(marketType, contract.MarketType);
        Assert.Equal(selection, contract.Selection);
        Assert.Equal(fixtureId, contract.FixtureId);
        if (tokenId is not null)
            Assert.Equal(tokenId, contract.Identity.TokenId);
        Assert.Equal(outcome, contract.Identity.OutcomeSide);
        return contract;
    }

    private static void AssertReject(FootballContractTokenMapping token, FootballContractMapRejectReason reason) =>
        Assert.Contains(token.Rejects, reject => reject.Reason == reason);
}
