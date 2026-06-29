namespace Oloraculo.Web.Models.ApiFootballModels
{
    public sealed class ApiFixtureFetchResult
    {
        public bool IsConfigured { get; init; }
        public IReadOnlyList<ApiFixtureRow> Fixtures { get; init; } = [];
        public IReadOnlyList<string> Notes { get; init; } = [];
        public IReadOnlyList<string> Errors { get; init; } = [];
    }
}