using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Models;
using Oloraculo.Web.Predictors;

namespace Oloraculo.Web.Services
{
    public class PredictionService
    {
        private readonly OloraculoDbContext _db;
        private readonly OloraculoConfig _config;

        public PredictionService(OloraculoDbContext db, IOptions<OloraculoConfig> config)
        {
            _db = db;
            _config = config.Value;
        }

        public async Task<MatchPredictionResult?> PredictFixtureAsync(string fixtureId, CancellationToken ct = default)
        {
            var fixture = await _db.Fixtures.FindAsync([fixtureId], ct);
            return fixture is null ? null : await PredictAsync(fixture, ct);
        }

        public async Task<MatchPredictionResult> PredictPairAsync(string homeId, string awayId, CancellationToken ct = default)
        {
            var fixture = new Fixture { Id = $"pair:{homeId}:{awayId}", HomeTeamId = homeId, AwayTeamId = awayId, NeutralVenue = true };
            return await PredictAsync(fixture, ct);
        }

        public async Task<IReadOnlyList<MatchPredictionResult>> PredictFixturesAsync(IEnumerable<Fixture> fixtures, CancellationToken ct = default)
        {
            var fixtureList = fixtures.ToList();
            var predictors = await BuildPredictorsAsync(ct);
            var results = new List<MatchPredictionResult>(fixtureList.Count);

            foreach (var fixture in fixtureList)
                results.Add(await PredictAsync(fixture, predictors, ct));

            return results;
        }

        public async Task<MatchPredictionResult> PredictAsync(Fixture fixture, CancellationToken ct = default)
        {
            var predictors = await BuildPredictorsAsync(ct);
            return await PredictAsync(fixture, predictors, ct);
        }

        private async Task<MatchPredictionResult> PredictAsync(Fixture fixture, IReadOnlyList<IPredictor> predictors, CancellationToken ct)
        {
            var context = await BuildContextAsync(fixture, ct);
            var ladder = predictors.Select(p => p.Predict(context)).ToList();

            return new MatchPredictionResult
            {
                Fixture = fixture,
                HomeTeamName = context.HomeTeam.Name,
                AwayTeamName = context.AwayTeam.Name,
                Predictions = ladder,
                BestPrediction = FinalPredictionSelector.Select(ladder)
            };
        }

        public async Task<MatchContext> BuildContextAsync(Fixture fixture, CancellationToken ct = default)
        {
            var home = await _db.Teams.FindAsync([fixture.HomeTeamId], ct) ?? new Team { Id = fixture.HomeTeamId, Name = fixture.HomeTeamId };
            var away = await _db.Teams.FindAsync([fixture.AwayTeamId], ct) ?? new Team { Id = fixture.AwayTeamId, Name = fixture.AwayTeamId };

            return new MatchContext
            {
                Fixture = fixture,
                HomeTeam = home,
                AwayTeam = away,
                HomeElo = await LatestRatingAsync(fixture.HomeTeamId, RatingTypeEnum.Elo, ct),
                AwayElo = await LatestRatingAsync(fixture.AwayTeamId, RatingTypeEnum.Elo, ct),
                HomeFifaRank = await LatestRatingAsync(fixture.HomeTeamId, RatingTypeEnum.Fifa, ct),
                AwayFifaRank = await LatestRatingAsync(fixture.AwayTeamId, RatingTypeEnum.Fifa, ct),
                HomeRecentMatchHistory = await RecentResultsAsync(fixture.HomeTeamId, ct),
                AwayRecentMatchHistory = await RecentResultsAsync(fixture.AwayTeamId, ct),
                FixtureContext = await _db.FixtureContexts.FindAsync([fixture.Id], ct)
            };
        }

        public async Task<IReadOnlyList<IPredictor>> BuildPredictorsAsync(CancellationToken ct = default)
        {
            var results = await _db.Results.AsNoTracking().ToListAsync(ct);
            var goal = new GoalModel(results, _config.GoalModelYearsWindow);

            return
            [
                new NullModel(),
                new FifaRankingModel(),
                new EloModel(),
                new RecentFormModel(),
                goal,
                new GoalPlusRecentContextModel(goal)
            ];
        }

        private async Task<Rating?> LatestRatingAsync(string teamId, RatingTypeEnum type, CancellationToken ct)
        {
            return (await _db.Ratings.AsNoTracking()
                .Where(r => r.TeamId == teamId && r.Type == type)
                .ToListAsync(ct))
                .OrderByDescending(r => r.AsOf)
                .FirstOrDefault();
        }

        private async Task<IReadOnlyList<MatchResult>> RecentResultsAsync(string teamId, CancellationToken ct)
        {
            return (await _db.Results.AsNoTracking()
                .Where(r => r.HomeTeamId == teamId || r.AwayTeamId == teamId)
                .ToListAsync(ct))
                .OrderByDescending(r => r.Date)
                .Take(_config.RecentResultCount)
                .ToList();
        }
    }
}
