using Oloraculo.Web.ComboLab.Contracts;
using Oloraculo.Web.ComboLab.Markets;
using Oloraculo.Web.Helpers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Oloraculo.Web.ComboLab.Mapping
{
    public sealed record FootballFixtureMappingContext(
        string FixtureId,
        string HomeTeamId,
        string AwayTeamId,
        string HomeTeamName,
        string AwayTeamName);

    public sealed record PolymarketEventIdentity(
        string? EventSlug = null,
        string? EventId = null,
        string? EventTitle = null);

    public enum FootballContractMapRejectReason
    {
        SourceMarketRejected,
        MissingFixtureContext,
        MissingConditionId,
        MissingTokenId,
        MissingQuestionSlugOrOutcome,
        UnsupportedMarketType,
        UnsupportedSettlementScope,
        OutcomeSetMismatch,
        OutcomeTeamMismatch,
        UnsupportedBinaryMoneyline,
        AmbiguousTeam,
        MissingLine,
        IntegerLineUnsupported,
        NegativeLine,
        MissingExactScore,
        AmbiguousExactScore,
        UnsupportedExactScoreOther
    }

    public sealed record FootballContractMapReject(
        FootballContractMapRejectReason Reason,
        string Detail,
        string? Outcome = null,
        string? TokenId = null,
        string? Evidence = null);

    public sealed record FootballContractTokenMapping(
        string Outcome,
        string? TokenId,
        FootballContract? Contract,
        IReadOnlyList<FootballContractMapReject> Rejects);

    public sealed record FootballContractMarketMapping(
        string? MarketId,
        string? ConditionId,
        IReadOnlyList<FootballContractTokenMapping> Tokens,
        IReadOnlyList<FootballContractMapReject> MarketRejects)
    {
        public bool HasMappedContracts => Tokens.Any(token => token.Contract is not null);
    }

    public sealed class PolymarketFootballContractMapper
    {
        private static readonly Regex UnsupportedScopeRegex = new(
            @"\b(first\s+half|1st\s+half|second\s+half|2nd\s+half|halftime|half-time|extra\s+time|overtime|penalt(?:y|ies)|shootout|to\s+advance|advance|qualif(?:y|ies)|group\s+winner|tournament\s+winner|corners?|cards?|bookings?|player|shots?|assists?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex BttsRegex = new(
            @"\b(btts|both\s+teams(?:\s+to)?\s+score)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex MoneylineRegex = new(
            @"\b(moneyline|match\s+winner|full\s+time\s+result|3-way|three-way|1x2)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ExactScoreRegex = new(
            @"\b(exact\s+score|correct\s+score|final\s+score|scoreline)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ScoreRegex = new(
            @"(?<!\d)(?<home>\d{1,2})\s*[-:]\s*(?<away>\d{1,2})(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex OtherExactScoreRegex = new(
            @"\b(any\s+other|other|field|neither|none\s+of\s+these|\d+\+)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public FootballContractMarketMapping Map(
            PolymarketMarketSnapshot market,
            FootballFixtureMappingContext fixture,
            PolymarketEventIdentity? eventIdentity = null)
        {
            ArgumentNullException.ThrowIfNull(market);
            ArgumentNullException.ThrowIfNull(fixture);

            var marketRejects = MarketRejects(market).ToList();
            if (string.IsNullOrWhiteSpace(fixture.FixtureId) || string.IsNullOrWhiteSpace(fixture.HomeTeamName) || string.IsNullOrWhiteSpace(fixture.AwayTeamName))
                marketRejects.Add(new FootballContractMapReject(FootballContractMapRejectReason.MissingFixtureContext, "Fixture id and team names are required."));

            var text = MarketText(market);
            if (string.IsNullOrWhiteSpace(text))
                marketRejects.Add(new FootballContractMapReject(FootballContractMapRejectReason.MissingQuestionSlugOrOutcome, "Market lacks question, slug, description, and market type."));
            if (UnsupportedScopeRegex.IsMatch(text))
                marketRejects.Add(new FootballContractMapReject(FootballContractMapRejectReason.UnsupportedSettlementScope, "Market appears to settle outside regulation scoreline scope.", Evidence: text));

            var tokenMappings = MapTokens(market, fixture, eventIdentity, marketRejects, text).ToList();
            return new FootballContractMarketMapping(market.MarketId, market.ConditionId, tokenMappings, marketRejects);
        }

        private IEnumerable<FootballContractTokenMapping> MapTokens(
            PolymarketMarketSnapshot market,
            FootballFixtureMappingContext fixture,
            PolymarketEventIdentity? eventIdentity,
            IReadOnlyList<FootballContractMapReject> marketRejects,
            string text)
        {
            if (market.Tokens.Count == 0)
                return [RejectToken(string.Empty, null, FootballContractMapRejectReason.MissingTokenId, "Market has no token ids.", marketRejects)];

            if (marketRejects.Any(reject => reject.Reason is FootballContractMapRejectReason.MissingConditionId or FootballContractMapRejectReason.MissingFixtureContext or FootballContractMapRejectReason.UnsupportedSettlementScope))
                return market.Tokens.Select(token => RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.UnsupportedMarketType, "Market-level blocker prevents safe contract mapping.", marketRejects));

            var family = PolymarketFootballMarketClassifier.Classify(market).Family;

            if (family == PolymarketFootballMarketFamily.ExactScore || IsExactScore(market, text))
                return MapExactScore(market, fixture, eventIdentity, marketRejects, text);
            if (family == PolymarketFootballMarketFamily.BothTeamsToScore || BttsRegex.IsMatch(text))
                return MapBtts(market, fixture, eventIdentity, marketRejects);
            if (TryParseTeamTotalContext(market, text, fixture, out var team, out var teamLine, out var teamReject))
                return MapTotalTokens(market, fixture, eventIdentity, marketRejects, FootballMarketType.TeamTotal, team, teamLine, text);
            if (teamReject is not null)
                return market.Tokens.Select(token => RejectToken(token.Outcome, token.TokenId, teamReject.Value, "Team total subject is ambiguous or invalid.", marketRejects));
            if (TryParseMatchTotalLine(market, text, out var matchLine, out var matchLineReject))
                return MapTotalTokens(market, fixture, eventIdentity, marketRejects, FootballMarketType.MatchTotal, null, matchLine, text);
            if (matchLineReject is not null)
                return market.Tokens.Select(token => RejectToken(token.Outcome, token.TokenId, matchLineReject.Value, "Match total line is unsupported.", marketRejects));
            if (family == PolymarketFootballMarketFamily.Spread || LooksLikeSpread(text))
                return MapSpread(market, fixture, eventIdentity, marketRejects, text);
            if (family == PolymarketFootballMarketFamily.Moneyline || LooksLikeMoneyline(market, text))
                return MapMoneyline(market, fixture, eventIdentity, marketRejects);

            return market.Tokens.Select(token => RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.UnsupportedMarketType, "No supported Combo Lab v1 market type matched.", marketRejects, text));
        }

        private IEnumerable<FootballContractTokenMapping> MapMoneyline(
            PolymarketMarketSnapshot market,
            FootballFixtureMappingContext fixture,
            PolymarketEventIdentity? eventIdentity,
            IReadOnlyList<FootballContractMapReject> marketRejects)
        {
            var outcomeSelections = market.Tokens
                .Select(token => (Token: token, Selection: MoneylineSelection(token.Outcome, fixture)))
                .ToList();

            if (outcomeSelections.Any(item => item.Selection == ContractSelection.Draw))
            {
                if (!outcomeSelections.Any(item => item.Selection == ContractSelection.Home) || !outcomeSelections.Any(item => item.Selection == ContractSelection.Away))
                    return market.Tokens.Select(token => RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.OutcomeSetMismatch, "Moneyline outcomes did not include home/draw/away.", marketRejects));

                return outcomeSelections.Select(item => item.Selection is null
                    ? RejectToken(item.Token.Outcome, item.Token.TokenId, FootballContractMapRejectReason.OutcomeTeamMismatch, "Outcome did not match home, draw, or away.", marketRejects)
                    : MapToken(item.Token, Contract(
                        market,
                        fixture,
                        eventIdentity,
                        FootballMarketType.Moneyline,
                        item.Selection.Value,
                        TeamForMoneyline(item.Selection.Value),
                        label: MoneylineLabel(item.Selection.Value, fixture)), marketRejects));
            }

            if (market.Tokens.All(token => YesNoSelection(token.Outcome) is not null) && TryParseBinaryMoneylineSubject(market, fixture, out var subject))
            {
                return market.Tokens.Select(token => YesNoSelection(token.Outcome) is { } yesNo
                    ? MapToken(token, Contract(
                        market,
                        fixture,
                        eventIdentity,
                        FootballMarketType.Moneyline,
                        yesNo == ContractSelection.Yes ? subject : ComplementMoneyline(subject),
                        TeamForMoneyline(subject),
                        label: MoneylineLabel(yesNo == ContractSelection.Yes ? subject : ComplementMoneyline(subject), fixture)), marketRejects)
                    : RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.OutcomeSetMismatch, "Binary moneyline outcomes must be Yes/No.", marketRejects));
            }

            return market.Tokens.Select(token => RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.UnsupportedBinaryMoneyline, "Binary soccer moneyline needs a home/draw/away subject in sportsMarketType, question, slug, or token outcome.", marketRejects));
        }

        private IEnumerable<FootballContractTokenMapping> MapBtts(
            PolymarketMarketSnapshot market,
            FootballFixtureMappingContext fixture,
            PolymarketEventIdentity? eventIdentity,
            IReadOnlyList<FootballContractMapReject> marketRejects) =>
            market.Tokens.Select(token => YesNoSelection(token.Outcome) is { } selection
                ? MapToken(token, Contract(market, fixture, eventIdentity, FootballMarketType.BothTeamsToScore, selection, label: $"BTTS {selection}"), marketRejects)
                : RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.OutcomeSetMismatch, "BTTS outcomes must be Yes/No.", marketRejects));

        private IEnumerable<FootballContractTokenMapping> MapSpread(
            PolymarketMarketSnapshot market,
            FootballFixtureMappingContext fixture,
            PolymarketEventIdentity? eventIdentity,
            IReadOnlyList<FootballContractMapReject> marketRejects,
            string text)
        {
            if (!TryParseSpreadContext(market, text, fixture, out var team, out var line, out var reject))
            {
                var reason = reject ?? FootballContractMapRejectReason.UnsupportedMarketType;
                return market.Tokens.Select(token => RejectToken(token.Outcome, token.TokenId, reason, "Spread subject or line is unsupported.", marketRejects, text));
            }

            if (line == decimal.Truncate(line))
                return market.Tokens.Select(token => RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.IntegerLineUnsupported, "Integer spread lines need push/settlement handling.", marketRejects));

            var spreadTeam = team ?? throw new InvalidOperationException("Spread parser succeeded without a team side.");

            return market.Tokens.Select(token => SpreadSelection(token.Outcome, spreadTeam, fixture) is { } selection
                ? MapToken(token, Contract(
                    market,
                    fixture,
                    eventIdentity,
                    FootballMarketType.Spread,
                    selection,
                    spreadTeam,
                    line,
                    label: SpreadLabel(spreadTeam, selection, line, fixture)), marketRejects)
                : RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.OutcomeSetMismatch, "Spread outcomes must be Yes/No or the selected/opposing team.", marketRejects));
        }

        private IEnumerable<FootballContractTokenMapping> MapTotalTokens(
            PolymarketMarketSnapshot market,
            FootballFixtureMappingContext fixture,
            PolymarketEventIdentity? eventIdentity,
            IReadOnlyList<FootballContractMapReject> marketRejects,
            FootballMarketType marketType,
            TeamSide? team,
            decimal line,
            string text)
        {
            if (line < 0)
                return market.Tokens.Select(token => RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.NegativeLine, "Total line is negative.", marketRejects));
            if (line == decimal.Truncate(line))
                return market.Tokens.Select(token => RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.IntegerLineUnsupported, "Integer total lines need push/settlement handling.", marketRejects));

            return market.Tokens.Select(token => TotalSelection(token.Outcome, text) is { } selection
                ? MapToken(token, Contract(
                    market,
                    fixture,
                    eventIdentity,
                    marketType,
                    selection,
                    team,
                    line,
                    label: TotalLabel(marketType, team, selection, line, fixture)), marketRejects)
                : RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.OutcomeSetMismatch, "Total market outcomes must map to Over/Under or Yes/No question side.", marketRejects));
        }

        private IEnumerable<FootballContractTokenMapping> MapExactScore(
            PolymarketMarketSnapshot market,
            FootballFixtureMappingContext fixture,
            PolymarketEventIdentity? eventIdentity,
            IReadOnlyList<FootballContractMapReject> marketRejects,
            string text)
        {
            var questionScore = ParseOrientedScore(text, fixture);
            return market.Tokens.Select(token =>
            {
                if (OtherExactScoreRegex.IsMatch(token.Outcome))
                    return RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.UnsupportedExactScoreOther, "Exact-score Other bucket is unsupported in v1.", marketRejects);

                var tokenScore = ParseOrientedScore(token.Outcome, fixture);
                var score = tokenScore ?? questionScore;
                if (score is null)
                    return RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.MissingExactScore, "No exact score found in question or outcome.", marketRejects);

                var selection = tokenScore is not null
                    ? ContractSelection.Yes
                    : YesNoSelection(token.Outcome);
                if (selection is null)
                    return RejectToken(token.Outcome, token.TokenId, FootballContractMapRejectReason.OutcomeSetMismatch, "Binary exact-score outcomes must be Yes/No.", marketRejects);

                return MapToken(token, Contract(
                    market,
                    fixture,
                    eventIdentity,
                    FootballMarketType.ExactScore,
                    selection.Value,
                    exactHomeGoals: score.Value.Home,
                    exactAwayGoals: score.Value.Away,
                    label: $"Exact Score {score.Value.Home}-{score.Value.Away} {selection.Value}"), marketRejects);
            });
        }

        private static FootballContractMapReject[] MarketRejects(PolymarketMarketSnapshot market)
        {
            var rejects = market.RejectReasons
                .Select(reason => new FootballContractMapReject(FootballContractMapRejectReason.SourceMarketRejected, $"Source market reject: {reason}"))
                .ToList();
            if (string.IsNullOrWhiteSpace(market.ConditionId))
                rejects.Add(new FootballContractMapReject(FootballContractMapRejectReason.MissingConditionId, "Market is missing condition id."));
            return rejects.ToArray();
        }

        private static FootballContractTokenMapping MapToken(PolymarketOutcomeToken token, FootballContract contractWithoutToken, IReadOnlyList<FootballContractMapReject> marketRejects)
        {
            var rejects = new List<FootballContractMapReject>(marketRejects);
            if (string.IsNullOrWhiteSpace(token.TokenId))
                rejects.Add(new FootballContractMapReject(FootballContractMapRejectReason.MissingTokenId, "Outcome is missing CLOB token id.", token.Outcome, token.TokenId));

            var contract = contractWithoutToken with
            {
                Identity = contractWithoutToken.Identity with
                {
                    TokenId = token.TokenId,
                    OutcomeSide = token.Outcome
                }
            };

            return new FootballContractTokenMapping(token.Outcome, token.TokenId, string.IsNullOrWhiteSpace(token.TokenId) ? null : contract, rejects);
        }

        private static FootballContractTokenMapping RejectToken(
            string outcome,
            string? tokenId,
            FootballContractMapRejectReason reason,
            string detail,
            IReadOnlyList<FootballContractMapReject> marketRejects,
            string? evidence = null)
        {
            var rejects = new List<FootballContractMapReject>(marketRejects)
            {
                new(reason, detail, outcome, tokenId, evidence)
            };
            return new FootballContractTokenMapping(outcome, tokenId, null, rejects);
        }

        private static FootballContract Contract(
            PolymarketMarketSnapshot market,
            FootballFixtureMappingContext fixture,
            PolymarketEventIdentity? eventIdentity,
            FootballMarketType marketType,
            ContractSelection selection,
            TeamSide? team = null,
            decimal? line = null,
            int? exactHomeGoals = null,
            int? exactAwayGoals = null,
            string label = "") => new()
            {
                FixtureId = fixture.FixtureId,
                HomeTeamId = fixture.HomeTeamId,
                AwayTeamId = fixture.AwayTeamId,
                Identity = new PolymarketContractIdentity(
                    EventSlug: eventIdentity?.EventSlug,
                    EventId: eventIdentity?.EventId,
                    MarketId: market.MarketId,
                    ConditionId: market.ConditionId,
                    TokenId: null,
                    OutcomeSide: null,
                    Question: market.Question,
                    ResolutionRules: market.Description),
                MarketType = marketType,
                Selection = selection,
                Period = PeriodScope.Regulation90,
                Team = team,
                Line = line,
                ExactHomeGoals = exactHomeGoals,
                ExactAwayGoals = exactAwayGoals,
                Label = label
            };

        private static FootballContract Contract(
            PolymarketMarketSnapshot market,
            FootballFixtureMappingContext fixture,
            PolymarketEventIdentity? eventIdentity,
            FootballMarketType marketType,
            ContractSelection selection,
            PolymarketOutcomeToken token,
            TeamSide? team = null,
            decimal? line = null,
            int? exactHomeGoals = null,
            int? exactAwayGoals = null,
            string label = "") => Contract(market, fixture, eventIdentity, marketType, selection, team, line, exactHomeGoals, exactAwayGoals, label) with
            {
                Identity = new PolymarketContractIdentity(
                    EventSlug: eventIdentity?.EventSlug,
                    EventId: eventIdentity?.EventId,
                    MarketId: market.MarketId,
                    ConditionId: market.ConditionId,
                    TokenId: token.TokenId,
                    OutcomeSide: token.Outcome,
                    Question: market.Question,
                    ResolutionRules: market.Description)
            };

        private static string MarketText(PolymarketMarketSnapshot market) =>
            string.Join(' ', new[] { market.SportsMarketType, market.MarketType, market.Question, market.Slug?.Replace('-', ' '), market.Description, market.Line?.ToString(CultureInfo.InvariantCulture) }.Where(value => !string.IsNullOrWhiteSpace(value)));

        private static bool LooksLikeMoneyline(PolymarketMarketSnapshot market, string text) =>
            MoneylineRegex.IsMatch(text) || market.Tokens.Any(token => NormalizeTeamId(token.Outcome) is "draw" or "tie") ||
            (market.Tokens.Any(token => NormalizeTeamId(token.Outcome) == NormalizeTeamId(text)) == false && market.MarketType?.Contains("moneyline", StringComparison.OrdinalIgnoreCase) == true);

        private static bool LooksLikeSpread(string text) =>
            Regex.IsMatch(text, @"\b(spread|handicap)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static bool IsExactScore(PolymarketMarketSnapshot market, string text) =>
            ExactScoreRegex.IsMatch(text) || market.Tokens.Any(token => ScoreRegex.IsMatch(token.Outcome));

        private static ContractSelection? MoneylineSelection(string outcome, FootballFixtureMappingContext fixture)
        {
            var id = NormalizeTeamId(outcome);
            if (id == NormalizeTeamId(fixture.HomeTeamName) || id == fixture.HomeTeamId)
                return ContractSelection.Home;
            if (id is "draw" or "tie")
                return ContractSelection.Draw;
            if (id == NormalizeTeamId(fixture.AwayTeamName) || id == fixture.AwayTeamId)
                return ContractSelection.Away;
            return null;
        }

        private static ContractSelection? YesNoSelection(string outcome)
        {
            var normalized = NormalizeWords(outcome);
            return normalized switch
            {
                "yes" => ContractSelection.Yes,
                "no" => ContractSelection.No,
                _ => null
            };
        }

        private static ContractSelection ComplementMoneyline(ContractSelection subject) => subject switch
        {
            ContractSelection.Home => ContractSelection.NotHome,
            ContractSelection.Draw => ContractSelection.NotDraw,
            ContractSelection.Away => ContractSelection.NotAway,
            _ => throw new ArgumentOutOfRangeException(nameof(subject), "Moneyline subject must be Home, Draw, or Away.")
        };

        private static TeamSide? TeamForMoneyline(ContractSelection selection) => selection switch
        {
            ContractSelection.Home or ContractSelection.NotHome => TeamSide.Home,
            ContractSelection.Away or ContractSelection.NotAway => TeamSide.Away,
            _ => null
        };

        private static bool TryParseBinaryMoneylineSubject(PolymarketMarketSnapshot market, FootballFixtureMappingContext fixture, out ContractSelection subject)
        {
            subject = default;
            var slug = market.Slug ?? string.Empty;
            if (Regex.IsMatch(slug, @"(?:^|-)draw(?:-|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                subject = ContractSelection.Draw;
                return true;
            }

            if (SlugEndsWith(slug, fixture.HomeTeamId) || Regex.IsMatch(slug, @"(?:^|-)home(?:-|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                subject = ContractSelection.Home;
                return true;
            }

            if (SlugEndsWith(slug, fixture.AwayTeamId) || Regex.IsMatch(slug, @"(?:^|-)away(?:-|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                subject = ContractSelection.Away;
                return true;
            }

            var question = market.Question ?? string.Empty;
            if (QuestionNamesSubject(question, fixture.HomeTeamName))
            {
                subject = ContractSelection.Home;
                return true;
            }

            if (QuestionNamesSubject(question, fixture.AwayTeamName))
            {
                subject = ContractSelection.Away;
                return true;
            }

            if (Regex.IsMatch(question, @"\bdraw\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                subject = ContractSelection.Draw;
                return true;
            }

            return false;
        }

        private static bool SlugEndsWith(string slug, string id)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(id))
                return false;
            var normalizedId = NormalizeTeamId(id);
            return Regex.IsMatch(slug, $@"(?:^|-){Regex.Escape(normalizedId)}(?:-|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                   Regex.IsMatch(slug, $@"(?:^|-){Regex.Escape(normalizedId)}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool QuestionNamesSubject(string question, string teamName)
        {
            if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(teamName))
                return false;
            var team = TeamPattern(teamName);
            return Regex.IsMatch(question, $@"(?:^|[:\-])\s*{team}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                   Regex.IsMatch(question, $@"\b{team}\b\s+(?:moneyline|to\s+win|wins?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static ContractSelection? SpreadSelection(string outcome, TeamSide team, FootballFixtureMappingContext fixture)
        {
            if (YesNoSelection(outcome) is { } yesNo)
                return yesNo;

            var moneyline = MoneylineSelection(outcome, fixture);
            if (moneyline == ContractSelection.Home)
                return team == TeamSide.Home ? ContractSelection.Yes : ContractSelection.No;
            if (moneyline == ContractSelection.Away)
                return team == TeamSide.Away ? ContractSelection.Yes : ContractSelection.No;
            return null;
        }

        private static ContractSelection? TotalSelection(string outcome, string questionText)
        {
            var normalized = NormalizeWords(outcome);
            if (normalized.StartsWith("over", StringComparison.Ordinal))
                return ContractSelection.Over;
            if (normalized.StartsWith("under", StringComparison.Ordinal))
                return ContractSelection.Under;

            var yesNo = YesNoSelection(outcome);
            if (yesNo is null)
                return null;

            var questionSide = Regex.IsMatch(questionText, @"\bover\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                ? ContractSelection.Over
                : Regex.IsMatch(questionText, @"\bunder\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    ? ContractSelection.Under
                    : (ContractSelection?)null;
            if (questionSide is null)
                return null;
            return yesNo == ContractSelection.Yes
                ? questionSide
                : questionSide == ContractSelection.Over ? ContractSelection.Under : ContractSelection.Over;
        }

        private static bool TryParseMatchTotalLine(PolymarketMarketSnapshot market, string text, out decimal line, out FootballContractMapRejectReason? reject)
        {
            reject = null;
            line = 0;
            if (!Regex.IsMatch(text, @"\b(total\s+goals?|goals?\s+total|over\s*/\s*under|\bo/u\b|\bover\b|\bunder\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return false;
            if (LooksLikeTeamTotalText(text, null))
                return false;
            if (!TryFindLine(market, text, out line))
            {
                reject = FootballContractMapRejectReason.MissingLine;
                return false;
            }
            if (line < 0)
                reject = FootballContractMapRejectReason.NegativeLine;
            else if (line == decimal.Truncate(line))
                reject = FootballContractMapRejectReason.IntegerLineUnsupported;
            return reject is null;
        }

        private static bool TryParseSpreadContext(PolymarketMarketSnapshot market, string text, FootballFixtureMappingContext fixture, out TeamSide? team, out decimal line, out FootballContractMapRejectReason? reject)
        {
            team = null;
            line = 0;
            reject = null;
            if (!LooksLikeSpread(text))
                return false;

            var slug = market.Slug ?? string.Empty;
            var home = Regex.IsMatch(slug, @"(?:^|-)home(?:-|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) || TextNamesTeam(text, fixture.HomeTeamName);
            var away = Regex.IsMatch(slug, @"(?:^|-)away(?:-|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) || TextNamesTeam(text, fixture.AwayTeamName);
            if (home && away)
            {
                reject = FootballContractMapRejectReason.AmbiguousTeam;
                return false;
            }

            if (!home && !away)
            {
                reject = FootballContractMapRejectReason.OutcomeTeamMismatch;
                return false;
            }

            if (!TryFindLine(market, text, out line))
            {
                reject = FootballContractMapRejectReason.MissingLine;
                return false;
            }

            team = home ? TeamSide.Home : TeamSide.Away;
            return true;
        }

        private static bool TryParseTeamTotalContext(PolymarketMarketSnapshot market, string text, FootballFixtureMappingContext fixture, out TeamSide? team, out decimal line, out FootballContractMapRejectReason? reject)
        {
            team = null;
            line = 0;
            reject = null;
            var home = LooksLikeTeamTotalText(text, fixture.HomeTeamName);
            var away = LooksLikeTeamTotalText(text, fixture.AwayTeamName);
            if (!home && !away)
                return false;
            if (home && away)
            {
                reject = FootballContractMapRejectReason.AmbiguousTeam;
                return false;
            }
            if (!TryFindLine(market, text, out line))
            {
                reject = FootballContractMapRejectReason.MissingLine;
                return false;
            }
            if (line < 0)
                reject = FootballContractMapRejectReason.NegativeLine;
            else if (line == decimal.Truncate(line))
                reject = FootballContractMapRejectReason.IntegerLineUnsupported;
            if (reject is not null)
                return false;

            team = home ? TeamSide.Home : TeamSide.Away;
            return true;
        }

        private static bool TextNamesTeam(string text, string teamName)
        {
            if (string.IsNullOrWhiteSpace(teamName))
                return false;
            return Regex.IsMatch(text, $@"\b{TeamPattern(teamName)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool LooksLikeTeamTotalText(string text, string? teamName)
        {
            if (string.IsNullOrWhiteSpace(teamName))
                return Regex.IsMatch(text, @"\b(team\s+total|team\s+goals?|to\s+score)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            var team = TeamPattern(teamName);
            return Regex.IsMatch(text, $@"\b{team}\b\s+(?:team\s+total|total\s+goals?|goals?\s+total|to\s+score|scores?|score)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                   Regex.IsMatch(text, $@"\b{team}\b.{{0,30}}\b(?:to\s+score|scores?|score)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                   Regex.IsMatch(text, $@"\b(?:team\s+total|goals?\s+(?:by|for|from)|goals?\s+scored\s+by)\b.{{0,30}}\b{team}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool TryFindLine(PolymarketMarketSnapshot market, string text, out decimal line)
        {
            line = 0;
            if (market.Line.HasValue)
            {
                line = market.Line.Value;
                return true;
            }

            var signed = Regex.Match(text, @"(?<line>[+-]?\d+(?:\.\d+)?)\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (signed.Success && decimal.TryParse(signed.Groups["line"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out line))
                return true;

            var pointSlug = Regex.Match(text, @"(?<!\d)(?<whole>\d+)pt(?<fraction>\d+)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (pointSlug.Success && decimal.TryParse($"{pointSlug.Groups["whole"].Value}.{pointSlug.Groups["fraction"].Value}", NumberStyles.Number, CultureInfo.InvariantCulture, out line))
                return true;

            var contextual = Regex.Match(text, @"(?:over|under|total|spread|handicap)[^0-9+-]{0,24}(?<line>[+-]?\d+(?:\.\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (contextual.Success && decimal.TryParse(contextual.Groups["line"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out line))
                return true;

            var match = Regex.Match(text, @"(?<!\d)(?<line>\d+(?:\.\d+)?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success && decimal.TryParse(match.Groups["line"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out line);
        }

        private static (int Home, int Away)? ParseOrientedScore(string text, FootballFixtureMappingContext fixture)
        {
            var home = TeamPattern(fixture.HomeTeamName);
            var away = TeamPattern(fixture.AwayTeamName);
            var homeAway = Regex.Match(text, $@"\b{home}\b.*?(?<home>\d{{1,2}})\s*[-:]\s*(?<away>\d{{1,2}}).*?\b{away}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (homeAway.Success)
                return (int.Parse(homeAway.Groups["home"].Value, CultureInfo.InvariantCulture), int.Parse(homeAway.Groups["away"].Value, CultureInfo.InvariantCulture));

            var awayHome = Regex.Match(text, $@"\b{away}\b.*?(?<away>\d{{1,2}})\s*[-:]\s*(?<home>\d{{1,2}}).*?\b{home}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (awayHome.Success)
                return (int.Parse(awayHome.Groups["home"].Value, CultureInfo.InvariantCulture), int.Parse(awayHome.Groups["away"].Value, CultureInfo.InvariantCulture));

            var score = ScoreRegex.Match(text);
            return score.Success
                ? (int.Parse(score.Groups["home"].Value, CultureInfo.InvariantCulture), int.Parse(score.Groups["away"].Value, CultureInfo.InvariantCulture))
                : null;
        }

        private static string TotalLabel(FootballMarketType marketType, TeamSide? team, ContractSelection selection, decimal line, FootballFixtureMappingContext fixture) =>
            marketType == FootballMarketType.MatchTotal
                ? $"Match Total {selection} {line:0.##}"
                : $"{(team == TeamSide.Home ? fixture.HomeTeamName : fixture.AwayTeamName)} Team Total {selection} {line:0.##}";

        private static string SpreadLabel(TeamSide team, ContractSelection selection, decimal line, FootballFixtureMappingContext fixture) =>
            $"{(team == TeamSide.Home ? fixture.HomeTeamName : fixture.AwayTeamName)} Spread {line:+0.##;-0.##;0} {selection}";

        private static string MoneylineLabel(ContractSelection selection, FootballFixtureMappingContext fixture) => selection switch
        {
            ContractSelection.Home => $"{fixture.HomeTeamName} ML",
            ContractSelection.NotHome => $"Not {fixture.HomeTeamName} ML",
            ContractSelection.Draw => "Draw",
            ContractSelection.NotDraw => "Not Draw",
            ContractSelection.Away => $"{fixture.AwayTeamName} ML",
            ContractSelection.NotAway => $"Not {fixture.AwayTeamName} ML",
            _ => selection.ToString()
        };

        private static string TeamPattern(string teamName) => Regex.Escape(teamName).Replace("\\ ", @"\s+");

        private static string NormalizeTeamId(string value) => TeamNameNormalizer.ToId(value);

        private static string NormalizeWords(string value) => Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
    }
}
