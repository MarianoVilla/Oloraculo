using Microsoft.Extensions.Options;

namespace Oloraculo.Web.Archive
{
    public sealed record ObjectArchiveHealthSnapshot(
        bool Configured,
        bool Enabled,
        DateTimeOffset? LastVerifiedManifestUtc,
        int PendingLocalBatchCount,
        string? LastError,
        string Provider);

    public interface IObjectArchiveHealthProbe
    {
        ObjectArchiveHealthSnapshot Probe(DateTimeOffset asOfUtc);
    }

    public sealed class DefaultObjectArchiveHealthProbe : IObjectArchiveHealthProbe
    {
        private readonly IOptions<OloraculoConfig> _options;
        private readonly Func<string, string?> _environment;

        public DefaultObjectArchiveHealthProbe(IOptions<OloraculoConfig> options)
            : this(options, Environment.GetEnvironmentVariable)
        {
        }

        public DefaultObjectArchiveHealthProbe(IOptions<OloraculoConfig> options, Func<string, string?> environment)
        {
            _options = options;
            _environment = environment;
        }

        public ObjectArchiveHealthSnapshot Probe(DateTimeOffset asOfUtc)
        {
            var config = ObjectArchiveConfigResolver.Resolve(_options.Value, _environment);
            return new ObjectArchiveHealthSnapshot(
                config.IsConfigured,
                config.Enabled,
                LastVerifiedManifestUtc: null,
                PendingLocalBatchCount: 0,
                LastError: null,
                config.Provider);
        }
    }
}
