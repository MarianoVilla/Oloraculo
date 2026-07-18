namespace Oloraculo.Web.Services.Simulation
{
    /// <summary>
    /// Estado de clasificacion de un equipo dentro de su grupo en la proyeccion determinista.
    /// </summary>
    public enum QualificationKind
    {
        Winner,        // 1ro del grupo
        RunnerUp,      // 2do del grupo
        ThirdQualified,// 3ro entre los 8 mejores -> clasifica
        ThirdOut,      // 3ro que no entra entre los 8 mejores
        Eliminated     // 4to del grupo
    }

    public sealed record DeterministicStanding(
        int Position,
        string TeamId,
        int Points,
        int GoalsFor,
        int GoalsAgainst,
        int GoalDiff,
        QualificationKind Qualification);

    public sealed record DeterministicGroup(
        string Name,
        IReadOnlyList<DeterministicStanding> Standings);

    public sealed record DeterministicTie(
        int Id,
        KnockoutStageEnum Stage,
        string HomeTeamId,
        string AwayTeamId,
        string WinnerTeamId,
        int HomeGoals,
        int AwayGoals,
        bool DecidedOnPenalties);

    /// <summary>
    /// Un unico cuadro "mas probable" del torneo: orden de cada grupo, quienes clasifican
    /// y todo el mata-mata resuelto hasta el campeon.
    /// </summary>
    public sealed record DeterministicProjection(
        IReadOnlyList<DeterministicGroup> Groups,
        IReadOnlyList<DeterministicTie> Knockout,
        string ChampionTeamId);
}
