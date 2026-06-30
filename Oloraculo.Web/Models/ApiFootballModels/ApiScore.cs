namespace Oloraculo.Web.Models.ApiFootballModels;

public class ApiScore
{
    public ApiScorePair? Fulltime { get; set; }
    public ApiScorePair? Extratime { get; set; }
    public ApiScorePair? Penalty { get; set; }
}

public class ApiScorePair
{
    public int? Home { get; set; }
    public int? Away { get; set; }
}
