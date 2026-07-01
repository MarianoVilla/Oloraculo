using Oloraculo.Web.Models;

namespace Oloraculo.Web.Helpers;

public static class KnockoutDisplayHelper
{
    public static string? PredictedWinnerName(KnockoutMatchView match)
    {
        if (match.PredictedWinnerTeamId == match.HomeTeamId)
            return match.HomeTeamName;
        if (match.PredictedWinnerTeamId == match.AwayTeamId)
            return match.AwayTeamName;
        return null;
    }
}
