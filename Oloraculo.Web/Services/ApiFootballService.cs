using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Models.ApiFootballModels;

namespace Oloraculo.Web.Services
{
    public class ApiFootballService
    {
        private readonly HttpClient _http;
        private readonly OloraculoDbContext _db;
        private readonly OloraculoConfig _config;
        private readonly AvailabilityNewsService _availability;
        private readonly PlayerImpactService? _impact;
        private bool IsConfigured => !string.IsNullOrWhiteSpace(_config.ApiFootballApiKey);
        public ApiFootballService(HttpClient httpClient, OloraculoDbContext db, IOptions<OloraculoConfig> config, AvailabilityNewsService availability, PlayerImpactService? impact = null)
        {
            this._http = httpClient;
            this._db = db;
            this._config = config.Value;
            this._availability = availability;
            _impact = impact;
        }

        public Task<ApiFootballRefreshReport> RefreshAsync(string fixtureId, CancellationToken ct = default) =>
            RefreshFixtureContextAsync(fixtureId, ct);

        public async Task<ApiFootballRefreshReport> RefreshFixtureContextAsync(string fixtureId, CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new ApiFootballRefreshReport { IsConfigured = false, Notes = ["La clave de API-Football no está configurada."] };

            var errors = new List<string>();
            var notes = new List<string>();
            try
            {
                var fixture = await _db.Fixtures.FindAsync([fixtureId], ct);
                if (fixture is null)
                    return new ApiFootballRefreshReport { IsConfigured = true, Errors = [$"No se encontró el partido {fixtureId}."] };

                var mapping = await _db.ApiMappings.SingleOrDefaultAsync(m => m.LocalFixtureId == fixtureId, ct);
                if (mapping is null)
                {
                    var refresh = await RefreshFixturesAsync(ct);
                    mapping = await _db.ApiMappings.SingleOrDefaultAsync(m => m.LocalFixtureId == fixtureId, ct);
                    if (mapping is null)
                        return new ApiFootballRefreshReport { IsConfigured = true, Notes = refresh.Notes, Errors = ["No se encontró un mapeo de API para este partido local."] };
                }

                var coverage = await GetApiAsync<ApiLeagueResponse>(
                    ApiFootballEndpoints.LeagueCoverage(_config.ApiFootballLeagueId, _config.ApiFootballSeason),
                    "cobertura",
                    errors,
                    ct);
                var coverageInfo = coverage?.Response.FirstOrDefault()?.League.Coverage;
                if (coverageInfo is not null)
                    notes.Add($"La cobertura indica lesiones={coverageInfo.Injuries}, cuotas={coverageInfo.Odds}, alineaciones={coverageInfo.Fixtures.Lineups}.");

                var fixtureInjuries = await GetApiAsync<ApiInjuryResponse>(
                    ApiFootballEndpoints.FixtureInjuries(mapping.ExternalFixtureId),
                    "lesiones del partido",
                    errors,
                    ct);
                var leagueInjuries = await GetApiAsync<ApiInjuryResponse>(
                    ApiFootballEndpoints.LeagueInjuries(_config.ApiFootballLeagueId, _config.ApiFootballSeason),
                    "lesiones de la liga",
                    errors,
                    ct);
                var lineups = await GetApiAsync<ApiLineupResponse>(
                    ApiFootballEndpoints.FixtureLineups(mapping.ExternalFixtureId),
                    "alineaciones",
                    errors,
                    ct);
                var preMatchOdds = await GetApiAsync<ApiOddsResponse>(
                    ApiFootballEndpoints.PreMatchOdds(mapping.ExternalFixtureId),
                    "cuotas previas",
                    errors,
                    ct);
                var liveOdds = await GetApiAsync<ApiOddsResponse>(
                    ApiFootballEndpoints.LiveOdds(mapping.ExternalFixtureId),
                    "cuotas en vivo",
                    errors,
                    ct);

                var fixtureInjuryRows = fixtureInjuries?.Response.Count ?? 0;
                var leagueInjuryRows = leagueInjuries?.Response.Count ?? 0;
                var lineupRows = lineups?.Response.Count ?? 0;
                var preMatchOddsRows = preMatchOdds?.Response.Count ?? 0;
                var liveOddsRows = liveOdds?.Response.Count ?? 0;

                var relevantInjuries = MergeRelevantInjuries(fixture, fixtureInjuries?.Response ?? [], leagueInjuries?.Response ?? []);
                var externalUnavailablePlayers = new List<UnavailablePlayerRole>();
                var candidatesByTeam = new Dictionary<long, List<PlayerRoleCandidate>>();
                foreach (var injury in relevantInjuries)
                {
                    var teamId = TeamNameNormalizer.ToId(injury.Team.Name);
                    var playerKey = AvailabilityNewsService.NormalizePlayerKey(injury.Player.Name);
                    var candidates = injury.Team.Id > 0
                        ? await PlayerCandidatesForTeamAsync(injury.Team.Id, candidatesByTeam, errors, ct)
                        : [];
                    var role = MatchPlayerRole(injury.Player.Id, injury.Player.Name, candidates);
                    var position = AvailabilityNewsService.NormalizePosition(role?.Position);
                    var impact = await CalculateImpactAsync(teamId, injury.Player.Name, playerKey, position, role?.Statistics, ct);
                    externalUnavailablePlayers.Add(new UnavailablePlayerRole(
                        teamId,
                        playerKey,
                        position,
                        impact.Attack,
                        impact.Defense,
                        injury.Player.Name,
                        impact.Source));
                }

                var newsClaims = await _availability.AffectingClaimsForTeamsAsync([fixture.HomeTeamId, fixture.AwayTeamId], ct);
                await _availability.RefreshFixtureContextCountsAsync(fixtureId, externalUnavailablePlayers, ct);
                var context = await _db.FixtureContexts.FindAsync([fixtureId], ct);
                if (context is null)
                {
                    context = new FixtureContext { FixtureId = fixtureId };
                    _db.FixtureContexts.Add(context);
                }

                context.HasLineups = lineupRows > 0;
                context.HasOdds = preMatchOddsRows > 0 || liveOddsRows > 0;
                context.Notes = $"Actualizado desde API-Football. lesiones del partido={fixtureInjuryRows}; lesiones de la liga={leagueInjuryRows}; noticias confirmadas={newsClaims.Count}; roles noticias matcheados={newsClaims.Count(c => c.Position != PlayerPositions.Unknown)}; roles noticias desconocidos={newsClaims.Count(c => c.Position == PlayerPositions.Unknown)}; impactos API enriquecidos={externalUnavailablePlayers.Count(p => !PlayerImpactSources.IsFallback(p.ImpactSource))}; alineaciones={lineupRows}; cuotas previas={preMatchOddsRows}; cuotas en vivo={liveOddsRows}.";
                context.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);

                notes.Add($"Filas de lesiones del partido: {fixtureInjuryRows}. Filas de lesiones de liga/temporada: {leagueInjuryRows}. Bajas o dudas relevantes guardadas: equipo A {context.UnavailableHomePlayers}, equipo B {context.UnavailableAwayPlayers}.");
                if (newsClaims.Count > 0)
                    notes.Add($"Noticias confirmadas incluidas en el contexto: {newsClaims.Count}.");
                if (externalUnavailablePlayers.Count > 0)
                    notes.Add($"Lesiones API-Football con impacto enriquecido: {externalUnavailablePlayers.Count(p => !PlayerImpactSources.IsFallback(p.ImpactSource))}/{externalUnavailablePlayers.Count}.");
                notes.Add($"Filas de alineaciones: {lineupRows}. Filas de cuotas previas: {preMatchOddsRows}. Filas de cuotas en vivo: {liveOddsRows}.");
                if (fixtureInjuryRows == 0 && leagueInjuryRows == 0)
                    notes.Add("No llegaron filas de lesiones. API-Football puede soportar lesiones para la competencia, pero todavía no tener bajas asociadas.");
                if (preMatchOddsRows == 0)
                    notes.Add("No llegaron cuotas previas. API-Football documenta las cuotas previas como limitadas a los últimos 7 días.");
                if (liveOddsRows == 0)
                    notes.Add("No llegaron cuotas en vivo. Es esperable salvo que el partido esté cerca de empezar, en vivo o recién terminado.");

                return new ApiFootballRefreshReport
                {
                    IsConfigured = true,
                    ContextRows = context.UnavailableHomePlayers + context.UnavailableAwayPlayers,
                    FixtureInjuryRows = fixtureInjuryRows,
                    LeagueInjuryRows = leagueInjuryRows,
                    LineupRows = lineupRows,
                    PreMatchOddsRows = preMatchOddsRows,
                    LiveOddsRows = liveOddsRows,
                    ImpactMatchedPlayers = externalUnavailablePlayers.Count(p => !PlayerImpactSources.IsFallback(p.ImpactSource)),
                    ImpactFallbackPlayers = externalUnavailablePlayers.Count(p => PlayerImpactSources.IsFallback(p.ImpactSource)),
                    Notes = notes,
                    Errors = errors
                };
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                return new ApiFootballRefreshReport { IsConfigured = true, Errors = errors };
            }
        }
        public async Task<ApiFootballRefreshReport> RefreshFixturesAsync(CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new ApiFootballRefreshReport { IsConfigured = false, Notes = ["La clave de API-Football no está configurada. Los datos CSV siguen funcionando."] };

            var errors = new List<string>();
            var notes = new List<string>();
            try
            {
                var response = await _http.GetFromJsonAsync<ApiFixtureResponse>(
                    ApiFootballEndpoints.Fixtures(_config.ApiFootballLeagueId, _config.ApiFootballSeason), ct);
                var items = response?.Response ?? [];
                var local = await _db.Fixtures.ToListAsync(ct);
                var byPair = local.ToDictionary(f => PairKey(f.HomeTeamId, f.AwayTeamId));
                var matched = 0;
                var unmatchedPairs = new List<string>();

                foreach (var api in items)
                {
                    var home = TeamNameNormalizer.ToId(api.Teams.Home.Name);
                    var away = TeamNameNormalizer.ToId(api.Teams.Away.Name);
                    if (!byPair.TryGetValue(PairKey(home, away), out var fixture))
                    {
                        unmatchedPairs.Add($"{api.Teams.Home.Name} vs {api.Teams.Away.Name} ({home} vs {away})");
                        continue;
                    }

                    fixture.KickoffUtc = api.Fixture.Date;
                    fixture.Venue = api.Fixture.Venue?.Name;
                    fixture.City = api.Fixture.Venue?.City;
                    fixture.Status = api.Fixture.Status?.Short;
                    if (IsFinishedStatus(api.Fixture.Status?.Short) && api.Goals.Home.HasValue && api.Goals.Away.HasValue)
                    {
                        fixture.IsPlayed = true;
                        fixture.HomeGoals = api.Teams.Home.Name is { } homeName &&
                            TeamNameNormalizer.ToId(homeName) == fixture.HomeTeamId
                                ? api.Goals.Home.Value
                                : api.Goals.Away.Value;
                        fixture.AwayGoals = api.Teams.Away.Name is { } awayName &&
                            TeamNameNormalizer.ToId(awayName) == fixture.AwayTeamId
                                ? api.Goals.Away.Value
                                : api.Goals.Home.Value;
                    }
                    fixture.Source = "API-Football";
                    matched++;

                    var existing = await _db.ApiMappings.SingleOrDefaultAsync(m => m.LocalFixtureId == fixture.Id, ct);
                    var updatedAt = DateTimeOffset.UtcNow;
                    if (existing is null)
                        _db.ApiMappings.Add(new ApiMapping { LocalFixtureId = fixture.Id, ExternalFixtureId = api.Fixture.Id.ToString(), UpdatedAt = updatedAt });
                    else
                    {
                        existing.ExternalFixtureId = api.Fixture.Id.ToString();
                        existing.UpdatedAt = updatedAt;
                    }
                }

                await _db.SaveChangesAsync(ct);
                notes.Add($"Se obtuvieron {items.Count} filas de partidos y se matchearon {matched} partidos locales de fase de grupos.");
                if (unmatchedPairs.Count > 0)
                    notes.Add($"No se matchearon {unmatchedPairs.Count} filas API contra partidos locales: {string.Join("; ", unmatchedPairs.Take(10))}{(unmatchedPairs.Count > 10 ? "; ..." : "")}");
                return new ApiFootballRefreshReport { IsConfigured = true, FixturesFetched = items.Count, FixturesMatched = matched, Notes = notes };
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
                return new ApiFootballRefreshReport { IsConfigured = true, Errors = errors };
            }
        }

        public async Task<AvailabilityRefreshReport> EnrichAvailabilityRolesAsync(CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new AvailabilityRefreshReport { IsConfigured = false, Notes = ["La clave de API-Football no está configurada. No se pueden resolver roles."] };

            var errors = new List<string>();
            var notes = new List<string>();
            var claims = await _db.AvailabilityClaims
                .Where(c => c.Status != AvailabilityClaimStatus.Available && c.Status != AvailabilityClaimStatus.NotRelevant)
                .ToListAsync(ct);

            if (claims.Count == 0)
                return new AvailabilityRefreshReport { IsConfigured = true, Notes = ["No hay reclamos de disponibilidad para enriquecer con roles."] };

            var apiTeams = await GetApiAsync<ApiTeamListResponse>(
                ApiFootballEndpoints.Teams(_config.ApiFootballLeagueId, _config.ApiFootballSeason),
                "equipos API-Football",
                errors,
                ct);
            var teamMap = (apiTeams?.Response ?? [])
                .GroupBy(t => TeamNameNormalizer.ToId(t.Team.Name), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Team.Id, StringComparer.Ordinal);
            var matched = 0;
            var unknown = 0;
            var impactMatched = 0;
            var candidatesByTeam = new Dictionary<long, List<PlayerRoleCandidate>>();

            foreach (var teamClaims in claims.GroupBy(c => c.TeamId))
            {
                if (!teamMap.TryGetValue(teamClaims.Key, out var apiTeamId))
                {
                    foreach (var claim in teamClaims)
                    {
                        MarkUnknown(claim);
                        var impact = await CalculateImpactAsync(claim.TeamId, claim.Player, claim.PlayerKey, claim.Position, null, ct);
                        AvailabilityNewsService.ApplyImpact(claim, impact);
                        if (!PlayerImpactSources.IsFallback(impact.Source))
                            impactMatched++;
                    }
                    unknown += teamClaims.Count();
                    continue;
                }

                var candidates = await PlayerCandidatesForTeamAsync(apiTeamId, candidatesByTeam, errors, ct);

                foreach (var claim in teamClaims)
                {
                    var role = MatchPlayerRole(claim.ApiFootballPlayerId, claim.Player, candidates);

                    if (role is null)
                    {
                        MarkUnknown(claim);
                        unknown++;
                    }
                    else
                    {
                        claim.ApiFootballPlayerId = role.Id;
                        claim.Position = AvailabilityNewsService.NormalizePosition(role.Position);
                        claim.PositionSource = role.Source;
                        claim.PositionMatchedAt = DateTimeOffset.UtcNow;
                        matched++;
                    }

                    var impact = await CalculateImpactAsync(claim.TeamId, claim.Player, claim.PlayerKey, claim.Position, role?.Statistics, ct);
                    AvailabilityNewsService.ApplyImpact(claim, impact);
                    if (!PlayerImpactSources.IsFallback(impact.Source))
                        impactMatched++;
                }
            }

            await _db.SaveChangesAsync(ct);
            var contexts = 0;
            foreach (var fixture in await _db.Fixtures.AsNoTracking().Select(f => f.Id).ToListAsync(ct))
            {
                if (await _availability.RefreshFixtureContextCountsAsync(fixture, [], ct))
                    contexts++;
            }

            notes.Add($"Roles API-Football resueltos: {matched}. Roles desconocidos: {unknown}. Impactos enriquecidos: {impactMatched}.");
            return new AvailabilityRefreshReport
            {
                IsConfigured = true,
                RoleMatchedClaims = matched,
                RoleUnknownClaims = unknown,
                ImpactMatchedClaims = impactMatched,
                ImpactFallbackClaims = claims.Count - impactMatched,
                ContextRowsUpdated = contexts,
                Notes = notes,
                Errors = errors
            };
        }

        private async Task<T?> GetApiAsync<T>(string uri, string label, List<string> errors, CancellationToken ct)
        {
            try
            {
                return await _http.GetFromJsonAsync<T>(uri, ct);
            }
            catch (Exception ex)
            {
                errors.Add($"{label}: {ex.Message}");
                return default;
            }
        }

        private async Task<List<PlayerRoleCandidate>> PlayerCandidatesForTeamAsync(
            long apiTeamId,
            Dictionary<long, List<PlayerRoleCandidate>> cache,
            List<string> errors,
            CancellationToken ct)
        {
            if (cache.TryGetValue(apiTeamId, out var cached))
                return cached;

            var squad = await GetApiAsync<ApiSquadResponse>(ApiFootballEndpoints.Squad(apiTeamId), $"plantel {apiTeamId}", errors, ct);
            var squadCandidates = (squad?.Response.FirstOrDefault()?.Players ?? [])
                .Select(p => new PlayerRoleCandidate(p.Id, p.Name, p.Position, ApiFootballEndpoints.SquadSource))
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToList();
            var seasonCandidates = await SeasonPlayerCandidatesAsync(apiTeamId, errors, ct);
            var merged = MergeCandidates(squadCandidates, seasonCandidates);
            cache[apiTeamId] = merged;
            return merged;
        }

        private async Task<List<PlayerRoleCandidate>> SeasonPlayerCandidatesAsync(long apiTeamId, List<string> errors, CancellationToken ct)
        {
            var response = await GetApiAsync<ApiPlayerStatsResponse>(ApiFootballEndpoints.PlayersByTeamSeason(apiTeamId, _config.ApiFootballSeason), $"jugadores {apiTeamId}", errors, ct);
            return (response?.Response ?? [])
                .Select(row => new PlayerRoleCandidate(
                    row.Player.Id,
                    row.Player.Name,
                    row.Statistics.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Games.Position))?.Games.Position ?? "",
                    ApiFootballEndpoints.PlayersByTeamSeasonSource(_config.ApiFootballSeason),
                    row.Statistics))
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .ToList();
        }

        private static List<PlayerRoleCandidate> MergeCandidates(
            IReadOnlyList<PlayerRoleCandidate> squadCandidates,
            IReadOnlyList<PlayerRoleCandidate> seasonCandidates)
        {
            var merged = new List<PlayerRoleCandidate>();
            var seasonById = seasonCandidates
                .Where(c => c.Id > 0)
                .GroupBy(c => c.Id)
                .ToDictionary(g => g.Key, g => g.First());
            var usedSeasonIds = new HashSet<long>();

            foreach (var squad in squadCandidates)
            {
                if (squad.Id > 0 && seasonById.TryGetValue(squad.Id, out var season))
                {
                    usedSeasonIds.Add(season.Id);
                    merged.Add(squad with
                    {
                        Source = $"{squad.Source}+{season.Source}",
                        Statistics = season.Statistics
                    });
                    continue;
                }

                var seasonByName = MatchPlayerRole(squad.Name, seasonCandidates);
                if (seasonByName is not null)
                {
                    if (seasonByName.Id > 0)
                        usedSeasonIds.Add(seasonByName.Id);
                    merged.Add(squad with
                    {
                        Source = $"{squad.Source}+{seasonByName.Source}",
                        Statistics = seasonByName.Statistics
                    });
                }
                else
                {
                    merged.Add(squad);
                }
            }

            merged.AddRange(seasonCandidates.Where(c => c.Id <= 0 || !usedSeasonIds.Contains(c.Id)));
            return merged;
        }

        private static void MarkUnknown(AvailabilityClaim claim)
        {
            claim.ApiFootballPlayerId = null;
            claim.Position = PlayerPositions.Unknown;
            claim.PositionSource = PlayerPositions.Unknown;
            claim.PositionMatchedAt = DateTimeOffset.UtcNow;
        }

        public static PlayerRoleCandidate? MatchPlayerRole(string playerName, IEnumerable<PlayerRoleCandidate> candidates)
        {
            var byFullName = candidates
                .GroupBy(c => AvailabilityNewsService.NormalizePlayerKey(c.Name), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var fullKey = AvailabilityNewsService.NormalizePlayerKey(playerName);
            if (byFullName.TryGetValue(fullKey, out var exact) && exact.Count == 1)
                return exact[0];

            var byInitialLast = candidates
                .GroupBy(c => InitialLastKey(c.Name), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var initialLast = InitialLastKey(playerName);
            if (byInitialLast.TryGetValue(initialLast, out var loose) && loose.Count == 1)
                return loose[0];

            return null;
        }

        private static PlayerRoleCandidate? MatchPlayerRole(long? playerId, string playerName, IEnumerable<PlayerRoleCandidate> candidates)
        {
            if (playerId is > 0)
            {
                var byId = candidates.Where(c => c.Id == playerId.Value).ToList();
                if (byId.Count == 1)
                    return byId[0];
            }

            return MatchPlayerRole(playerName, candidates);
        }

        private async Task<PlayerImpactResult> CalculateImpactAsync(
            string teamId,
            string player,
            string playerKey,
            string? position,
            IReadOnlyList<ApiPlayerStatistic>? statistics,
            CancellationToken ct) =>
            _impact is null
                ? PlayerImpactService.FallbackImpact(position)
                : await _impact.CalculateAsync(teamId, player, playerKey, position, statistics, ct);

        private static bool IsFinishedStatus(string? status) =>
            status is "FT" or "AET" or "PEN";

        public static string InitialLastKey(string playerName)
        {
            var parts = AvailabilityNewsService.NormalizePlayerKey(playerName)
                .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length == 0)
                return string.Join("-", parts);

            return $"{parts[0][0]}-{parts[^1]}";
        }

        private static string PairKey(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
        private static IReadOnlyList<ApiInjury> MergeRelevantInjuries(Fixture fixture, IEnumerable<ApiInjury> fixtureInjuries, IEnumerable<ApiInjury> leagueInjuries)
        {
            var relevant = new Dictionary<string, ApiInjury>();
            foreach (var injury in fixtureInjuries.Concat(leagueInjuries))
            {
                var teamId = TeamNameNormalizer.ToId(injury.Team.Name);
                if (teamId != fixture.HomeTeamId && teamId != fixture.AwayTeamId)
                    continue;

                var playerKey = injury.Player.Id > 0 ? injury.Player.Id.ToString() : injury.Player.Name;
                var key = $"{teamId}|{playerKey}|{injury.Player.Type}|{injury.Player.Reason}";
                relevant.TryAdd(key, injury);
            }

            return relevant.Values.ToList();
        }

    }

    public sealed record PlayerRoleCandidate(
        long Id,
        string Name,
        string Position,
        string Source,
        IReadOnlyList<ApiPlayerStatistic>? Statistics = null);
}
