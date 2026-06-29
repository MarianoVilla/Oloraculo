using Microsoft.EntityFrameworkCore;
using Oloraculo.Web.DAL;
using Oloraculo.Web.Helpers;
using Oloraculo.Web.Models;
using Oloraculo.Web.Probability;
using System.Data;
using System.Globalization;
using System.Text.Json;

namespace Oloraculo.Web.Services
{
    public class SnapshotService
    {
        private const string MatchKind = "match";
        private const string TournamentKind = "tournament";
        private const string FullFixtureKind = "full-fixture";

        private readonly OloraculoDbContext _db;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        public SnapshotService(OloraculoDbContext db) => _db = db;

        public async Task<PredictionSnapshot> SaveMatchAsync(MatchPrediction prediction, CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            var snapshot = CreateMatchSnapshot(prediction, DateTimeOffset.UtcNow, batchId: null);
            _db.Snapshots.Add(snapshot);
            await _db.SaveChangesAsync(ct);
            return snapshot;
        }

        public async Task<IReadOnlyList<PredictionSnapshot>> SaveMatchesAsync(IEnumerable<MatchPrediction> predictions, CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var snapshots = predictions.Select(prediction => CreateMatchSnapshot(prediction, now, batchId: null)).ToList();
            _db.Snapshots.AddRange(snapshots);
            await _db.SaveChangesAsync(ct);
            return snapshots;
        }

        public async Task<PredictionSnapshot> SaveFullFixtureAsync(IEnumerable<MatchPrediction> predictions, CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            var predictionList = predictions.ToList();
            if (predictionList.Count == 0)
                throw new InvalidOperationException("No hay predicciones para guardar.");

            var fixtureIds = predictionList.Select(p => p.FixtureId).ToList();
            if (fixtureIds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Todas las predicciones deben tener fixture.");
            if (fixtureIds.Distinct(StringComparer.Ordinal).Count() != fixtureIds.Count)
                throw new InvalidOperationException("No se puede guardar un fixture completo con partidos duplicados.");

            var now = DateTimeOffset.UtcNow;
            var modelName = predictionList.Select(p => p.PredictorName).Distinct(StringComparer.Ordinal).SingleOrDefault() ?? "Fixture completo";
            var payload = JsonSerializer.Serialize(new FullFixtureSnapshotPayload
            {
                SavedAt = now,
                FixtureIds = fixtureIds,
                Count = predictionList.Count
            }, JsonOptions);

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var batch = new PredictionSnapshot
            {
                Kind = FullFixtureKind,
                ModelName = modelName,
                CreatedAt = now,
                InputSummaryHash = CryptoUtil.GetSha256($"full-fixture|{now:O}|{string.Join("|", fixtureIds)}"),
                PayloadJson = payload,
                Explanation = $"{predictionList.Count} predicciones de fixture guardadas como lote.",
                HomeWin = 0,
                Draw = 0,
                AwayWin = 0
            };

            _db.Snapshots.Add(batch);
            await _db.SaveChangesAsync(ct);

            var matchSnapshots = predictionList
                .Select(prediction => CreateMatchSnapshot(prediction, now, batch.Id))
                .ToList();

            _db.Snapshots.AddRange(matchSnapshots);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return batch;
        }

        private static PredictionSnapshot CreateMatchSnapshot(MatchPrediction prediction, DateTimeOffset createdAt, int? batchId)
        {
            var payload = JsonSerializer.Serialize(new
            {
                prediction.PredictorName,
                prediction.PredictorPriority,
                prediction.FixtureId,
                prediction.HomeTeamId,
                prediction.AwayTeamId,
                Outcome = prediction.Outcome,
                prediction.ExpectedHomeGoals,
                prediction.ExpectedAwayGoals,
                MostLikelyScore = prediction.MostLikelyScore is null ? null : new ScorePayload(prediction.MostLikelyScore.Value.Home, prediction.MostLikelyScore.Value.Away),
                prediction.Explanation,
                prediction.FeaturesUsed,
                prediction.FeaturesMissing,
                prediction.Drivers,
                prediction.Sources,
                prediction.Degraded
            }, JsonOptions);

            return new PredictionSnapshot
            {
                Kind = MatchKind,
                BatchId = batchId,
                FixtureId = prediction.FixtureId,
                ModelName = prediction.PredictorName,
                CreatedAt = createdAt,
                InputSummaryHash = CryptoUtil.GetSha256($"{prediction.FixtureId}|{prediction.PredictorName}|{createdAt:O}"),
                PayloadJson = payload,
                Explanation = prediction.Explanation,
                HomeWin = prediction.Outcome.HomeWin,
                Draw = prediction.Outcome.Draw,
                AwayWin = prediction.Outcome.AwayWin
            };
        }

        public async Task<IReadOnlyList<FullFixtureSnapshotSummary>> FullFixtureSnapshotsAsync(int? take = null, CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            var batches = await _db.Snapshots
                .AsNoTracking()
                .Where(s => s.Kind == FullFixtureKind)
                .ToListAsync(ct);

            var batchIds = batches.Select(s => s.Id).ToList();
            var childCounts = await _db.Snapshots
                .AsNoTracking()
                .Where(s => s.Kind == MatchKind && s.BatchId.HasValue && batchIds.Contains(s.BatchId.Value))
                .GroupBy(s => s.BatchId!.Value)
                .Select(g => new { BatchId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.BatchId, g => g.Count, ct);

            IEnumerable<PredictionSnapshot> ordered = batches
                .OrderByDescending(s => s.CreatedAt)
                .ThenByDescending(s => s.Id);

            if (take is > 0)
                ordered = ordered.Take(take.Value);

            return ordered
                .Select(snapshot => ToFullFixtureSummary(snapshot, childCounts.GetValueOrDefault(snapshot.Id)))
                .ToList();
        }

        public async Task<FullFixtureSnapshotLoadResult> LoadFullFixtureSnapshotAsync(int id, CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            var batch = await _db.Snapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Kind == FullFixtureKind && s.Id == id, ct);

            if (batch is null)
                return new FullFixtureSnapshotLoadResult([], "No se encontró el snapshot de fixture completo.");

            var payload = DeserializeFullFixturePayload(batch.PayloadJson, out var error);
            if (payload is null)
                return new FullFixtureSnapshotLoadResult([], error ?? "No se pudo leer el snapshot de fixture completo.");

            var snapshots = await _db.Snapshots
                .AsNoTracking()
                .Where(s => s.Kind == MatchKind && s.BatchId == batch.Id)
                .ToListAsync(ct);

            if (snapshots.Count != payload.FixtureIds.Count)
                return new FullFixtureSnapshotLoadResult([], "El snapshot de fixture completo está incompleto.");

            var order = payload.FixtureIds
                .Select((fixtureId, index) => new { fixtureId, index })
                .ToDictionary(item => item.fixtureId, item => item.index, StringComparer.Ordinal);

            var results = new List<MatchPredictionResult>(snapshots.Count);
            foreach (var snapshot in snapshots.OrderBy(s => order.TryGetValue(s.FixtureId ?? "", out var index) ? index : int.MaxValue).ThenBy(s => s.Id))
            {
                var result = await ToMatchPredictionResultAsync(snapshot, ct);
                if (!result.IsValid || result.Prediction is null)
                    return new FullFixtureSnapshotLoadResult([], result.Error ?? "No se pudo leer una predicción del lote.");

                results.Add(result.Prediction);
            }

            return new FullFixtureSnapshotLoadResult(results, null);
        }

        public async Task<IReadOnlyList<MatchSnapshotSummary>> MatchSnapshotsAsync(string fixtureId, int? take = null, CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            IEnumerable<PredictionSnapshot> snapshots = await _db.Snapshots
                .AsNoTracking()
                .Where(s => s.Kind == MatchKind && s.FixtureId == fixtureId)
                .ToListAsync(ct);

            snapshots = snapshots
                .OrderByDescending(s => s.CreatedAt)
                .ThenByDescending(s => s.Id);

            if (take is > 0)
                snapshots = snapshots.Take(take.Value);

            return snapshots.Select(ToMatchSummary).ToList();
        }

        public async Task<MatchSnapshotLoadResult> LoadLatestMatchSnapshotAsync(string fixtureId, CancellationToken ct = default)
        {
            var latest = (await MatchSnapshotsAsync(fixtureId, ct: ct)).FirstOrDefault(snapshot => snapshot.IsValid);
            return latest is null
                ? new MatchSnapshotLoadResult(null, "No hay snapshots guardados para este partido.")
                : await LoadMatchSnapshotAsync(latest.Id, ct);
        }

        public async Task<MatchSnapshotLoadResult> LoadLatestMatchSnapshotAtOrBeforeAsync(
            string fixtureId,
            DateTimeOffset cutoff,
            CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            var snapshots = await _db.Snapshots
                .AsNoTracking()
                .Where(s =>
                    s.Kind == MatchKind &&
                    s.FixtureId == fixtureId &&
                    s.HomeWin.HasValue &&
                    s.Draw.HasValue &&
                    s.AwayWin.HasValue)
                .ToListAsync(ct);

            var snapshot = snapshots
                .Where(s => s.CreatedAt <= cutoff)
                .OrderByDescending(s => s.CreatedAt)
                .ThenByDescending(s => s.Id)
                .FirstOrDefault();

            return snapshot is null
                ? new MatchSnapshotLoadResult(null, "No hay snapshots guardados para este partido antes del corte.")
                : await ToMatchPredictionResultAsync(snapshot, ct);
        }

        public async Task<MatchSnapshotLoadResult> LoadMatchSnapshotAsync(int id, CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            var snapshot = await _db.Snapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Kind == MatchKind && s.Id == id, ct);

            if (snapshot is null)
                return new MatchSnapshotLoadResult(null, "No se encontró el snapshot de partido.");

            return await ToMatchPredictionResultAsync(snapshot, ct);
        }

        public async Task<PredictionSnapshot> SaveTournamentAsync(TournamentProjection projection, CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            var payload = JsonSerializer.Serialize(projection, JsonOptions);
            var snapshot = new PredictionSnapshot
            {
                Kind = TournamentKind,
                ModelName = projection.ModelName,
                CreatedAt = projection.GeneratedAt,
                InputSummaryHash = projection.InputSummaryHash,
                PayloadJson = payload,
                Explanation = $"{projection.Simulations:N0} simulaciones usando {projection.ModelName}.",
                HomeWin = 0,
                Draw = 0,
                AwayWin = 0
            };
            _db.Snapshots.Add(snapshot);
            await _db.SaveChangesAsync(ct);
            return snapshot;
        }

        public async Task<IReadOnlyList<TournamentSnapshotSummary>> TournamentSnapshotsAsync(int? take = null, CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            var snapshots = await _db.Snapshots
                .AsNoTracking()
                .Where(s => s.Kind == TournamentKind)
                .ToListAsync(ct);

            IEnumerable<PredictionSnapshot> ordered = snapshots
                .OrderByDescending(s => s.CreatedAt)
                .ThenByDescending(s => s.Id);

            if (take is > 0)
                ordered = ordered.Take(take.Value);

            return ordered.Select(ToTournamentSummary).ToList();
        }

        public async Task<TournamentSnapshotLoadResult> LoadTournamentSnapshotAsync(int id, CancellationToken ct = default)
        {
            await EnsureSnapshotColumnsAsync(ct);
            var snapshot = await _db.Snapshots
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Kind == TournamentKind && s.Id == id, ct);

            if (snapshot is null)
                return new TournamentSnapshotLoadResult(null, "No se encontró el snapshot de torneo.");

            var projection = DeserializeTournamentProjection(snapshot.PayloadJson, out var error);
            if (projection is null)
                return new TournamentSnapshotLoadResult(null, error ?? "No se pudo leer el snapshot de torneo.");

            return new TournamentSnapshotLoadResult(projection, null);
        }

        private async Task<MatchSnapshotLoadResult> ToMatchPredictionResultAsync(PredictionSnapshot snapshot, CancellationToken ct)
        {
            var stored = DeserializeMatchPayloadBestEffort(snapshot.PayloadJson);
            var fixtureId = FirstNonEmpty(stored.FixtureId, snapshot.FixtureId);
            if (string.IsNullOrWhiteSpace(fixtureId))
                return new MatchSnapshotLoadResult(null, "El snapshot no tiene fixture asociado.");

            var fixture = await _db.Fixtures.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fixtureId, ct);
            var homeTeamId = FirstNonEmpty(stored.HomeTeamId, fixture?.HomeTeamId);
            var awayTeamId = FirstNonEmpty(stored.AwayTeamId, fixture?.AwayTeamId);
            if (string.IsNullOrWhiteSpace(homeTeamId) || string.IsNullOrWhiteSpace(awayTeamId))
                return new MatchSnapshotLoadResult(null, "El snapshot no tiene equipos suficientes para reconstruir la predicción.");

            fixture ??= new Fixture
            {
                Id = fixtureId,
                HomeTeamId = homeTeamId,
                AwayTeamId = awayTeamId,
                NeutralVenue = true
            };

            var outcome = stored.Outcome ?? SnapshotOutcome(snapshot);
            if (outcome is null || !outcome.Value.IsValid)
                return new MatchSnapshotLoadResult(null, "El snapshot no contiene probabilidades válidas.");

            var normalized = outcome.Value.Normalize();
            var prediction = new MatchPrediction
            {
                PredictorName = FirstNonEmpty(stored.PredictorName, snapshot.ModelName) ?? "Snapshot",
                PredictorPriority = stored.PredictorPriority ?? 0,
                FixtureId = fixtureId,
                HomeTeamId = homeTeamId,
                AwayTeamId = awayTeamId,
                Outcome = normalized,
                ExpectedHomeGoals = stored.ExpectedHomeGoals,
                ExpectedAwayGoals = stored.ExpectedAwayGoals,
                MostLikelyScore = stored.MostLikelyScore,
                Explanation = FirstNonEmpty(stored.Explanation, snapshot.Explanation) ?? "",
                FeaturesUsed = stored.FeaturesUsed ?? [],
                FeaturesMissing = stored.FeaturesMissing ?? [],
                Drivers = stored.Drivers ?? [],
                Sources = stored.Sources ?? [],
                Degraded = stored.Degraded ?? false
            };

            var result = new MatchPredictionResult
            {
                Fixture = fixture,
                HomeTeamName = await TeamNameAsync(homeTeamId, ct),
                AwayTeamName = await TeamNameAsync(awayTeamId, ct),
                Predictions = [prediction],
                BestPrediction = prediction
            };

            return new MatchSnapshotLoadResult(result, null);
        }

        private async Task<string> TeamNameAsync(string teamId, CancellationToken ct)
        {
            var team = await _db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teamId, ct);
            return team?.Name ?? teamId;
        }

        private static MatchSnapshotSummary ToMatchSummary(PredictionSnapshot snapshot)
        {
            var error = string.IsNullOrWhiteSpace(snapshot.FixtureId)
                ? "El snapshot no tiene fixture asociado."
                : SnapshotOutcome(snapshot) is null
                    ? "El snapshot no contiene probabilidades válidas."
                    : null;

            return new MatchSnapshotSummary(
                snapshot.Id,
                snapshot.CreatedAt,
                snapshot.ModelName,
                snapshot.FixtureId ?? "",
                snapshot.InputSummaryHash,
                snapshot.BatchId,
                error);
        }

        private static FullFixtureSnapshotSummary ToFullFixtureSummary(PredictionSnapshot snapshot, int childCount)
        {
            var payload = DeserializeFullFixturePayload(snapshot.PayloadJson, out var error);
            return new FullFixtureSnapshotSummary(
                snapshot.Id,
                snapshot.CreatedAt,
                snapshot.ModelName,
                snapshot.InputSummaryHash,
                childCount > 0 ? childCount : payload?.Count ?? payload?.FixtureIds.Count ?? 0,
                error);
        }

        private static TournamentSnapshotSummary ToTournamentSummary(PredictionSnapshot snapshot)
        {
            var projection = DeserializeTournamentProjection(snapshot.PayloadJson, out var error);
            return new TournamentSnapshotSummary(
                snapshot.Id,
                snapshot.CreatedAt,
                snapshot.ModelName,
                snapshot.InputSummaryHash,
                projection?.Simulations,
                error);
        }

        private static FullFixtureSnapshotPayload? DeserializeFullFixturePayload(string payloadJson, out string? error)
        {
            error = null;

            try
            {
                var payload = JsonSerializer.Deserialize<FullFixtureSnapshotPayload>(payloadJson, JsonOptions);
                if (payload is null)
                {
                    error = "El snapshot no contiene un lote de fixture completo.";
                    return null;
                }

                if (payload.FixtureIds.Count == 0)
                {
                    error = "El snapshot de fixture completo no contiene partidos.";
                    return null;
                }

                return payload;
            }
            catch (JsonException)
            {
                error = "El snapshot guardado no tiene un formato válido.";
                return null;
            }
        }

        private static TournamentProjection? DeserializeTournamentProjection(string payloadJson, out string? error)
        {
            error = null;

            try
            {
                var projection = JsonSerializer.Deserialize<TournamentProjection>(payloadJson, JsonOptions);
                if (projection is null)
                {
                    error = "El snapshot no contiene una proyección de torneo.";
                    return null;
                }

                return projection;
            }
            catch (JsonException)
            {
                error = "El snapshot guardado no tiene un formato válido.";
                return null;
            }
        }

        private static StoredMatchPrediction DeserializeMatchPayloadBestEffort(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
                return new StoredMatchPrediction();

            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                var root = document.RootElement;
                return new StoredMatchPrediction
                {
                    PredictorName = ReadString(root, "PredictorName"),
                    PredictorPriority = ReadInt(root, "PredictorPriority"),
                    FixtureId = ReadString(root, "FixtureId"),
                    HomeTeamId = ReadString(root, "HomeTeamId"),
                    AwayTeamId = ReadString(root, "AwayTeamId"),
                    Outcome = ReadOutcome(root),
                    ExpectedHomeGoals = ReadDouble(root, "ExpectedHomeGoals"),
                    ExpectedAwayGoals = ReadDouble(root, "ExpectedAwayGoals"),
                    MostLikelyScore = ReadScore(root, "MostLikelyScore"),
                    Explanation = ReadString(root, "Explanation"),
                    FeaturesUsed = ReadStringList(root, "FeaturesUsed"),
                    FeaturesMissing = ReadStringList(root, "FeaturesMissing"),
                    Drivers = ReadStringList(root, "Drivers"),
                    Sources = ReadSources(root, "Sources"),
                    Degraded = ReadBool(root, "Degraded")
                };
            }
            catch (JsonException)
            {
                return new StoredMatchPrediction();
            }
        }

        private static OutcomeProbabilities? SnapshotOutcome(PredictionSnapshot snapshot)
        {
            if (snapshot.HomeWin is null || snapshot.Draw is null || snapshot.AwayWin is null)
                return null;

            var outcome = new OutcomeProbabilities(snapshot.HomeWin.Value, snapshot.Draw.Value, snapshot.AwayWin.Value);
            return outcome.IsValid ? outcome.Normalize() : null;
        }

        private static OutcomeProbabilities? ReadOutcome(JsonElement root)
        {
            if (!TryGetProperty(root, "Outcome", out var outcomeElement) || outcomeElement.ValueKind != JsonValueKind.Object)
                return null;

            var home = ReadDouble(outcomeElement, "HomeWin");
            var draw = ReadDouble(outcomeElement, "Draw");
            var away = ReadDouble(outcomeElement, "AwayWin");
            if (home is null || draw is null || away is null)
                return null;

            var outcome = new OutcomeProbabilities(home.Value, draw.Value, away.Value);
            return outcome.IsValid ? outcome.Normalize() : null;
        }

        private static (int Home, int Away)? ReadScore(JsonElement root, string propertyName)
        {
            if (!TryGetProperty(root, propertyName, out var scoreElement))
                return null;

            if (scoreElement.ValueKind == JsonValueKind.Object)
            {
                var home = ReadInt(scoreElement, "Home");
                var away = ReadInt(scoreElement, "Away");
                return home.HasValue && away.HasValue ? (home.Value, away.Value) : null;
            }

            if (scoreElement.ValueKind == JsonValueKind.String)
            {
                var text = scoreElement.GetString();
                var parts = text?.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts?.Length == 2 && int.TryParse(parts[0], out var home) && int.TryParse(parts[1], out var away))
                    return (home, away);
            }

            return null;
        }

        private static IReadOnlyList<string>? ReadStringList(JsonElement root, string propertyName)
        {
            if (!TryGetProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
                return null;

            var values = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }

            return values;
        }

        private static IReadOnlyList<SourceMetadata>? ReadSources(JsonElement root, string propertyName)
        {
            if (!TryGetProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
                return null;

            var sources = new List<SourceMetadata>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        sources.Add(new SourceMetadata(value, "snapshot"));
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var name = ReadString(item, "Name");
                var kind = ReadString(item, "Kind");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kind))
                    continue;

                DateTimeOffset? fetchedAt = null;
                var fetchedText = ReadString(item, "FetchedAt");
                if (DateTimeOffset.TryParse(fetchedText, out var parsedFetchedAt))
                    fetchedAt = parsedFetchedAt;

                sources.Add(new SourceMetadata(name, kind, fetchedAt, ReadString(item, "Notes")));
            }

            return sources;
        }

        private static string? ReadString(JsonElement root, string propertyName)
        {
            if (!TryGetProperty(root, propertyName, out var element))
                return null;

            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        private static int? ReadInt(JsonElement root, string propertyName)
        {
            if (!TryGetProperty(root, propertyName, out var element))
                return null;

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
                return value;

            return element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value) ? value : null;
        }

        private static double? ReadDouble(JsonElement root, string propertyName)
        {
            if (!TryGetProperty(root, propertyName, out var element))
                return null;

            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value))
                return value;

            return element.ValueKind == JsonValueKind.String &&
                double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    ? value
                    : null;
        }

        private static bool? ReadBool(JsonElement root, string propertyName)
        {
            if (!TryGetProperty(root, propertyName, out var element))
                return null;

            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(element.GetString(), out var value) => value,
                _ => null
            };
        }

        private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static string? FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        private async Task EnsureSnapshotColumnsAsync(CancellationToken ct)
        {
            var connection = _db.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
                await connection.OpenAsync(ct);

            try
            {
                var snapshotColumns = await ColumnsAsync("Snapshots", ct);
                if (snapshotColumns.Count > 0 && !snapshotColumns.Contains("BatchId"))
                    await ExecuteSchemaAsync("""ALTER TABLE "Snapshots" ADD COLUMN "BatchId" INTEGER NULL""", ct);

                if (snapshotColumns.Count > 0)
                    await ExecuteSchemaAsync("""CREATE INDEX IF NOT EXISTS "IX_Snapshots_Kind_BatchId_CreatedAt" ON "Snapshots" ("Kind", "BatchId", "CreatedAt")""", ct);
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }

            async Task<HashSet<string>> ColumnsAsync(string table, CancellationToken token)
            {
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                await using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info(\"{table}\")";
                await using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                    columns.Add(reader.GetString(1));
                return columns;
            }

            async Task ExecuteSchemaAsync(string sql, CancellationToken token)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(token);
            }
        }

        private sealed class FullFixtureSnapshotPayload
        {
            public DateTimeOffset SavedAt { get; set; }
            public IReadOnlyList<string> FixtureIds { get; set; } = [];
            public int Count { get; set; }
        }

        private sealed record ScorePayload(int Home, int Away);

        private sealed class StoredMatchPrediction
        {
            public string? PredictorName { get; init; }
            public int? PredictorPriority { get; init; }
            public string? FixtureId { get; init; }
            public string? HomeTeamId { get; init; }
            public string? AwayTeamId { get; init; }
            public OutcomeProbabilities? Outcome { get; init; }
            public double? ExpectedHomeGoals { get; init; }
            public double? ExpectedAwayGoals { get; init; }
            public (int Home, int Away)? MostLikelyScore { get; init; }
            public string? Explanation { get; init; }
            public IReadOnlyList<string>? FeaturesUsed { get; init; }
            public IReadOnlyList<string>? FeaturesMissing { get; init; }
            public IReadOnlyList<string>? Drivers { get; init; }
            public IReadOnlyList<SourceMetadata>? Sources { get; init; }
            public bool? Degraded { get; init; }
        }
    }
}
