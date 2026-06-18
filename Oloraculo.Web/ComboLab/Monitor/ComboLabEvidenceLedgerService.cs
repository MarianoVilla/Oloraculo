using Microsoft.AspNetCore.Hosting;
using Oloraculo.Web.Archive;
using Oloraculo.Web.Helpers;
using System.Text;
using System.Text.Json;

namespace Oloraculo.Web.ComboLab.Monitor
{
    public sealed record ComboLabEvidenceLedgerEntry(
        DateTimeOffset SavedAtUtc,
        string EntryId,
        string CheckpointKind,
        string Schema,
        string Mode,
        string Verdict,
        string SnapshotHash,
        string PayloadHash,
        string LiveOrderPath,
        string Summary,
        string PayloadJson);

    public sealed record ComboLabEvidenceLedgerSnapshot(
        DateTimeOffset LoadedAtUtc,
        string LedgerPath,
        IReadOnlyList<ComboLabEvidenceLedgerEntry> Entries,
        IReadOnlyList<string> Rejects)
    {
        public int LiveEntries => Entries.Count(entry => string.Equals(entry.CheckpointKind, ComboLabEvidenceLedgerService.LiveCheckpointKind, StringComparison.Ordinal));
        public int BatchEntries => Entries.Count(entry => string.Equals(entry.CheckpointKind, ComboLabEvidenceLedgerService.BatchCheckpointKind, StringComparison.Ordinal));
        public bool HasRejects => Rejects.Count > 0;
    }

    public sealed class ComboLabEvidenceLedgerService
    {
        public const string LiveCheckpointKind = "live-lock";
        public const string BatchCheckpointKind = "batch-lock";
        public const string LedgerFileName = "combo_lab_evidence_ledger.jsonl";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly SemaphoreSlim FileLock = new(1, 1);
        private readonly string _ledgerPath;
        private readonly IObjectArchiveService? _archive;

        public ComboLabEvidenceLedgerService(IWebHostEnvironment environment)
            : this(environment, null)
        {
        }

        public ComboLabEvidenceLedgerService(IWebHostEnvironment environment, IObjectArchiveService? archive)
        {
            ArgumentNullException.ThrowIfNull(environment);
            _ledgerPath = Path.Combine(environment.ContentRootPath, "Data", LedgerFileName);
            _archive = archive;
        }

        public string LedgerPath => _ledgerPath;

        public Task<ComboLabEvidenceLedgerEntry> SaveAsync(LiveComplementaryLockCheckpoint checkpoint, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(checkpoint);
            return SaveAsync(LiveCheckpointKind, checkpoint.Verdict, checkpoint.SnapshotHash, checkpoint.PayloadJson, ct);
        }

        public Task<ComboLabEvidenceLedgerEntry> SaveAsync(LiveComplementaryBatchScanCheckpoint checkpoint, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(checkpoint);
            return SaveAsync(BatchCheckpointKind, checkpoint.Verdict, checkpoint.SnapshotHash, checkpoint.PayloadJson, ct);
        }

        public async Task<ComboLabEvidenceLedgerSnapshot> LoadAsync(CancellationToken ct = default)
        {
            if (!File.Exists(_ledgerPath))
            {
                return new ComboLabEvidenceLedgerSnapshot(
                    DateTimeOffset.UtcNow,
                    _ledgerPath,
                    Entries: [],
                    Rejects: []);
            }

            var entries = new List<ComboLabEvidenceLedgerEntry>();
            var rejects = new List<string>();
            var lines = await File.ReadAllLinesAsync(_ledgerPath, ct);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<ComboLabEvidenceLedgerEntry>(line, JsonOptions);
                    if (entry is null)
                    {
                        rejects.Add($"line {index + 1}: empty ledger entry");
                        continue;
                    }

                    var payloadHash = CryptoUtil.GetSha256(entry.PayloadJson);
                    if (!string.Equals(payloadHash, entry.PayloadHash, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(payloadHash, entry.SnapshotHash, StringComparison.OrdinalIgnoreCase))
                    {
                        rejects.Add($"line {index + 1}: checkpoint hash mismatch");
                        continue;
                    }

                    entries.Add(entry);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    rejects.Add($"line {index + 1}: {ex.Message}");
                }
            }

            return new ComboLabEvidenceLedgerSnapshot(
                DateTimeOffset.UtcNow,
                _ledgerPath,
                entries
                    .DistinctBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(entry => entry.SavedAtUtc)
                    .ThenBy(entry => entry.CheckpointKind, StringComparer.Ordinal)
                    .ThenBy(entry => entry.SnapshotHash, StringComparer.Ordinal)
                    .ToList(),
                rejects);
        }

        private async Task<ComboLabEvidenceLedgerEntry> SaveAsync(
            string checkpointKind,
            string verdict,
            string snapshotHash,
            string payloadJson,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(snapshotHash))
                throw new ArgumentException("Snapshot hash is required.", nameof(snapshotHash));
            if (string.IsNullOrWhiteSpace(payloadJson))
                throw new ArgumentException("Payload JSON is required.", nameof(payloadJson));

            var payloadHash = CryptoUtil.GetSha256(payloadJson);
            if (!string.Equals(payloadHash, snapshotHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Checkpoint hash does not match payload JSON.");

            var entry = BuildEntry(checkpointKind, verdict, snapshotHash, payloadHash, payloadJson);

            await FileLock.WaitAsync(ct);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_ledgerPath)!);
                var current = await LoadAsync(ct);
                var existing = current.Entries.FirstOrDefault(item => string.Equals(item.EntryId, entry.EntryId, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                    return existing;

                var line = JsonSerializer.Serialize(entry, JsonOptions);
                await File.AppendAllTextAsync(_ledgerPath, line + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
                await ArchiveEntryAsync(entry, ct);
                return entry;
            }
            finally
            {
                FileLock.Release();
            }
        }

        private async Task ArchiveEntryAsync(ComboLabEvidenceLedgerEntry entry, CancellationToken ct)
        {
            if (_archive is null)
                return;

            var logicalName = $"{entry.SavedAtUtc:yyyyMMddTHHmmssZ}-{entry.CheckpointKind}-{entry.EntryId}.json";
            var result = await _archive.UploadAsync(new ObjectArchivePayload(
                $"combo-lab/checkpoints/{entry.CheckpointKind}",
                logicalName,
                Encoding.UTF8.GetBytes(entry.PayloadJson),
                "application/json",
                RowCount: 1,
                ReceivedFromUtc: entry.SavedAtUtc,
                ReceivedToUtc: entry.SavedAtUtc), ct);

            if (result.Status == ObjectArchiveUploadStatus.Failed)
                throw new InvalidOperationException($"Evidence checkpoint archive failed: {result.Detail}");
        }

        private static ComboLabEvidenceLedgerEntry BuildEntry(
            string checkpointKind,
            string verdict,
            string snapshotHash,
            string payloadHash,
            string payloadJson)
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var schema = StringProperty(root, "Schema");
            var mode = StringProperty(root, "Mode");
            var liveOrderPath = StringProperty(root, "LiveOrderPath");

            return new ComboLabEvidenceLedgerEntry(
                DateTimeOffset.UtcNow,
                CryptoUtil.GetSha256($"{checkpointKind}|{snapshotHash}"),
                checkpointKind,
                schema,
                mode,
                verdict,
                snapshotHash,
                payloadHash,
                liveOrderPath,
                BuildSummary(checkpointKind, root),
                payloadJson);
        }

        private static string BuildSummary(string checkpointKind, JsonElement root)
        {
            if (string.Equals(checkpointKind, LiveCheckpointKind, StringComparison.Ordinal))
            {
                return $"{StringProperty(root, "FixtureLabel")} · {StringProperty(root, "EventSlug")} · books {IntProperty(root, "BooksFetched")} · candidates {ArrayLength(root, "Candidates")}/{ArrayLength(root, "GeneralizedCandidates")}";
            }

            return $"fixtures {IntProperty(root, "FixturesScanned")}/{IntProperty(root, "FixturesSeen")} · books {IntProperty(root, "TotalBooksFetched")} · candidates {ArrayLength(root, "Candidates")}";
        }

        private static string StringProperty(JsonElement root, string name)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
            }

            return "";
        }

        private static int IntProperty(JsonElement root, string name)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result))
                    return result;
                if (int.TryParse(value.ToString(), out result))
                    return result;
            }

            return 0;
        }

        private static int ArrayLength(JsonElement root, string name) =>
            root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Array
                ? value.GetArrayLength()
                : 0;
    }
}
