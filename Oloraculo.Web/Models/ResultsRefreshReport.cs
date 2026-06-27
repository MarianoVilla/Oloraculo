namespace Oloraculo.Web.Models
{
    public class ResultsRefreshReport
    {
        public int ResultsImported { get; init; }
        public int FixturesReconciled { get; init; }
        public IReadOnlyList<string> Notes { get; init; } = [];
        public IReadOnlyList<string> Errors { get; init; } = [];
    }
}
