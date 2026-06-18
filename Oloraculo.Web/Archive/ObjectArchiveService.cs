using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Oloraculo.Web.Helpers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oloraculo.Web.Archive
{
    public enum ObjectArchiveUploadStatus
    {
        Uploaded,
        SkippedDisabled,
        ConfigMissing,
        Failed
    }

    public sealed record ObjectArchiveReadiness(
        bool Enabled,
        bool Configured,
        string Provider,
        string Detail);

    public sealed record ResolvedObjectArchiveConfig(
        bool Enabled,
        string Provider,
        string? Bucket,
        string? Endpoint,
        string Region,
        string Prefix,
        bool ForcePathStyle,
        string? AccessKeyId,
        string? SecretAccessKey)
    {
        public bool HasCredentials =>
            !string.IsNullOrWhiteSpace(AccessKeyId) &&
            !string.IsNullOrWhiteSpace(SecretAccessKey);

        public bool EndpointRequired =>
            !string.Equals(Provider, "S3", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(Endpoint);

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Bucket) &&
            HasCredentials &&
            !string.IsNullOrWhiteSpace(Region) &&
            (!EndpointRequired || !string.IsNullOrWhiteSpace(Endpoint));

        public ObjectArchiveReadiness ToReadiness()
        {
            if (!Enabled)
                return new ObjectArchiveReadiness(false, IsConfigured, Provider, "archive disabled by config");

            if (IsConfigured)
            {
                var endpointDetail = string.IsNullOrWhiteSpace(Endpoint)
                    ? $"aws region {Region}"
                    : $"endpoint host {SafeHost(Endpoint)}";
                return new ObjectArchiveReadiness(true, true, Provider, $"bucket configured; {endpointDetail}");
            }

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(Bucket))
                missing.Add("bucket");
            if (EndpointRequired && string.IsNullOrWhiteSpace(Endpoint))
                missing.Add("endpoint");
            if (string.IsNullOrWhiteSpace(Region))
                missing.Add("region");
            if (string.IsNullOrWhiteSpace(AccessKeyId))
                missing.Add("access key id");
            if (string.IsNullOrWhiteSpace(SecretAccessKey))
                missing.Add("secret access key");

            return new ObjectArchiveReadiness(true, false, Provider, "missing " + string.Join(", ", missing));
        }

        private static string SafeHost(string endpoint)
        {
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                return uri.Host;
            return "configured endpoint";
        }
    }

    public static class ObjectArchiveConfigResolver
    {
        public static ResolvedObjectArchiveConfig Resolve(OloraculoConfig config, Func<string, string?>? environment = null)
        {
            ArgumentNullException.ThrowIfNull(config);
            environment ??= Environment.GetEnvironmentVariable;
            var archive = config.ObjectArchive ?? new ObjectArchiveConfig();

            return new ResolvedObjectArchiveConfig(
                archive.Enabled,
                CleanValue(archive.Provider) ?? "R2",
                FirstNonEmpty(environment(archive.BucketEnvironmentVariable), archive.Bucket),
                FirstNonEmpty(environment(archive.EndpointEnvironmentVariable), archive.Endpoint),
                FirstNonEmpty(environment(archive.RegionEnvironmentVariable), archive.Region) ?? "auto",
                CleanPrefix(archive.Prefix),
                archive.ForcePathStyle,
                CleanValue(environment(archive.AccessKeyIdEnvironmentVariable)),
                CleanValue(environment(archive.SecretAccessKeyEnvironmentVariable)));
        }

        private static string? FirstNonEmpty(params string?[] values) =>
            values.Select(CleanValue).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        private static string? CleanValue(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string CleanPrefix(string? value)
        {
            var clean = CleanValue(value) ?? "oloraculo";
            return clean.Trim('/').Replace('\\', '/');
        }
    }

    public sealed record ObjectArchivePayload(
        string StreamName,
        string LogicalName,
        byte[] Content,
        string ContentType,
        long? RowCount = null,
        DateTimeOffset? ReceivedFromUtc = null,
        DateTimeOffset? ReceivedToUtc = null);

    public sealed record ObjectArchiveManifest(
        string Schema,
        DateTimeOffset UploadedAtUtc,
        string Provider,
        string BucketRedaction,
        string ObjectKey,
        string ManifestKey,
        string StreamName,
        string LogicalName,
        string ContentType,
        long Bytes,
        long? RowCount,
        DateTimeOffset? ReceivedFromUtc,
        DateTimeOffset? ReceivedToUtc,
        string Sha256);

    public sealed record ObjectArchiveUploadResult(
        ObjectArchiveUploadStatus Status,
        string Detail,
        string? ObjectKey = null,
        string? ManifestKey = null,
        long Bytes = 0,
        string? Sha256 = null);

    public interface IObjectArchiveService
    {
        ObjectArchiveReadiness Readiness { get; }
        Task<ObjectArchiveUploadResult> UploadAsync(ObjectArchivePayload payload, CancellationToken ct = default);
    }

    public sealed class DisabledObjectArchiveService : IObjectArchiveService
    {
        private readonly ResolvedObjectArchiveConfig _config;

        public DisabledObjectArchiveService(IOptions<OloraculoConfig> options)
        {
            _config = ObjectArchiveConfigResolver.Resolve(options.Value);
        }

        public ObjectArchiveReadiness Readiness => _config.ToReadiness();

        public Task<ObjectArchiveUploadResult> UploadAsync(ObjectArchivePayload payload, CancellationToken ct = default) =>
            Task.FromResult(new ObjectArchiveUploadResult(
                _config.Enabled ? ObjectArchiveUploadStatus.ConfigMissing : ObjectArchiveUploadStatus.SkippedDisabled,
                _config.ToReadiness().Detail));
    }

    public sealed class S3ObjectArchiveService : IObjectArchiveService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly IOptions<OloraculoConfig> _options;
        private readonly Func<DateTimeOffset> _clock;

        public S3ObjectArchiveService(IOptions<OloraculoConfig> options)
            : this(options, () => DateTimeOffset.UtcNow)
        {
        }

        public S3ObjectArchiveService(IOptions<OloraculoConfig> options, Func<DateTimeOffset> clock)
        {
            _options = options;
            _clock = clock;
        }

        public ObjectArchiveReadiness Readiness => ObjectArchiveConfigResolver.Resolve(_options.Value).ToReadiness();

        public async Task<ObjectArchiveUploadResult> UploadAsync(ObjectArchivePayload payload, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(payload);
            var config = ObjectArchiveConfigResolver.Resolve(_options.Value);
            var readiness = config.ToReadiness();
            if (!config.Enabled)
                return new ObjectArchiveUploadResult(ObjectArchiveUploadStatus.SkippedDisabled, readiness.Detail);
            if (!config.IsConfigured)
                return new ObjectArchiveUploadResult(ObjectArchiveUploadStatus.ConfigMissing, readiness.Detail);
            if (payload.Content.Length == 0)
                return new ObjectArchiveUploadResult(ObjectArchiveUploadStatus.Failed, "empty payload was not archived");

            var uploadedAt = _clock();
            var sha256 = CryptoUtil.GetSha256(payload.Content);
            var objectKey = BuildObjectKey(config, payload, uploadedAt, sha256);
            var manifestKey = objectKey + ".manifest.json";
            var manifest = new ObjectArchiveManifest(
                "oloraculo/object-archive-manifest/v1",
                uploadedAt,
                config.Provider,
                "configured",
                objectKey,
                manifestKey,
                payload.StreamName,
                payload.LogicalName,
                payload.ContentType,
                payload.Content.LongLength,
                payload.RowCount,
                payload.ReceivedFromUtc,
                payload.ReceivedToUtc,
                sha256);

            using var client = CreateClient(config);
            await PutBytesAsync(client, config, objectKey, payload.Content, payload.ContentType, sha256, ct);

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            await PutBytesAsync(client, config, manifestKey, Encoding.UTF8.GetBytes(manifestJson), "application/json", CryptoUtil.GetSha256(manifestJson), ct);

            var head = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = config.Bucket,
                Key = objectKey
            }, ct);

            if (head.Headers.ContentLength != payload.Content.LongLength)
            {
                return new ObjectArchiveUploadResult(
                    ObjectArchiveUploadStatus.Failed,
                    $"uploaded object size mismatch: expected {payload.Content.LongLength}, got {head.Headers.ContentLength}",
                    objectKey,
                    manifestKey,
                    payload.Content.LongLength,
                    sha256);
            }

            var remoteHash = MetadataValue(head.Metadata, "sha256");
            if (!string.IsNullOrWhiteSpace(remoteHash) && !string.Equals(remoteHash, sha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ObjectArchiveUploadResult(
                    ObjectArchiveUploadStatus.Failed,
                    "uploaded object hash metadata mismatch",
                    objectKey,
                    manifestKey,
                    payload.Content.LongLength,
                    sha256);
            }

            return new ObjectArchiveUploadResult(
                ObjectArchiveUploadStatus.Uploaded,
                "archive upload verified",
                objectKey,
                manifestKey,
                payload.Content.LongLength,
                sha256);
        }

        private static async Task PutBytesAsync(
            IAmazonS3 client,
            ResolvedObjectArchiveConfig config,
            string key,
            byte[] bytes,
            string contentType,
            string sha256,
            CancellationToken ct)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var request = new PutObjectRequest
            {
                BucketName = config.Bucket,
                Key = key,
                InputStream = stream,
                ContentType = contentType
            };
            request.Metadata["sha256"] = sha256;
            request.Metadata["archive-schema"] = "oloraculo/object-archive/v1";
            await client.PutObjectAsync(request, ct);
        }

        private static IAmazonS3 CreateClient(ResolvedObjectArchiveConfig config)
        {
            var s3Config = new AmazonS3Config
            {
                ForcePathStyle = config.ForcePathStyle
            };

            if (!string.IsNullOrWhiteSpace(config.Endpoint))
            {
                s3Config.ServiceURL = config.Endpoint;
                s3Config.AuthenticationRegion = string.IsNullOrWhiteSpace(config.Region) ? "auto" : config.Region;
            }
            else
            {
                s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(config.Region);
            }

            return new AmazonS3Client(
                new BasicAWSCredentials(config.AccessKeyId, config.SecretAccessKey),
                s3Config);
        }

        private static string BuildObjectKey(ResolvedObjectArchiveConfig config, ObjectArchivePayload payload, DateTimeOffset uploadedAt, string sha256)
        {
            var stream = CleanPath(payload.StreamName);
            var logicalName = CleanSegment(payload.LogicalName);
            if (string.IsNullOrWhiteSpace(logicalName))
                logicalName = sha256[..16] + ".bin";

            return string.Join(
                '/',
                new[]
                {
                    config.Prefix,
                    stream,
                    $"date={uploadedAt:yyyy-MM-dd}",
                    $"hour={uploadedAt:HH}",
                    logicalName
                }.Where(segment => !string.IsNullOrWhiteSpace(segment)));
        }

        private static string CleanPath(string value) =>
            string.Join('/', value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Select(CleanSegment));

        private static string CleanSegment(string value)
        {
            var clean = Regex.Replace(value.Trim(), @"[^A-Za-z0-9._=-]+", "-");
            return clean.Trim('-');
        }

        private static string? MetadataValue(MetadataCollection metadata, string name)
        {
            var direct = metadata[name];
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;
            return metadata["x-amz-meta-" + name];
        }
    }
}
