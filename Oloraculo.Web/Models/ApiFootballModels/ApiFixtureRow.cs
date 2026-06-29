namespace Oloraculo.Web.Models.ApiFootballModels
{
    public class ApiFixtureRow
    {
        public ApiFixture Fixture { get; set; } = new();
        public ApiFixtureLeague League { get; set; } = new();
        public ApiTeams Teams { get; set; } = new();
        public ApiGoals Goals { get; set; } = new();
        public ApiGoals Penalty { get; set; } = new();
    }
}
