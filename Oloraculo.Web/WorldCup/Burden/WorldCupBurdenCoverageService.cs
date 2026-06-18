using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Models;

namespace Oloraculo.Web.WorldCup.Burden
{
    public sealed class WorldCupBurdenCoverageService
    {
        private readonly OloraculoDbContext _db;

        public WorldCupBurdenCoverageService(OloraculoDbContext db) => _db = db;

        public async Task<WorldCupBurdenCoverageSnapshot> SnapshotAsync(CancellationToken ct = default)
        {
            var fixtures = await _db.Fixtures.AsNoTracking().ToListAsync(ct);
            return Snapshot(fixtures);
        }

        public static WorldCupBurdenCoverageSnapshot Snapshot(IReadOnlyList<Fixture> fixtures)
        {
            var blockerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var ready = 0;

            foreach (var fixture in fixtures)
            {
                var blockers = Blockers(fixture);
                if (blockers.Count == 0)
                {
                    ready++;
                    continue;
                }

                foreach (var blocker in blockers)
                    blockerCounts[blocker] = blockerCounts.TryGetValue(blocker, out var count) ? count + 1 : 1;
            }

            return new WorldCupBurdenCoverageSnapshot(
                fixtures.Count,
                fixtures.Count(fixture => fixture.KickoffUtc.HasValue),
                fixtures.Count(fixture => !string.IsNullOrWhiteSpace(fixture.Venue)),
                fixtures.Count(fixture => !string.IsNullOrWhiteSpace(fixture.City)),
                ready,
                blockerCounts);
        }

        private static IReadOnlyList<string> Blockers(Fixture fixture)
        {
            var blockers = new List<string>();
            if (fixture.KickoffUtc is null)
                blockers.Add("NO_KICKOFF_UTC");
            if (string.IsNullOrWhiteSpace(fixture.Venue))
                blockers.Add("NO_VENUE");
            if (string.IsNullOrWhiteSpace(fixture.City))
                blockers.Add("NO_CITY");
            return blockers;
        }
    }
}
