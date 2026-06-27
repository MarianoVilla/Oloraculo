using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Models;
using Oloraculo.Web.Services.Simulation;

namespace Oloraculo.Web.Services
{
    public sealed record ResolvedKnockoutTie(
        int Id,
        KnockoutStageEnum Stage,
        string? HomeTeamId,
        string? AwayTeamId,
        string HomeLabel,
        string AwayLabel,
        bool IsResolved,
        int? HomeGoals,
        int? AwayGoals,
        bool IsPlayed);

    public class KnockoutBracketService
    {
        private readonly OloraculoDbContext _db;
        private readonly OloraculoConfig _config;

        public KnockoutBracketService(OloraculoDbContext db, IOptions<OloraculoConfig> options)
        {
            _db = db;
            _config = options.Value;
        }

        public async Task<IReadOnlyList<ResolvedKnockoutTie>> ResolveAsync(CancellationToken ct = default)
        {
            var groups = await _db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
            var fixtures = await _db.Fixtures.AsNoTracking().ToListAsync(ct);
            var fifaPoints = (await _db.Ratings.AsNoTracking()
                .Where(r => r.Type == RatingTypeEnum.Fifa)
                .ToListAsync(ct))
                .GroupBy(r => r.TeamId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.AsOf).First().Value, StringComparer.OrdinalIgnoreCase);
            var teams = await _db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);

            var groupStageCutoff = new DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero);
            var knockoutPlayed = (await _db.Results
                .Where(r => r.Tournament == "FIFA World Cup")
                .AsNoTracking()
                .ToListAsync(ct))
                .Where(r => r.Date > groupStageCutoff)
                .ToList();

            // Compute actual group standings from played fixtures only
            var groupSlots = new Dictionary<string, (string Winner, string RunnerUp, string Third)>(StringComparer.OrdinalIgnoreCase);
            var allThirds = new List<GroupStanding>();

            foreach (var group in groups)
            {
                var table = new GroupTable(group, fifaPoints);
                foreach (var f in fixtures.Where(f => f.Group == group.Name && f.IsPlayed && f.HomeGoals.HasValue && f.AwayGoals.HasValue))
                    table.AddMatch(new SimulatedMatch(group.Name, f.HomeTeamId, f.AwayTeamId, f.HomeGoals!.Value, f.AwayGoals!.Value, true));

                var ranked = table.Rank();
                if (ranked.Count >= 3)
                {
                    groupSlots[group.Name] = (ranked[0].TeamId, ranked[1].TeamId, ranked[2].TeamId);
                    allThirds.Add(ranked[2]);
                }
            }

            // Assign thirds only if all 12 groups have enough data
            Dictionary<int, string>? thirdAssignments = null;
            if (allThirds.Count == 12)
            {
                try
                {
                    var best8 = GroupTable.RankBestThirds(allThirds, fifaPoints).Take(8).ToList();
                    thirdAssignments = (Dictionary<int, string>)WorldCup2026Bracket.AssignThirdPlaceGroups(best8.Select(t => t.Group).ToList());
                }
                catch { }
            }

            // Build knockout results lookup
            var knockoutResults = knockoutPlayed.ToDictionary(
                r => (r.HomeTeamId, r.AwayTeamId),
                r => (r.HomeGoals, r.AwayGoals));

            var result = new List<ResolvedKnockoutTie>();
            var tieWinners = new Dictionary<int, string?>();

            foreach (var tie in WorldCup2026Bracket.KnockoutTies)
            {
                var (homeId, homeLabel) = ResolveSlot(tie, tie.Home, groupSlots, thirdAssignments, tieWinners, teams);
                var (awayId, awayLabel) = ResolveSlot(tie, tie.Away, groupSlots, thirdAssignments, tieWinners, teams);

                bool isResolved = homeId is not null && awayId is not null;

                // Check for actual played result
                (int HomeGoals, int AwayGoals)? played = null;
                string? winner = null;
                if (homeId is not null && awayId is not null)
                {
                    if (knockoutResults.TryGetValue((homeId, awayId), out var r))
                        played = r;
                    else if (knockoutResults.TryGetValue((awayId, homeId), out var rFlipped))
                        played = (rFlipped.AwayGoals, rFlipped.HomeGoals);

                    if (played.HasValue)
                        winner = played.Value.HomeGoals > played.Value.AwayGoals ? homeId : played.Value.AwayGoals > played.Value.HomeGoals ? awayId : null;
                }

                tieWinners[tie.Id] = winner;

                result.Add(new ResolvedKnockoutTie(
                    Id: tie.Id,
                    Stage: tie.Stage,
                    HomeTeamId: homeId,
                    AwayTeamId: awayId,
                    HomeLabel: homeLabel,
                    AwayLabel: awayLabel,
                    IsResolved: isResolved,
                    HomeGoals: played?.HomeGoals,
                    AwayGoals: played?.AwayGoals,
                    IsPlayed: played.HasValue));
            }

            return result;
        }

        private static (string? Id, string Label) ResolveSlot(
            SimulationService.BracketTie tie,
            SimulationService.BracketSlot slot,
            IReadOnlyDictionary<string, (string Winner, string RunnerUp, string Third)> groupSlots,
            IReadOnlyDictionary<int, string>? thirdAssignments,
            IReadOnlyDictionary<int, string?> tieWinners,
            IReadOnlyDictionary<string, string> teamNames)
        {
            string? id = null;
            string label;

            switch (slot.Kind)
            {
                case BracketSlotKindEnum.GroupWinner:
                    if (groupSlots.TryGetValue(slot.Group!, out var gs))
                        id = gs.Winner;
                    label = id is not null ? TeamName(id, teamNames) : $"1° Grupo {slot.Group}";
                    break;
                case BracketSlotKindEnum.GroupRunnerUp:
                    if (groupSlots.TryGetValue(slot.Group!, out var gs2))
                        id = gs2.RunnerUp;
                    label = id is not null ? TeamName(id, teamNames) : $"2° Grupo {slot.Group}";
                    break;
                case BracketSlotKindEnum.GroupThird:
                    if (thirdAssignments is not null && thirdAssignments.TryGetValue(tie.Id, out var thirdGroup) && groupSlots.TryGetValue(thirdGroup, out var gs3))
                        id = gs3.Third;
                    label = id is not null ? TeamName(id, teamNames) : $"3° ({string.Join("/", slot.ThirdPlaceGroupOptions ?? [])})";
                    break;
                case BracketSlotKindEnum.WinnerOfTie:
                    if (tieWinners.TryGetValue(slot.TieId!.Value, out var w) && w is not null)
                        id = w;
                    label = id is not null ? TeamName(id, teamNames) : $"Ganador partido {slot.TieId}";
                    break;
                default:
                    label = "TBD";
                    break;
            }

            return (id, label);
        }

        private static string TeamName(string id, IReadOnlyDictionary<string, string> names) =>
            names.TryGetValue(id, out var n) ? n : id;
    }
}
