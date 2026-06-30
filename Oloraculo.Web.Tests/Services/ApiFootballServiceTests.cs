using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Oloraculo.Web;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Models.ApiFootballModels;
using Oloraculo.Web.Models.CsvModels;
using Oloraculo.Web.Predictors;
using Oloraculo.Web.Probability;
using Oloraculo.Web.Services;
using Oloraculo.Web.Services.Simulation;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Oloraculo.Web.Tests;

public class ApiFootballServiceTests : TestFixtures
{
    [Fact]
    public void ApiFootball_EndpointHelpersProduceExpectedUris()
    {
        Assert.Equal("leagues?id=1&season=2026", ApiFootballEndpoints.LeagueCoverage(1, 2026));
        Assert.Equal("injuries?fixture=10", ApiFootballEndpoints.FixtureInjuries("10"));
        Assert.Equal("injuries?league=1&season=2026", ApiFootballEndpoints.LeagueInjuries(1, 2026));
        Assert.Equal("fixtures/lineups?fixture=10", ApiFootballEndpoints.FixtureLineups("10"));
        Assert.Equal("odds?fixture=10", ApiFootballEndpoints.PreMatchOdds("10"));
        Assert.Equal("odds/live?fixture=10", ApiFootballEndpoints.LiveOdds("10"));
        Assert.Equal("fixtures?league=1&season=2026&timezone=UTC", ApiFootballEndpoints.Fixtures(1, 2026));
        Assert.Equal("teams?league=1&season=2026", ApiFootballEndpoints.Teams(1, 2026));
        Assert.Equal("players/squads?team=2", ApiFootballEndpoints.Squad(2));
        Assert.Equal("players?team=2&season=2026", ApiFootballEndpoints.PlayersByTeamSeason(2, 2026));
    }

    [Fact]
    public void ApiFootball_SquadResponseParsesPlayerPositions()
    {
        var parsed = JsonSerializer.Deserialize<ApiSquadResponse>("""
            {
              "response": [{
                "team": { "id": 2, "name": "France" },
                "players": [
                  { "id": 278, "name": "Kylian Mbappé", "position": "Attacker" },
                  { "id": 22090, "name": "W. Saliba", "position": "Defender" }
                ]
              }]
            }
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(parsed);
        Assert.Equal(PlayerPositions.Attacker, parsed.Response[0].Players[0].Position);
        Assert.Equal(PlayerPositions.Defender, parsed.Response[0].Players[1].Position);
    }

    [Fact]
    public void ApiFootball_PlayerStatsResponseParsesImpactFields()
    {
        var parsed = JsonSerializer.Deserialize<ApiPlayerStatsResponse>("""
            {
              "response": [{
                "player": { "id": 278, "name": "Kylian Mbappé" },
                "statistics": [{
                  "games": { "appearences": "5", "lineups": 4, "minutes": 360, "position": "Attacker", "rating": "7.200000" },
                  "goals": { "total": 3, "assists": 2 }
                }]
              }]
            }
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(parsed);
        var stat = parsed.Response[0].Statistics[0];
        Assert.Equal(5, stat.Games.Appearences);
        Assert.Equal(4, stat.Games.Lineups);
        Assert.Equal(360, stat.Games.Minutes);
        Assert.Equal(7.2, stat.Games.Rating);
        Assert.Equal(3, stat.Goals.Total);
        Assert.Equal(2, stat.Goals.Assists);
    }

    [Fact]
    public void ApiFootball_PlayerRoleMatchingHandlesAccentsAndInitialLastNames()
    {
        var candidates = new[]
        {
            new PlayerRoleCandidate(278, "Kylian Mbappé", PlayerPositions.Attacker, "test"),
            new PlayerRoleCandidate(22090, "William Saliba", PlayerPositions.Defender, "test")
        };

        var accent = ApiFootballService.MatchPlayerRole("Kylian Mbappe", candidates);
        var initial = ApiFootballService.MatchPlayerRole("W. Saliba", candidates);

        Assert.Equal(278, accent?.Id);
        Assert.Equal(PlayerPositions.Attacker, accent?.Position);
        Assert.Equal(22090, initial?.Id);
        Assert.Equal(PlayerPositions.Defender, initial?.Position);
    }

    [Fact]
    public async Task ApiFootball_RefreshFixturesStoresFinalScores()
    {
        await using var db = await NewDb();
        db.Fixtures.Add(new Fixture { Id = "f1", Group = "A", HomeTeamId = "argentina", AwayTeamId = "france" });
        await db.SaveChangesAsync();
        var handler = new FakeHttpMessageHandler(new Dictionary<string, string>
        {
            [$"https://api.test/{ApiFootballEndpoints.Fixtures(1, 2026)}"] = """
                {
                  "response": [{
                    "fixture": {
                      "id": 10,
                      "date": "2026-06-12T20:00:00+00:00",
                      "venue": { "name": "Test Stadium", "city": "Test City" },
                      "status": { "short": "FT" }
                    },
                    "teams": {
                      "home": { "id": 1, "name": "Argentina" },
                      "away": { "id": 2, "name": "France" }
                    },
                    "goals": { "home": 2, "away": 1 }
                  }]
                }
                """
        });
        var api = ApiService(db, handler);

        var report = await api.RefreshFixturesAsync();
        var fixture = await db.Fixtures.FindAsync("f1");

        Assert.Equal(1, report.FixturesMatched);
        Assert.NotNull(fixture);
        Assert.True(fixture.IsPlayed);
        Assert.Equal(2, fixture.HomeGoals);
        Assert.Equal(1, fixture.AwayGoals);
        Assert.Equal("FT", fixture.Status);
    }

    [Fact]
    public async Task ApiFootball_RefreshFixturesMatchesApiFootballNameAliases()
    {
        await using var db = await NewDb();
        db.Fixtures.AddRange(
            new Fixture { Id = "cape", Group = "H", HomeTeamId = "spain", AwayTeamId = "cape-verde" },
            new Fixture { Id = "bosnia", Group = "B", HomeTeamId = "canada", AwayTeamId = "bosnia-and-herzegovina" });
        await db.SaveChangesAsync();
        var beforeRefresh = DateTimeOffset.UtcNow;
        var handler = new FakeHttpMessageHandler(new Dictionary<string, string>
        {
            [$"https://api.test/{ApiFootballEndpoints.Fixtures(1, 2026)}"] = """
                {
                  "response": [
                    {
                      "fixture": {
                        "id": 20,
                        "date": "2026-06-18T20:00:00+00:00",
                        "venue": { "name": "Cape Stadium", "city": "Cape City" },
                        "status": { "short": "NS" }
                      },
                      "teams": {
                        "home": { "id": 1, "name": "Spain" },
                        "away": { "id": 2, "name": "Cape Verde Islands" }
                      },
                      "goals": { "home": null, "away": null }
                    },
                    {
                      "fixture": {
                        "id": 21,
                        "date": "2026-06-19T20:00:00+00:00",
                        "venue": { "name": "Bosnia Stadium", "city": "Bosnia City" },
                        "status": { "short": "NS" }
                      },
                      "teams": {
                        "home": { "id": 3, "name": "Canada" },
                        "away": { "id": 4, "name": "Bosnia-Herzegovina" }
                      },
                      "goals": { "home": null, "away": null }
                    }
                  ]
                }
                """
        });
        var api = ApiService(db, handler);

        var report = await api.RefreshFixturesAsync();
        var mappings = await db.ApiMappings.ToDictionaryAsync(m => m.LocalFixtureId);

        Assert.Equal(2, report.FixturesMatched);
        Assert.Equal("20", mappings["cape"].ExternalFixtureId);
        Assert.Equal("21", mappings["bosnia"].ExternalFixtureId);
        Assert.All(mappings.Values, mapping => Assert.InRange(mapping.UpdatedAt, beforeRefresh, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task ApiFootball_RoleEnrichmentUpdatesClaimsWithoutDeletingEvidence()
    {
        await using var db = await NewDb();
        db.AvailabilityClaims.Add(new AvailabilityClaim
        {
            Player = "Kylian Mbappe",
            PlayerKey = AvailabilityNewsService.NormalizePlayerKey("Kylian Mbappe"),
            TeamId = "france",
            TeamName = "France",
            Status = AvailabilityClaimStatus.ConfirmedOutInjury,
            EvidenceLevel = AvailabilityEvidenceLevel.Official,
            SourceUrl = "https://source.test",
            SupportingQuote = "France confirmed Kylian Mbappe will miss the match.",
            AffectsPrediction = true
        });
        await db.SaveChangesAsync();
        var handler = new FakeHttpMessageHandler(new Dictionary<string, string>
        {
            [$"https://api.test/{ApiFootballEndpoints.Teams(1, 2026)}"] = """
                {"response":[{"team":{"id":2,"name":"France"}}]}
                """,
            [$"https://api.test/{ApiFootballEndpoints.Squad(2)}"] = """
                {"response":[{"team":{"id":2,"name":"France"},"players":[{"id":278,"name":"Kylian Mbappé","position":"Attacker"}]}]}
                """,
            [$"https://api.test/{ApiFootballEndpoints.PlayersByTeamSeason(2, 2026)}"] = """
                {"response":[{"player":{"id":278,"name":"Kylian Mbappé"},"statistics":[{"games":{"position":"Attacker","lineups":4,"minutes":360,"rating":"7.200000"},"goals":{"total":3,"assists":1}}]}]}
                """
        });
        var api = ApiService(db, handler);

        var report = await api.EnrichAvailabilityRolesAsync();
        var claim = Assert.Single(await db.AvailabilityClaims.ToListAsync());

        Assert.Equal(1, report.RoleMatchedClaims);
        Assert.Equal(278, claim.ApiFootballPlayerId);
        Assert.Equal(PlayerPositions.Attacker, claim.Position);
        Assert.True(claim.AttackImpact > AvailabilityNewsService.ImpactForPosition(PlayerPositions.Attacker).Attack);
        Assert.Equal(3, claim.ApiGoals);
        Assert.Equal(PlayerImpactSources.ApiStats, claim.ImpactSource);
        Assert.Equal("France confirmed Kylian Mbappe will miss the match.", claim.SupportingQuote);
    }

    [Fact]
    public async Task ApiFootball_SquadFailureLeavesClaimsUnknown()
    {
        await using var db = await NewDb();
        db.AvailabilityClaims.Add(new AvailabilityClaim
        {
            Player = "Mystery Player",
            PlayerKey = AvailabilityNewsService.NormalizePlayerKey("Mystery Player"),
            TeamId = "france",
            TeamName = "France",
            Status = AvailabilityClaimStatus.ConfirmedOutInjury,
            EvidenceLevel = AvailabilityEvidenceLevel.Official,
            SourceUrl = "https://source.test",
            AffectsPrediction = true
        });
        await db.SaveChangesAsync();
        var handler = new FakeHttpMessageHandler(new Dictionary<string, string>
        {
            [$"https://api.test/{ApiFootballEndpoints.Teams(1, 2026)}"] = """
                {"response":[{"team":{"id":2,"name":"France"}}]}
                """
        });
        var api = ApiService(db, handler);

        var report = await api.EnrichAvailabilityRolesAsync();
        var claim = Assert.Single(await db.AvailabilityClaims.ToListAsync());

        Assert.Equal(1, report.RoleUnknownClaims);
        Assert.Equal(PlayerPositions.Unknown, claim.Position);
    }

    [Fact]
    public async Task ApiFootball_RefreshFixtureContextDedupesApiAndNewsPlayersUsingStrongerImpact()
    {
        await using var db = await NewDb();
        db.Teams.AddRange(new Team { Id = "france", Name = "France" }, new Team { Id = "argentina", Name = "Argentina" });
        db.Fixtures.Add(new Fixture { Id = "f1", Group = "A", HomeTeamId = "france", AwayTeamId = "argentina" });
        db.ApiMappings.Add(new ApiMapping { LocalFixtureId = "f1", ExternalFixtureId = "10" });
        db.AvailabilityClaims.Add(new AvailabilityClaim
        {
            Player = "Kylian Mbappe",
            PlayerKey = AvailabilityNewsService.NormalizePlayerKey("Kylian Mbappe"),
            TeamId = "france",
            TeamName = "France",
            Status = AvailabilityClaimStatus.ConfirmedOutInjury,
            EvidenceLevel = AvailabilityEvidenceLevel.Official,
            SourceUrl = "https://source.test",
            AffectsPrediction = true,
            Position = PlayerPositions.Attacker,
            AttackImpact = AvailabilityNewsService.ImpactForPosition(PlayerPositions.Attacker).Attack,
            DefenseImpact = AvailabilityNewsService.ImpactForPosition(PlayerPositions.Attacker).Defense
        });
        await db.SaveChangesAsync();
        var handler = new FakeHttpMessageHandler(new Dictionary<string, string>
        {
            [$"https://api.test/{ApiFootballEndpoints.LeagueCoverage(1, 2026)}"] = """{"response":[{"league":{"coverage":{"injuries":true,"odds":true,"fixtures":{"lineups":true}}}}]}""",
            [$"https://api.test/{ApiFootballEndpoints.FixtureInjuries("10")}"] = """
                {"response":[{"player":{"id":278,"name":"Kylian Mbappé","type":"Missing Fixture","reason":"injury"},"team":{"id":2,"name":"France"}}]}
                """,
            [$"https://api.test/{ApiFootballEndpoints.LeagueInjuries(1, 2026)}"] = """{"response":[]}""",
            [$"https://api.test/{ApiFootballEndpoints.FixtureLineups("10")}"] = """{"response":[]}""",
            [$"https://api.test/{ApiFootballEndpoints.PreMatchOdds("10")}"] = """{"response":[]}""",
            [$"https://api.test/{ApiFootballEndpoints.LiveOdds("10")}"] = """{"response":[]}""",
            [$"https://api.test/{ApiFootballEndpoints.Squad(2)}"] = """
                {"response":[{"team":{"id":2,"name":"France"},"players":[{"id":278,"name":"Kylian Mbappé","position":"Attacker"}]}]}
                """,
            [$"https://api.test/{ApiFootballEndpoints.PlayersByTeamSeason(2, 2026)}"] = """
                {"response":[{"player":{"id":278,"name":"Kylian Mbappé"},"statistics":[{"games":{"position":"Attacker","lineups":4,"minutes":360,"rating":"7.200000"},"goals":{"total":3,"assists":1}}]}]}
                """
        });
        var api = ApiService(db, handler);

        var report = await api.RefreshFixtureContextAsync("f1");
        var context = await db.FixtureContexts.FindAsync("f1");

        Assert.NotNull(context);
        Assert.Equal(1, context.UnavailableHomePlayers);
        Assert.True(context.UnavailableHomeAttackImpact > AvailabilityNewsService.ImpactForPosition(PlayerPositions.Attacker).Attack);
        Assert.Equal(1, report.ImpactMatchedPlayers);
    }

    private static ApiFootballService ApiService(OloraculoDbContext db, HttpMessageHandler handler)
    {
        var options = Options.Create(new OloraculoConfig
        {
            ApiFootballApiKey = "test-key",
            ApiFootballBaseUrl = "https://api.test/",
            ApiFootballLeagueId = 1,
            ApiFootballSeason = 2026,
            OpenRouterApiKey = "test-key",
            OpenRouterBaseUrl = "https://openrouter.test/",
            AvailabilitySourceUrls = [],
            GoalscorersRawUrl = ""
        });
        var impact = new PlayerImpactService(
            new HttpClient(new FakeHttpMessageHandler(new Dictionary<string, string>())),
            new TestEnvironment(NewTempRoot()),
            options);
        var availability = new AvailabilityNewsService(
            new HttpClient(new FakeHttpMessageHandler(new Dictionary<string, string>())) { BaseAddress = new Uri("https://openrouter.test/") },
            db,
            options,
            impact);

        return new ApiFootballService(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") }, db, options, availability, impact);
    }

}
