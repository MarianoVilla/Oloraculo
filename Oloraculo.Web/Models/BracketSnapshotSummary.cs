namespace Oloraculo.Web.Models
{
    public sealed record BracketSnapshotSummary(
        int Id,
        DateTimeOffset CreatedAt,
        string ModelName,
        string InputSummaryHash,
        int TieCount,
        string? Error);
}
