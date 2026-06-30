namespace Oloraculo.Web.Models
{
    public sealed record BracketSnapshotLoadResult(BracketProjection? Projection, string? Error)
    {
        public bool IsValid => Projection is not null && string.IsNullOrWhiteSpace(Error);
    }
}
