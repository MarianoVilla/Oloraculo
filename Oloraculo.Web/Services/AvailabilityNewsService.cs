using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Oloraculo.Web.Services
{
    public class AvailabilityNewsService
    {
        private const string PositionClaimJsonField = "position";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly HttpClient _http;
        private readonly OloraculoDbContext _db;
        private readonly OloraculoConfig _config;
        private readonly PlayerImpactService? _impact;

        private bool IsConfigured => !string.IsNullOrWhiteSpace(_config.OpenRouterApiKey);

        public AvailabilityNewsService(HttpClient http, OloraculoDbContext db, IOptions<OloraculoConfig> config, PlayerImpactService? impact = null)
        {
            _http = http;
            _db = db;
            _config = config.Value;
            _impact = impact;
        }

        public async Task<AvailabilityRefreshReport> RefreshAsync(CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new AvailabilityRefreshReport { IsConfigured = false, Notes = ["OpenRouter key is not configured."] };

            var notes = new List<string>();
            var errors = new List<string>();
            var fetched = 0;
            var skipped = 0;
            var saved = 0;
            var savedConfirmedOut = 0;
            var savedDoubtful = 0;
            var savedAvailable = 0;

            foreach (var url in _config.AvailabilitySourceUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fetch = await FetchSourceAsync(url, ct);
                await UpsertSourceAsync(url, fetch, ct);

                if (!fetch.Success || string.IsNullOrWhiteSpace(fetch.Text))
                {
                    skipped++;
                    errors.Add(fetch.Error ?? $"Could not read {url}.");
                    continue;
                }

                fetched++;
                try
                {
                    var json = await ClassifyAsync(fetch, ct);
                    var claims = ParseClaimsFromJson(json, fetch.Url, fetch.Publisher)
                        .Where(c => c.Status != AvailabilityClaimStatus.NotRelevant)
                        .ToList();

                    await ReplaceClaimsForSourceAsync(fetch.Url, claims, ct);
                    saved += claims.Count;
                    savedConfirmedOut += claims.Count(c => IsConfirmedOut(c.Status));
                    savedDoubtful += claims.Count(c => c.Status is AvailabilityClaimStatus.Doubtful or AvailabilityClaimStatus.FitnessConcern);
                    savedAvailable += claims.Count(c => c.Status == AvailabilityClaimStatus.Available);
                    notes.Add($"{fetch.Publisher ?? fetch.Url}: {claims.Count} availability claims saved from OpenRouter.");
                }
                catch (Exception ex)
                {
                    var deterministicClaims = ParseTrackerClaims(fetch.Text, fetch.Url, fetch.Publisher);
                    if (deterministicClaims.Count > 0)
                    {
                        await ReplaceClaimsForSourceAsync(fetch.Url, deterministicClaims, ct);
                        saved += deterministicClaims.Count;
                        savedConfirmedOut += deterministicClaims.Count(c => IsConfirmedOut(c.Status));
                        savedDoubtful += deterministicClaims.Count(c => c.Status is AvailabilityClaimStatus.Doubtful or AvailabilityClaimStatus.FitnessConcern);
                        savedAvailable += deterministicClaims.Count(c => c.Status == AvailabilityClaimStatus.Available);
                        notes.Add($"{fetch.Publisher ?? fetch.Url}: {deterministicClaims.Count} claims saved from tracker rows.");
                        errors.Add($"{url}: OpenRouter returned no parseable data ({ex.Message}); locally parsed tracker rows were used.");
                    }
                    else
                    {
                        errors.Add($"{url}: OpenRouter returned no parseable data ({ex.Message}). Existing claims from that source were preserved.");
                    }
                }
            }

            await RecomputePredictionFlagsAsync(ct);
            var impactMatched = await RefreshClaimImpactsAsync(ct);
            var contexts = await RefreshAllFixtureContextsAsync(ct);
            var affecting = await _db.AvailabilityClaims.CountAsync(c => c.AffectsPrediction, ct);
            var matched = await _db.AvailabilityClaims.CountAsync(c => c.AffectsPrediction && c.Position != PlayerPositions.Unknown, ct);

            return new AvailabilityRefreshReport
            {
                IsConfigured = true,
                SourcesFetched = fetched,
                SourcesSkipped = skipped,
                ClaimsSaved = saved,
                ConfirmedOutClaims = savedConfirmedOut,
                DoubtfulClaims = savedDoubtful,
                AvailableClaims = savedAvailable,
                ClaimsAffectingPredictions = affecting,
                RoleMatchedClaims = matched,
                RoleUnknownClaims = affecting - matched,
                ImpactMatchedClaims = impactMatched,
                ImpactFallbackClaims = affecting - impactMatched,
                ContextRowsUpdated = contexts,
                Notes = notes,
                Errors = errors
            };
        }

        public async Task<AvailabilityRefreshReport> RefreshFixtureContextAsync(string fixtureId, CancellationToken ct = default)
        {
            var impactMatched = await RefreshClaimImpactsAsync(ct);
            var updated = await RefreshFixtureContextCountsAsync(fixtureId, [], ct);
            var affecting = await _db.AvailabilityClaims.CountAsync(c => c.AffectsPrediction, ct);
            var matched = await _db.AvailabilityClaims.CountAsync(c => c.AffectsPrediction && c.Position != PlayerPositions.Unknown, ct);
            return new AvailabilityRefreshReport
            {
                IsConfigured = IsConfigured,
                ContextRowsUpdated = updated ? 1 : 0,
                ClaimsAffectingPredictions = affecting,
                RoleMatchedClaims = matched,
                RoleUnknownClaims = affecting - matched,
                ImpactMatchedClaims = impactMatched,
                ImpactFallbackClaims = affecting - impactMatched,
                Notes = updated ? ["Availability context refreshed from news."] : ["Selected match was not found."]
            };
        }

        public async Task<IReadOnlyList<AvailabilityClaim>> ClaimsForFixtureAsync(string fixtureId, CancellationToken ct = default)
        {
            var fixture = await _db.Fixtures.FindAsync([fixtureId], ct);
            if (fixture is null)
                return [];

            var claims = await _db.AvailabilityClaims.AsNoTracking()
                .Where(c => c.TeamId == fixture.HomeTeamId || c.TeamId == fixture.AwayTeamId)
                .ToListAsync(ct);

            return claims
                .OrderBy(c => StatusSort(c))
                .ThenBy(c => c.TeamName)
                .ThenBy(c => c.Player)
                .ToList();
        }

        public async Task<IReadOnlyList<AvailabilityClaim>> AffectingClaimsForTeamsAsync(IEnumerable<string> teamIds, CancellationToken ct = default)
        {
            var teams = teamIds.ToHashSet(StringComparer.Ordinal);
            if (teams.Count == 0)
                return [];

            return await _db.AvailabilityClaims.AsNoTracking()
                .Where(c => c.AffectsPrediction && teams.Contains(c.TeamId))
                .ToListAsync(ct);
        }

        public async Task<bool> RefreshFixtureContextCountsAsync(string fixtureId, IEnumerable<UnavailablePlayerRole> externalUnavailablePlayers, CancellationToken ct = default)
        {
            var fixture = await _db.Fixtures.FindAsync([fixtureId], ct);
            if (fixture is null)
                return false;

            var newsClaims = await AffectingClaimsForTeamsAsync([fixture.HomeTeamId, fixture.AwayTeamId], ct);
            var unavailable = new Dictionary<string, UnavailablePlayerRole>(StringComparer.Ordinal);

            foreach (var player in externalUnavailablePlayers)
            {
                if (player.TeamId == fixture.HomeTeamId || player.TeamId == fixture.AwayTeamId)
                {
                    AddUnavailable(unavailable, player with { Position = NormalizePosition(player.Position) });
                }
            }

            foreach (var claim in newsClaims)
            {
                AddUnavailable(
                    unavailable,
                    new UnavailablePlayerRole(
                        claim.TeamId,
                        claim.PlayerKey,
                        NormalizePosition(claim.Position),
                        claim.AttackImpact,
                        claim.DefenseImpact,
                        claim.Player,
                        claim.ImpactSource));
            }

            var homePlayers = unavailable.Values.Where(p => p.TeamId == fixture.HomeTeamId).ToList();
            var awayPlayers = unavailable.Values.Where(p => p.TeamId == fixture.AwayTeamId).ToList();
            var homeUnavailable = homePlayers.Count;
            var awayUnavailable = awayPlayers.Count;
            var homeImpacts = SumImpacts(homePlayers);
            var awayImpacts = SumImpacts(awayPlayers);
            var context = await _db.FixtureContexts.FindAsync([fixtureId], ct);
            if (context is null)
            {
                context = new FixtureContext { FixtureId = fixtureId };
                _db.FixtureContexts.Add(context);
            }

            var newsHome = newsClaims.Count(c => c.TeamId == fixture.HomeTeamId);
            var newsAway = newsClaims.Count(c => c.TeamId == fixture.AwayTeamId);
            context.UnavailableHomePlayers = homeUnavailable;
            context.UnavailableAwayPlayers = awayUnavailable;
            context.UnavailableHomeAttackImpact = homeImpacts.Attack;
            context.UnavailableHomeDefenseImpact = homeImpacts.Defense;
            context.UnavailableAwayAttackImpact = awayImpacts.Attack;
            context.UnavailableAwayDefenseImpact = awayImpacts.Defense;
            context.HasAvailabilityNews = newsHome + newsAway > 0;
            context.Notes = AppendAvailabilityNote(context.Notes, newsHome, newsAway, newsClaims.Count(c => c.Position != PlayerPositions.Unknown), newsClaims.Count(c => c.Position == PlayerPositions.Unknown));
            context.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public static IReadOnlyList<AvailabilityClaim> ParseClaimsFromJson(string json, string sourceUrl, string? publisher = null)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var array = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("claims", out var claimsElement) && claimsElement.ValueKind == JsonValueKind.Array
                    ? claimsElement
                    : throw new JsonException("Expected an array or an object with a claims array.");

            var claims = new List<AvailabilityClaim>();
            foreach (var item in array.EnumerateArray())
            {
                var player = GetString(item, "player");
                var team = GetString(item, "team");
                if (string.IsNullOrWhiteSpace(player) || string.IsNullOrWhiteSpace(team))
                    continue;

                var status = ParseEnum(GetString(item, "status"), AvailabilityClaimStatus.NotRelevant);
                var evidence = ParseEnum(GetString(item, "evidenceLevel"), AvailabilityEvidenceLevel.Unsupported);
                var quote = GetString(item, "supportingText");
                var position = NormalizePosition(GetString(item, PositionClaimJsonField));
                claims.Add(new AvailabilityClaim
                {
                    Player = player.Trim(),
                    PlayerKey = NormalizePlayerKey(player),
                    TeamName = TeamNameNormalizer.CanonicalName(team),
                    TeamId = TeamNameNormalizer.ToId(team),
                    Status = status,
                    Reason = GetString(item, "reason").Trim(),
                    Confidence = GetString(item, "confidence").Trim(),
                    EvidenceLevel = evidence,
                    SourceUrl = sourceUrl,
                    Publisher = publisher,
                    SupportingQuote = quote.Trim(),
                    ObservedDate = TryParseDate(GetString(item, "publishedOrObservedDate")),
                    Position = position,
                    PositionSource = position == PlayerPositions.Unknown ? PlayerPositions.Unknown : PlayerImpactSources.AvailabilityNews,
                    PositionMatchedAt = position == PlayerPositions.Unknown ? null : DateTimeOffset.UtcNow,
                    AffectsPrediction = false
                });
            }

            return claims;
        }

        public static IReadOnlyList<AvailabilityClaim> ParseTrackerClaims(string text, string sourceUrl, string? publisher = null)
        {
            var claims = new List<AvailabilityClaim>();
            foreach (var rawLine in CandidateTrackerLines(text))
            {
                var line = CleanTrackerLine(rawLine);
                var statusMatch = Regex.Match(line, @"(?<status>MAJOR\s+DOUBTS?|DOUBTS?|OUT|IN)\.?\s*$", RegexOptions.IgnoreCase);
                if (!statusMatch.Success)
                    continue;

                var statusText = Regex.Replace(statusMatch.Groups["status"].Value, @"\s+", " ").Trim();
                var status = TrackerStatus(statusText, line);
                var withoutStatus = line[..statusMatch.Index].Trim().TrimEnd('.');
                var separator = Regex.Match(withoutStatus, @"\s[-–—]\s");
                if (!separator.Success)
                    continue;

                var subject = withoutStatus[..separator.Index].Trim();
                var reason = withoutStatus[(separator.Index + separator.Length)..].Trim();
                var teamComma = subject.LastIndexOf(',');
                if (teamComma < 1 || teamComma >= subject.Length - 1)
                    continue;

                var playersText = subject[..teamComma].Trim();
                var team = subject[(teamComma + 1)..].Trim();
                foreach (var player in SplitTrackerPlayers(playersText))
                {
                    claims.Add(new AvailabilityClaim
                    {
                        Player = player,
                        PlayerKey = NormalizePlayerKey(player),
                        TeamName = TeamNameNormalizer.CanonicalName(team),
                        TeamId = TeamNameNormalizer.ToId(team),
                        Status = status,
                        Reason = reason,
                        Confidence = status == AvailabilityClaimStatus.Available || IsConfirmedOut(status) ? "high" : "medium",
                        EvidenceLevel = AvailabilityEvidenceLevel.ReputableReported,
                        SourceUrl = sourceUrl,
                        Publisher = publisher,
                        SupportingQuote = line,
                        AffectsPrediction = false
                    });
                }
            }

            return claims;
        }

        public static void ApplyPredictionFlags(IEnumerable<AvailabilityClaim> claims, bool requireCrossCheck)
        {
            foreach (var claim in claims)
                claim.AffectsPrediction = false;

            var confirmed = claims
                .Where(c => IsConfirmedOut(c.Status))
                .GroupBy(c => $"{c.TeamId}|{c.PlayerKey}", StringComparer.Ordinal);

            foreach (var group in confirmed)
            {
                var hasOfficial = group.Any(c => c.EvidenceLevel == AvailabilityEvidenceLevel.Official);
                var reputableSourceCount = group
                    .Where(c => c.EvidenceLevel == AvailabilityEvidenceLevel.ReputableReported)
                    .Select(c => PublisherKey(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var shouldAffect = hasOfficial || !requireCrossCheck || reputableSourceCount >= 1;

                if (!shouldAffect)
                    continue;

                foreach (var claim in group)
                    claim.AffectsPrediction = true;
            }
        }

        public static string NormalizePlayerKey(string player) =>
            Regex.Replace(player.ToLowerInvariant().Normalize(NormalizationForm.FormD), @"[^\p{Ll}\p{Nd}]+", "-").Trim('-');

        public static string NormalizePosition(string? position)
        {
            var value = (position ?? "").Trim();
            if (value.Equals(PlayerPositions.Goalkeeper, StringComparison.OrdinalIgnoreCase))
                return PlayerPositions.Goalkeeper;
            if (value.Equals(PlayerPositions.Defender, StringComparison.OrdinalIgnoreCase))
                return PlayerPositions.Defender;
            if (value.Equals(PlayerPositions.Midfielder, StringComparison.OrdinalIgnoreCase))
                return PlayerPositions.Midfielder;
            if (value.Equals(PlayerPositions.Attacker, StringComparison.OrdinalIgnoreCase) || value.Equals(PlayerPositions.Forward, StringComparison.OrdinalIgnoreCase))
                return PlayerPositions.Attacker;

            return PlayerPositions.Unknown;
        }

        public static (double Attack, double Defense) ImpactForPosition(string? position) =>
            NormalizePosition(position) switch
            {
                PlayerPositions.Attacker => (0.035, 0.003),
                PlayerPositions.Midfielder => (0.015, 0.010),
                PlayerPositions.Defender => (0.004, 0.025),
                PlayerPositions.Goalkeeper => (0.000, 0.050),
                _ => (0.020, 0.000)
            };

        public static (double Attack, double Defense) SumImpacts(IEnumerable<string?> positions)
        {
            var attack = 0.0;
            var defense = 0.0;
            foreach (var position in positions)
            {
                var impact = ImpactForPosition(position);
                attack += impact.Attack;
                defense += impact.Defense;
            }

            return (Math.Min(0.18, attack), Math.Min(0.18, defense));
        }

        public static (double Attack, double Defense) SumImpacts(IEnumerable<UnavailablePlayerRole> players)
        {
            var attack = 0.0;
            var defense = 0.0;
            foreach (var player in players)
            {
                var impact = ImpactForPlayer(player);
                attack += impact.Attack;
                defense += impact.Defense;
            }

            return (Math.Min(0.18, attack), Math.Min(0.18, defense));
        }

        public static void ApplyImpact(AvailabilityClaim claim, PlayerImpactResult impact)
        {
            claim.AttackImpact = impact.Attack;
            claim.DefenseImpact = impact.Defense;
            claim.ImpactSource = impact.Source;
            claim.WeightedInternationalGoals = impact.WeightedInternationalGoals;
            claim.ApiGoals = impact.ApiGoals;
            claim.ApiAssists = impact.ApiAssists;
            claim.ApiMinutes = impact.ApiMinutes;
            claim.ApiLineups = impact.ApiLineups;
            claim.ApiRating = impact.ApiRating;
            claim.ImpactMatchedAt = DateTimeOffset.UtcNow;
        }

        private async Task<int> RefreshClaimImpactsAsync(CancellationToken ct)
        {
            var claims = await _db.AvailabilityClaims
                .Where(c => c.AffectsPrediction)
                .ToListAsync(ct);
            var enhanced = 0;

            foreach (var claim in claims)
            {
                var impact = await CalculateImpactAsync(claim.TeamId, claim.Player, claim.PlayerKey, claim.Position, null, ct);
                ApplyImpact(claim, impact);
                if (!PlayerImpactSources.IsFallback(impact.Source))
                    enhanced++;
            }

            if (claims.Count > 0)
                await _db.SaveChangesAsync(ct);

            return enhanced;
        }

        private async Task<PlayerImpactResult> CalculateImpactAsync(
            string teamId,
            string player,
            string playerKey,
            string? position,
            IReadOnlyList<Oloraculo.Web.Models.ApiFootballModels.ApiPlayerStatistic>? apiStatistics,
            CancellationToken ct) =>
            _impact is null
                ? PlayerImpactService.FallbackImpact(position)
                : await _impact.CalculateAsync(teamId, player, playerKey, position, apiStatistics, ct);

        private static void AddUnavailable(Dictionary<string, UnavailablePlayerRole> unavailable, UnavailablePlayerRole player)
        {
            var key = $"{player.TeamId}|{player.PlayerKey}";
            if (!unavailable.TryGetValue(key, out var existing) || ImpactTotal(player) > ImpactTotal(existing))
                unavailable[key] = player;
        }

        private static double ImpactTotal(UnavailablePlayerRole player)
        {
            var impact = ImpactForPlayer(player);
            return impact.Attack + impact.Defense;
        }

        private static (double Attack, double Defense) ImpactForPlayer(UnavailablePlayerRole player) =>
            player.AttackImpact.HasValue || player.DefenseImpact.HasValue
                ? (player.AttackImpact ?? 0.0, player.DefenseImpact ?? 0.0)
                : ImpactForPosition(player.Position);

        private async Task<SourceFetchResult> FetchSourceAsync(string url, CancellationToken ct)
        {
            try
            {
                using var response = await _http.GetAsync(url, ct);
                var html = await response.Content.ReadAsStringAsync(ct);
                var title = ExtractTitle(html);
                var publisher = PublisherFromUrl(url);

                if (!response.IsSuccessStatusCode)
                    return SourceFetchResult.Fail(url, (int)response.StatusCode, title, publisher, $"HTTP {(int)response.StatusCode} al leer {url}.");

                if (LooksBotGated(html))
                    return SourceFetchResult.Fail(url, (int)response.StatusCode, title, publisher, $"{publisher ?? url}: page appears blocked by verification or JavaScript.");

                var text = ExtractReadableText(html);
                if (text.Length > _config.AvailabilityMaxArticleChars)
                    text = text[.._config.AvailabilityMaxArticleChars];

                if (text.Length < 200)
                    return SourceFetchResult.Fail(url, (int)response.StatusCode, title, publisher, $"{publisher ?? url}: insufficient text to classify.");

                return SourceFetchResult.Ok(url, (int)response.StatusCode, title, publisher, text);
            }
            catch (Exception ex)
            {
                return SourceFetchResult.Fail(url, 0, null, PublisherFromUrl(url), ex.Message);
            }
        }

        private async Task<string> ClassifyAsync(SourceFetchResult source, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.OpenRouterApiKey);
            request.Content = JsonContent.Create(new
            {
                model = _config.OpenRouterModel,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = """
                        You extract source-backed football availability claims. Return JSON only:
                        {"claims":[{"player":"","team":"","status":"","reason":"","confidence":"","evidenceLevel":"","supportingText":"","sourceUrl":"","publishedOrObservedDate":""}]}
                        Allowed status values: ConfirmedOutInjury, ConfirmedOutIllness, ConfirmedOutSuspension, ConfirmedOutOther, Doubtful, FitnessConcern, Rumor, Available, NotRelevant.
                        Allowed evidenceLevel values: Official, ReputableReported, ReportedUncertain, Unsupported.
                        Inspect the full article or tracker text, including line-item statuses such as OUT, IN, DOUBT, and MAJOR DOUBT.
                        On a player row, OUT means a confirmed-out status unless nearby text clearly says otherwise. Use the reason text to choose injury, illness, suspension, or other.
                        Use ConfirmedOut* only for clear ruled out, withdrawn, replaced, will miss, suspended, unavailable, or OUT statements. Use Doubtful/FitnessConcern for could miss, race to be fit, doubt, major doubt, or fitness concern. Do not infer beyond the article text.
                        """
                    },
                    new
                    {
                        role = "user",
                        content = $"Source URL: {source.Url}\nPublisher: {source.Publisher}\nTitle: {source.Title}\nArticle text:\n{source.Text}"
                    }
                },
                response_format = new { type = "json_object" }
            }, options: JsonOptions);

            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            return document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? throw new JsonException("OpenRouter response did not include message content.");
        }

        private async Task UpsertSourceAsync(string url, SourceFetchResult fetch, CancellationToken ct)
        {
            var source = await _db.AvailabilitySources.SingleOrDefaultAsync(s => s.Url == url, ct);
            if (source is null)
            {
                source = new AvailabilitySource { Url = url };
                _db.AvailabilitySources.Add(source);
            }

            source.Title = fetch.Title;
            source.Publisher = fetch.Publisher;
            source.StatusCode = fetch.StatusCode;
            source.TextHash = fetch.Text is null ? null : Hash(fetch.Text);
            source.Error = fetch.Error;
            source.LastFetchedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        private async Task ReplaceClaimsForSourceAsync(string url, IReadOnlyList<AvailabilityClaim> claims, CancellationToken ct)
        {
            var existing = await _db.AvailabilityClaims.Where(c => c.SourceUrl == url).ToListAsync(ct);
            _db.AvailabilityClaims.RemoveRange(existing);
            _db.AvailabilityClaims.AddRange(claims);
            await _db.SaveChangesAsync(ct);
        }

        private async Task RecomputePredictionFlagsAsync(CancellationToken ct)
        {
            var claims = await _db.AvailabilityClaims.ToListAsync(ct);
            ApplyPredictionFlags(claims, _config.AvailabilityRequireCrossCheck);
            await _db.SaveChangesAsync(ct);
        }

        private async Task<int> RefreshAllFixtureContextsAsync(CancellationToken ct)
        {
            var fixtures = await _db.Fixtures.AsNoTracking().ToListAsync(ct);
            var updated = 0;
            foreach (var fixture in fixtures)
            {
                if (await RefreshFixtureContextCountsAsync(fixture.Id, [], ct))
                    updated++;
            }

            return updated;
        }

        private static string AppendAvailabilityNote(string existing, int home, int away, int matched, int unknown)
        {
            var prefix = string.IsNullOrWhiteSpace(existing) ? "" : existing.Split(" Noticias:")[0].Trim();
            var note = $"News: confirmed absences Team A {home}, Team B {away}; matched roles {matched}; unknown roles {unknown}.";
            return string.IsNullOrWhiteSpace(prefix) ? note : $"{prefix} {note}";
        }

        private static bool IsConfirmedOut(AvailabilityClaimStatus status) =>
            status is AvailabilityClaimStatus.ConfirmedOutInjury
                or AvailabilityClaimStatus.ConfirmedOutIllness
                or AvailabilityClaimStatus.ConfirmedOutSuspension
                or AvailabilityClaimStatus.ConfirmedOutOther;

        private static int StatusSort(AvailabilityClaim claim)
        {
            if (claim.AffectsPrediction)
                return 0;

            if (IsConfirmedOut(claim.Status))
                return 1;

            return claim.Status switch
            {
                AvailabilityClaimStatus.Doubtful or AvailabilityClaimStatus.FitnessConcern => 2,
                AvailabilityClaimStatus.Rumor => 3,
                AvailabilityClaimStatus.Available => 4,
                _ => 5
            };
        }

        private static IEnumerable<string> CandidateTrackerLines(string text)
        {
            foreach (var line in Regex.Split(text ?? "", @"\r?\n+"))
            {
                var cleaned = CleanTrackerLine(line);
                if (!string.IsNullOrWhiteSpace(cleaned))
                    yield return cleaned;
            }
        }

        private static string CleanTrackerLine(string line)
        {
            var cleaned = WebUtility.HtmlDecode(line ?? "");
            cleaned = Regex.Replace(cleaned, @"^[\s\-*•]+", "");
            return Regex.Replace(cleaned, @"\s+", " ").Trim();
        }

        private static AvailabilityClaimStatus TrackerStatus(string status, string line)
        {
            if (status.Equals("IN", StringComparison.OrdinalIgnoreCase))
                return AvailabilityClaimStatus.Available;

            if (Regex.IsMatch(status, @"DOUBT", RegexOptions.IgnoreCase))
                return AvailabilityClaimStatus.Doubtful;

            var lower = line.ToLowerInvariant();
            if (lower.Contains("suspension") || lower.Contains("suspended"))
                return AvailabilityClaimStatus.ConfirmedOutSuspension;
            if (lower.Contains("illness") || lower.Contains("virus") || lower.Contains("sick"))
                return AvailabilityClaimStatus.ConfirmedOutIllness;
            if (lower.Contains("visa") || lower.Contains("personal reasons") || lower.Contains("disciplinary"))
                return AvailabilityClaimStatus.ConfirmedOutOther;

            return AvailabilityClaimStatus.ConfirmedOutInjury;
        }

        private static IEnumerable<string> SplitTrackerPlayers(string playersText)
        {
            var normalized = Regex.Replace(playersText, @"\s+and\s+", ", ", RegexOptions.IgnoreCase);
            foreach (var player in normalized.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (player.Length > 1)
                    yield return player;
            }
        }

        private static string PublisherKey(AvailabilityClaim claim) =>
            string.IsNullOrWhiteSpace(claim.Publisher) ? claim.SourceUrl : claim.Publisher;

        private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum
        {
            var cleaned = Regex.Replace(value ?? "", @"[\s_\-]+", "");
            foreach (var name in Enum.GetNames<T>())
            {
                if (string.Equals(Regex.Replace(name, @"[\s_\-]+", ""), cleaned, StringComparison.OrdinalIgnoreCase))
                    return Enum.Parse<T>(name);
            }

            return fallback;
        }

        private static string GetString(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

        private static DateTimeOffset? TryParseDate(string value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

        private static string ExtractTitle(string html)
        {
            var match = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, @"\s+", " ").Trim()) : "";
        }

        private static string ExtractReadableText(string html)
        {
            var selectedHtml = ExtractElementContent(html, "article")
                ?? ExtractElementContent(html, "main")
                ?? ExtractElementContent(html, "body")
                ?? html;

            var text = Regex.Replace(selectedHtml, @"<script\b[^<]*(?:(?!</script>)<[^<]*)*</script>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<style\b[^<]*(?:(?!</style>)<[^<]*)*</style>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</?(p|div|section|article|li|ul|ol|tr|table|tbody|thead|h[1-6]|br)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"[ \t\f\v]+", " ");
            return Regex.Replace(text, @"\s*\r?\n\s*", "\n").Trim();
        }

        private static string? ExtractElementContent(string html, string tagName)
        {
            var matches = Regex.Matches(
                html ?? "",
                $@"<\s*{tagName}\b[^>]*>(?<content>.*?)<\s*/\s*{tagName}\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (matches.Count == 0)
                return null;

            var content = string.Join("\n", matches
                .Select(m => m.Groups["content"].Value)
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }

        private static bool LooksBotGated(string html)
        {
            var text = html.ToLowerInvariant();
            return text.Contains("enable javascript")
                || text.Contains("verify you are a human")
                || text.Contains("bot detection")
                || text.Contains("captcha");
        }

        private static string? PublisherFromUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;

            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            return host;
        }

        private static string Hash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        private sealed record SourceFetchResult(
            string Url,
            int StatusCode,
            string? Title,
            string? Publisher,
            string? Text,
            string? Error)
        {
            public bool Success => Error is null;

            public static SourceFetchResult Ok(string url, int statusCode, string? title, string? publisher, string text) =>
                new(url, statusCode, title, publisher, text, null);

            public static SourceFetchResult Fail(string url, int statusCode, string? title, string? publisher, string error) =>
                new(url, statusCode, title, publisher, null, error);
        }
    }

    public sealed record UnavailablePlayerRole(
        string TeamId,
        string PlayerKey,
        string Position,
        double? AttackImpact = null,
        double? DefenseImpact = null,
        string Player = "",
        string ImpactSource = PlayerImpactSources.Position);
}
