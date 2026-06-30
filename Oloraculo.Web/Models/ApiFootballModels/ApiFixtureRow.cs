namespace Oloraculo.Web.Models.ApiFootballModels
{
    public class ApiFixtureRow
    {
        public ApiFixture Fixture { get; set; } = new();
        public ApiFixtureLeague League { get; set; } = new();
        public ApiTeams Teams { get; set; } = new();
        public ApiGoals Goals { get; set; } = new();
        public ApiScore Score { get; set; } = new();
    }

    public class ApiFixtureLeague
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string? Round { get; set; }
    }
}
