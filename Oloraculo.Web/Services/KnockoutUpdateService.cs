using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Predictors;
using Oloraculo.Web.Services.Simulation;

namespace Oloraculo.Web.Services;

public class KnockoutUpdateService
{
    private readonly OloraculoDbContext _db;
    private readonly ITournamentFixtureSource _source;
    private readonly PredictionService _predictions;
    private readonly SnapshotService _snapshots;

    public KnockoutUpdateService(
        OloraculoDbContext db,
        ITournamentFixtureSource source,
        PredictionService predictions,
        SnapshotService snapshots)
    {
        _db = db;
        _source = source;
        _predictions = predictions;
        _snapshots = snapshots;
    }

    public async Task<KnockoutRefreshReport> RefreshAsync(CancellationToken ct = default)
    {
        var feed = await _source.FetchTournamentFixturesAsync(ct);
        var warnings = feed.Errors.ToList();
        var applied = 0;
        if (feed.IsConfigured && feed.Errors.Count == 0)
            applied = await ApplyFeedAsync(feed.Fixtures, warnings, ct);

        var board = await BuildBoardAsync(feed.IsConfigured && feed.Errors.Count == 0, warnings, ct);
        await _snapshots.SaveKnockoutBoardAsync(board, ct);
        return new KnockoutRefreshReport
        {
            Board = board,
            FixturesFetched = feed.Fixtures.Count,
            FixturesApplied = applied,
            Notes = [$"Partidos recibidos: {feed.Fixtures.Count}; aplicados al cuadro: {applied}."],
            Errors = feed.Errors
        };
    }

    public async Task<KnockoutBoard> BuildBoardAsync(
        bool sourceRefreshSucceeded = false,
        IReadOnlyList<string>? refreshWarnings = null,
        CancellationToken ct = default)
    {
        var warnings = refreshWarnings?.ToList() ?? [];
        var names = await _db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var predictors = await _predictions.BuildPredictorsAsync(ct);
        var groupSlots = await ProjectGroupSlotsAsync(predictors, ct);
        var thirdAssignments = WorldCup2026Bracket.AssignThirdPlaceGroups(groupSlots.BestThirds.Select(t => t.Group).ToList());
        var persisted = await _db.KnockoutMatches.AsNoTracking().ToDictionaryAsync(m => m.MatchNumber, ct);
        var winners = new Dictionary<int, string>();
        var losers = new Dictionary<int, string>();
        var views = new List<KnockoutMatchView>(32);
        var now = DateTimeOffset.UtcNow;

        foreach (var tie in WorldCup2026Bracket.KnockoutTies)
        {
            persisted.TryGetValue(tie.Id, out var actual);
            var derivedHome = Resolve(tie, tie.Home);
            var derivedAway = Resolve(tie, tie.Away);
            var home = actual?.ConfirmedHomeTeamId ?? derivedHome;
            var away = actual?.ConfirmedAwayTeamId ?? derivedAway;
            var homeResolution = actual?.ConfirmedHomeTeamId is not null ? ParticipantResolution.Confirmed
                : home is not null ? ParticipantResolution.Projected : ParticipantResolution.Tbd;
            var awayResolution = actual?.ConfirmedAwayTeamId is not null ? ParticipantResolution.Confirmed
                : away is not null ? ParticipantResolution.Projected : ParticipantResolution.Tbd;

            MatchPredictionResult? predictionResult = null;
            DateTimeOffset? predictionAt = null;
            var predictionUnavailable = false;
            if (home is not null && away is not null)
            {
                var cutoffReached = actual?.IsPlayed == true ||
                    actual?.KickoffUtc is { } kickoff && kickoff <= now ||
                    IsLiveStatus(actual?.Status);
                if (cutoffReached)
                {
                    if (actual?.KickoffUtc is { } cutoff)
                        (predictionResult, predictionAt) = await _snapshots.LoadPregamePredictionAsync(
                            FixtureId(tie.Id), home, away, cutoff, ct);
                    predictionUnavailable = predictionResult is null;
                }
                else
                {
                    var fixture = new Fixture
                    {
                        Id = FixtureId(tie.Id),
                        Group = StageLabel(tie.Stage),
                        HomeTeamId = home,
                        AwayTeamId = away,
                        KickoffUtc = actual?.KickoffUtc,
                        Venue = actual?.Venue,
                        City = actual?.City,
                        NeutralVenue = true,
                        Source = actual?.Source ?? "projection"
                    };
                    predictionResult = await _predictions.PredictAsync(fixture, predictors, ct);
                    var saved = await _snapshots.SaveMatchAsync(predictionResult.BestPrediction, ct);
                    predictionAt = saved.CreatedAt;
                }
            }

            var score = predictionResult is null ? ((int Home, int Away)?)null : PredictionScore(predictionResult);
            var predictedWinner = predictionResult is null ? null : PredictionWinner(predictionResult, score!.Value);
            var winner = actual?.IsPlayed == true ? actual.WinnerTeamId : predictedWinner;
            if (actual?.IsPlayed == true && winner is null)
                warnings.Add($"El partido {tie.Id} terminó pero el feed no informó un ganador utilizable.");
            if (winner is not null && home is not null && away is not null)
            {
                winners[tie.Id] = winner;
                losers[tie.Id] = winner == home ? away : home;
            }

            views.Add(new KnockoutMatchView
            {
                MatchNumber = tie.Id,
                Stage = tie.Stage,
                KickoffUtc = actual?.KickoffUtc,
                Venue = actual?.Venue,
                City = actual?.City,
                Status = actual?.Status,
                HomeTeamId = home,
                HomeTeamName = Name(home),
                HomeResolution = homeResolution,
                AwayTeamId = away,
                AwayTeamName = Name(away),
                AwayResolution = awayResolution,
                PredictedHomeGoals = score?.Home,
                PredictedAwayGoals = score?.Away,
                PredictedWinnerTeamId = predictedWinner,
                Probabilities = predictionResult?.BestPrediction.Outcome,
                PredictionCreatedAt = predictionAt,
                PredictionUnavailable = predictionUnavailable,
                IsPlayed = actual?.IsPlayed == true,
                HomeGoals = actual?.HomeGoals,
                AwayGoals = actual?.AwayGoals,
                HomePenaltyGoals = actual?.HomePenaltyGoals,
                AwayPenaltyGoals = actual?.AwayPenaltyGoals,
                WinnerTeamId = actual?.WinnerTeamId
            });
        }

        return new KnockoutBoard
        {
            GeneratedAt = now,
            SourceRefreshSucceeded = sourceRefreshSucceeded,
            Warnings = warnings.Distinct().ToList(),
            Matches = views
        };

        string? Resolve(SimulationService.BracketTie tie, SimulationService.BracketSlot slot) => slot.Kind switch
        {
            BracketSlotKindEnum.GroupWinner => groupSlots.Groups.GetValueOrDefault(slot.Group!)?.Winner,
            BracketSlotKindEnum.GroupRunnerUp => groupSlots.Groups.GetValueOrDefault(slot.Group!)?.RunnerUp,
            BracketSlotKindEnum.GroupThird => groupSlots.ThirdByGroup.GetValueOrDefault(thirdAssignments[tie.Id]),
            BracketSlotKindEnum.WinnerOfTie => winners.GetValueOrDefault(slot.TieId!.Value),
            BracketSlotKindEnum.LoserOfTie => losers.GetValueOrDefault(slot.TieId!.Value),
            _ => null
        };

        string? Name(string? id) => id is null ? null : names.GetValueOrDefault(id, id);
    }

    private async Task<int> ApplyFeedAsync(
        IReadOnlyList<TournamentFixtureFeedRow> feed,
        List<string> warnings,
        CancellationToken ct)
    {
        var applied = 0;
        var groupFixtures = await _db.Fixtures.ToListAsync(ct);
        var groupByPair = groupFixtures.ToDictionary(f => PairKey(f.HomeTeamId, f.AwayTeamId));
        foreach (var row in feed.Where(r => ParseStage(r.Round) is null))
        {
            if (row.HomeTeamId is null || row.AwayTeamId is null ||
                !groupByPair.TryGetValue(PairKey(row.HomeTeamId, row.AwayTeamId), out var fixture))
                continue;
            ApplyGroupFixture(fixture, row);
            if (row.IsFinished)
                UpsertResult(row);
        }

        var knockoutRows = feed
            .Select(row => (Row: row, Stage: ParseStage(row.Round)))
            .Where(x => x.Stage.HasValue)
            .ToList();
        var matches = await _db.KnockoutMatches.ToDictionaryAsync(m => m.MatchNumber, ct);
        var mappedExternalIds = matches.Values.Where(m => m.ExternalFixtureId is not null)
            .ToDictionary(m => m.ExternalFixtureId!, m => m.MatchNumber, StringComparer.Ordinal);

        var stageOrder = new[]
        {
            KnockoutStageEnum.RoundOf32,
            KnockoutStageEnum.RoundOf16,
            KnockoutStageEnum.QuarterFinal,
            KnockoutStageEnum.SemiFinal,
            KnockoutStageEnum.ThirdPlace,
            KnockoutStageEnum.Final
        };
        foreach (var stage in stageOrder)
        {
            var definitions = WorldCup2026Bracket.ForStage(stage).OrderBy(t => t.Id).ToList();
            var rows = knockoutRows.Where(x => x.Stage == stage).Select(x => x.Row)
                .OrderBy(r => r.KickoffUtc).ThenBy(r => r.ExternalFixtureId).ToList();
            if (rows.Count == 0)
                continue;

            var stageMap = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var existing = mappedExternalIds.GetValueOrDefault(row.ExternalFixtureId);
                if (existing != 0)
                {
                    stageMap[row.ExternalFixtureId] = existing;
                    continue;
                }

                var scheduleCandidates = definitions
                    .Where(definition => CanClaim(definition.Id, row.ExternalFixtureId) && OfficialScheduleMatches(definition.Id, row))
                    .ToList();
                if (scheduleCandidates.Count == 1)
                {
                    stageMap[row.ExternalFixtureId] = scheduleCandidates[0].Id;
                    continue;
                }

                var confirmedPairCandidates = definitions
                    .Where(definition => CanClaim(definition.Id, row.ExternalFixtureId) &&
                        MatchesPair(matches[definition.Id].ConfirmedHomeTeamId, matches[definition.Id].ConfirmedAwayTeamId, row))
                    .ToList();
                if (confirmedPairCandidates.Count == 1)
                {
                    stageMap[row.ExternalFixtureId] = confirmedPairCandidates[0].Id;
                    continue;
                }

                var bracketPairCandidates = definitions
                    .Where(definition => CanClaim(definition.Id, row.ExternalFixtureId) && MatchesDerivedPair(definition, row))
                    .ToList();
                if (bracketPairCandidates.Count == 1)
                    stageMap[row.ExternalFixtureId] = bracketPairCandidates[0].Id;
            }
            if (rows.Count != definitions.Count)
                warnings.Add($"API-Football devolvió {rows.Count}/{definitions.Count} partidos para {StageLabel(stage)}; se aplicarán únicamente asociaciones inequívocas.");

            foreach (var row in rows)
            {
                var matchNumber = mappedExternalIds.GetValueOrDefault(row.ExternalFixtureId);
                if (matchNumber == 0)
                    matchNumber = stageMap.GetValueOrDefault(row.ExternalFixtureId);
                if (matchNumber == 0 || !matches.TryGetValue(matchNumber, out var match))
                {
                    warnings.Add($"No se pudo asociar el fixture externo {row.ExternalFixtureId} " +
                        $"({row.Round}: {row.HomeTeamId ?? "TBD"} vs {row.AwayTeamId ?? "TBD"}) a un número oficial.");
                    continue;
                }
                if (!CanClaim(matchNumber, row.ExternalFixtureId))
                {
                    warnings.Add($"El partido oficial {matchNumber} ya está asociado a otro fixture externo; se ignoró {row.ExternalFixtureId}.");
                    continue;
                }

                match.ExternalFixtureId = row.ExternalFixtureId;
                match.KickoffUtc = row.KickoffUtc;
                match.Venue = row.Venue;
                match.City = row.City;
                match.Status = row.Status;
                if (row.HomeTeamId is not null) match.ConfirmedHomeTeamId = row.HomeTeamId;
                if (row.AwayTeamId is not null) match.ConfirmedAwayTeamId = row.AwayTeamId;
                if (row.IsFinished)
                {
                    match.HomeGoals = row.HomeGoals;
                    match.AwayGoals = row.AwayGoals;
                    match.HomePenaltyGoals = row.HomePenaltyGoals;
                    match.AwayPenaltyGoals = row.AwayPenaltyGoals;
                    match.WinnerTeamId = row.WinnerTeamId;
                    match.IsPlayed = true;
                }
                match.Source = "API-Football";
                match.SourceUpdatedAt = DateTimeOffset.UtcNow;
                mappedExternalIds[row.ExternalFixtureId] = matchNumber;
                if (row.IsFinished)
                    UpsertResult(row);
                applied++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return applied;

        void ApplyGroupFixture(Fixture fixture, TournamentFixtureFeedRow row)
        {
            fixture.KickoffUtc = row.KickoffUtc;
            fixture.Venue = row.Venue;
            fixture.City = row.City;
            fixture.Status = row.Status;
            fixture.Source = "API-Football";
            if (!row.IsFinished || !row.HomeGoals.HasValue || !row.AwayGoals.HasValue)
                return;
            fixture.IsPlayed = true;
            var sameOrder = fixture.HomeTeamId == row.HomeTeamId;
            fixture.HomeGoals = sameOrder ? row.HomeGoals : row.AwayGoals;
            fixture.AwayGoals = sameOrder ? row.AwayGoals : row.HomeGoals;
        }

        void UpsertResult(TournamentFixtureFeedRow row)
        {
            if (row.HomeTeamId is null || row.AwayTeamId is null ||
                !row.HomeGoals.HasValue || !row.AwayGoals.HasValue || !row.KickoffUtc.HasValue)
                return;
            var id = $"api-football:{row.ExternalFixtureId}";
            var result = _db.Results.Local.FirstOrDefault(r => r.Id == id) ?? _db.Results.Find(id);
            if (result is null)
            {
                result = new MatchResult { Id = id };
                _db.Results.Add(result);
            }
            result.HomeTeamId = row.HomeTeamId;
            result.AwayTeamId = row.AwayTeamId;
            result.HomeGoals = row.HomeGoals.Value;
            result.AwayGoals = row.AwayGoals.Value;
            result.Date = row.KickoffUtc.Value;
            result.Tournament = "FIFA World Cup 2026";
            result.Neutral = true;
            result.Source = "API-Football";
        }

        bool CanClaim(int matchNumber, string externalFixtureId) =>
            matches.TryGetValue(matchNumber, out var match) &&
            (match.ExternalFixtureId is null || string.Equals(match.ExternalFixtureId, externalFixtureId, StringComparison.Ordinal));

        bool MatchesDerivedPair(SimulationService.BracketTie definition, TournamentFixtureFeedRow row)
        {
            var home = ResolveAuthoritativeSlot(definition.Home);
            var away = ResolveAuthoritativeSlot(definition.Away);
            return MatchesPair(home, away, row);
        }

        string? ResolveAuthoritativeSlot(SimulationService.BracketSlot slot)
        {
            if (!slot.TieId.HasValue || !matches.TryGetValue(slot.TieId.Value, out var upstream) ||
                !upstream.IsPlayed || upstream.WinnerTeamId is null)
                return null;

            return slot.Kind switch
            {
                BracketSlotKindEnum.WinnerOfTie => upstream.WinnerTeamId,
                BracketSlotKindEnum.LoserOfTie when upstream.ConfirmedHomeTeamId is not null && upstream.ConfirmedAwayTeamId is not null =>
                    upstream.WinnerTeamId == upstream.ConfirmedHomeTeamId
                        ? upstream.ConfirmedAwayTeamId
                        : upstream.ConfirmedHomeTeamId,
                _ => null
            };
        }
    }

    private async Task<ProjectedGroupSlots> ProjectGroupSlotsAsync(IReadOnlyList<IPredictor> predictors, CancellationToken ct)
    {
        var groups = await _db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
        var fixtures = await _db.Fixtures.AsNoTracking().ToListAsync(ct);
        var fifa = (await _db.Ratings.AsNoTracking().Where(r => r.Type == RatingTypeEnum.Fifa).ToListAsync(ct))
            .GroupBy(r => r.TeamId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.AsOf).First().Value);
        var slots = new Dictionary<string, GroupSlots>(StringComparer.OrdinalIgnoreCase);
        var thirds = new List<GroupStanding>();

        foreach (var group in groups)
        {
            var table = new GroupTable(group, fifa);
            foreach (var fixture in fixtures.Where(f => f.Group.Equals(group.Name, StringComparison.OrdinalIgnoreCase)))
            {
                (int Home, int Away) score;
                var known = fixture is { IsPlayed: true, HomeGoals: int hg, AwayGoals: int ag };
                if (known)
                    score = (fixture.HomeGoals!.Value, fixture.AwayGoals!.Value);
                else
                    score = PredictionScore(await _predictions.PredictAsync(fixture, predictors, ct));
                table.AddMatch(new SimulatedMatch(group.Name, fixture.HomeTeamId, fixture.AwayTeamId, score.Home, score.Away, known));
            }
            var ranked = table.Rank();
            slots[group.Name] = new GroupSlots(ranked[0].TeamId, ranked[1].TeamId, ranked[2].TeamId);
            thirds.Add(ranked[2]);
        }

        var bestThirds = GroupTable.RankBestThirds(thirds, fifa).Take(8).ToList();
        return new ProjectedGroupSlots(
            slots,
            bestThirds,
            bestThirds.ToDictionary(t => t.Group, t => t.TeamId, StringComparer.OrdinalIgnoreCase));
    }

    internal static KnockoutStageEnum? ParseStage(string? round)
    {
        if (string.IsNullOrWhiteSpace(round)) return null;
        var value = round.ToLowerInvariant();
        if (value.Contains("round of 32") || value.Contains("1/16")) return KnockoutStageEnum.RoundOf32;
        if (value.Contains("round of 16") || value.Contains("1/8")) return KnockoutStageEnum.RoundOf16;
        if (value.Contains("quarter")) return KnockoutStageEnum.QuarterFinal;
        if (value.Contains("semi")) return KnockoutStageEnum.SemiFinal;
        if (value.Contains("third") || value.Contains("3rd") || value.Contains("bronze")) return KnockoutStageEnum.ThirdPlace;
        if (value.Trim() == "final" || value.EndsWith(" final")) return KnockoutStageEnum.Final;
        return null;
    }

    internal static string FixtureId(int matchNumber) => $"wc2026:match:{matchNumber}";

    private static (int Home, int Away) PredictionScore(MatchPredictionResult result)
    {
        if (PredictionScoreHelper.TryPreferredScore(result.BestPrediction, out var score))
            return score;
        return result.BestPrediction.Outcome.TopPick switch
        {
            "Home" => (1, 0),
            "Away" => (0, 1),
            _ => (0, 0)
        };
    }

    private static string PredictionWinner(MatchPredictionResult result, (int Home, int Away) score)
    {
        if (score.Home > score.Away) return result.Fixture.HomeTeamId;
        if (score.Away > score.Home) return result.Fixture.AwayTeamId;
        return result.BestPrediction.Outcome.HomeWin >= result.BestPrediction.Outcome.AwayWin
            ? result.Fixture.HomeTeamId
            : result.Fixture.AwayTeamId;
    }

    public static string StageLabel(KnockoutStageEnum stage) => stage switch
    {
        KnockoutStageEnum.RoundOf32 => "Dieciseisavos",
        KnockoutStageEnum.RoundOf16 => "Octavos de final",
        KnockoutStageEnum.QuarterFinal => "Cuartos de final",
        KnockoutStageEnum.SemiFinal => "Semifinales",
        KnockoutStageEnum.ThirdPlace => "Tercer puesto",
        KnockoutStageEnum.Final => "Final",
        _ => stage.ToString()
    };

    private static bool IsLiveStatus(string? status) => status is "1H" or "HT" or "2H" or "ET" or "BT" or "P" or "LIVE";
    private static string PairKey(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";

    private static bool OfficialScheduleMatches(int matchNumber, TournamentFixtureFeedRow row)
    {
        if (!WorldCup2026Bracket.OfficialSchedule.TryGetValue(matchNumber, out var official) || !row.KickoffUtc.HasValue)
            return false;
        var easternDate = DateOnly.FromDateTime(row.KickoffUtc.Value.ToOffset(TimeSpan.FromHours(-4)).DateTime);
        if (easternDate != official.Date)
            return false;
        var location = TeamNameNormalizer.ToId($"{row.Venue} {row.City}");
        return official.Location.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(hint => location.Contains(TeamNameNormalizer.ToId(hint), StringComparison.Ordinal));
    }

    private static bool MatchesPair(string? expectedHome, string? expectedAway, TournamentFixtureFeedRow row) =>
        expectedHome is not null && expectedAway is not null &&
        row.HomeTeamId is not null && row.AwayTeamId is not null &&
        PairKey(expectedHome, expectedAway) == PairKey(row.HomeTeamId, row.AwayTeamId);

    private sealed record GroupSlots(string Winner, string RunnerUp, string Third);
    private sealed record ProjectedGroupSlots(
        IReadOnlyDictionary<string, GroupSlots> Groups,
        IReadOnlyList<GroupStanding> BestThirds,
        IReadOnlyDictionary<string, string> ThirdByGroup);
}
