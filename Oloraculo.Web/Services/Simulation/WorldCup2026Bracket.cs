using static Oloraculo.Web.Services.Simulation.SimulationService;

namespace Oloraculo.Web.Services.Simulation
{

    public class WorldCup2026Bracket
    {
        public static readonly IReadOnlyList<BracketTie> RoundOf32 =
[
        new(73, KnockoutStageEnum.RoundOf32, RunnerUp("A"), RunnerUp("B")),
        new(74, KnockoutStageEnum.RoundOf32, Winner("E"), Third("A", "B", "C", "D", "F")),
        new(75, KnockoutStageEnum.RoundOf32, Winner("F"), RunnerUp("C")),
        new(76, KnockoutStageEnum.RoundOf32, Winner("C"), RunnerUp("F")),
        new(77, KnockoutStageEnum.RoundOf32, Winner("I"), Third("C", "D", "F", "G", "H")),
        new(78, KnockoutStageEnum.RoundOf32, RunnerUp("E"), RunnerUp("I")),
        new(79, KnockoutStageEnum.RoundOf32, Winner("A"), Third("C", "E", "F", "H", "I")),
        new(80, KnockoutStageEnum.RoundOf32, Winner("L"), Third("E", "H", "I", "J", "K")),
        new(81, KnockoutStageEnum.RoundOf32, Winner("D"), Third("B", "E", "F", "I", "J")),
        new(82, KnockoutStageEnum.RoundOf32, Winner("G"), Third("A", "E", "H", "I", "J")),
        new(83, KnockoutStageEnum.RoundOf32, RunnerUp("K"), RunnerUp("L")),
        new(84, KnockoutStageEnum.RoundOf32, Winner("H"), RunnerUp("J")),
        new(85, KnockoutStageEnum.RoundOf32, Winner("B"), Third("E", "F", "G", "I", "J")),
        new(86, KnockoutStageEnum.RoundOf32, Winner("J"), RunnerUp("H")),
        new(87, KnockoutStageEnum.RoundOf32, Winner("K"), Third("D", "E", "I", "J", "L")),
        new(88, KnockoutStageEnum.RoundOf32, RunnerUp("D"), RunnerUp("G"))
];

        public static readonly IReadOnlyList<BracketTie> RoundOf16 =
        [
            new(89, KnockoutStageEnum.RoundOf16, WinnerOf(74), WinnerOf(77)),
        new(90, KnockoutStageEnum.RoundOf16, WinnerOf(73), WinnerOf(75)),
        new(91, KnockoutStageEnum.RoundOf16, WinnerOf(76), WinnerOf(78)),
        new(92, KnockoutStageEnum.RoundOf16, WinnerOf(79), WinnerOf(80)),
        new(93, KnockoutStageEnum.RoundOf16, WinnerOf(83), WinnerOf(84)),
        new(94, KnockoutStageEnum.RoundOf16, WinnerOf(81), WinnerOf(82)),
        new(95, KnockoutStageEnum.RoundOf16, WinnerOf(86), WinnerOf(88)),
        new(96, KnockoutStageEnum.RoundOf16, WinnerOf(85), WinnerOf(87))
        ];

        public static readonly IReadOnlyList<BracketTie> QuarterFinals =
        [
            new(97, KnockoutStageEnum.QuarterFinal, WinnerOf(89), WinnerOf(90)),
        new(98, KnockoutStageEnum.QuarterFinal, WinnerOf(93), WinnerOf(94)),
        new(99, KnockoutStageEnum.QuarterFinal, WinnerOf(91), WinnerOf(92)),
        new(100, KnockoutStageEnum.QuarterFinal, WinnerOf(95), WinnerOf(96))
        ];

        public static readonly IReadOnlyList<BracketTie> SemiFinals =
        [
            new(101, KnockoutStageEnum.SemiFinal, WinnerOf(97), WinnerOf(98)),
        new(102, KnockoutStageEnum.SemiFinal, WinnerOf(99), WinnerOf(100))
        ];

        public static readonly BracketTie ThirdPlace = new(103, KnockoutStageEnum.ThirdPlace, LoserOf(101), LoserOf(102));
        public static readonly BracketTie Final = new(104, KnockoutStageEnum.Final, WinnerOf(101), WinnerOf(102));

        public static IReadOnlyList<BracketTie> KnockoutTies =>
        [
            ..RoundOf32,
        ..RoundOf16,
        ..QuarterFinals,
        ..SemiFinals,
        ThirdPlace,
        Final
        ];

        public static IReadOnlyList<BracketTie> ForStage(KnockoutStageEnum stage) => stage switch
        {
            KnockoutStageEnum.RoundOf32 => RoundOf32,
            KnockoutStageEnum.RoundOf16 => RoundOf16,
            KnockoutStageEnum.QuarterFinal => QuarterFinals,
            KnockoutStageEnum.SemiFinal => SemiFinals,
            KnockoutStageEnum.ThirdPlace => [ThirdPlace],
            KnockoutStageEnum.Final => [Final],
            _ => []
        };

        public static IReadOnlyDictionary<int, string> AssignThirdPlaceGroups(IReadOnlyCollection<string> qualifiedThirdGroups)
        {
            if (qualifiedThirdGroups.Count != 8)
                throw new InvalidOperationException($"El cuadro 2026 requiere exactamente ocho grupos con terceros clasificados, pero recibió {qualifiedThirdGroups.Count}.");

            var qualified = qualifiedThirdGroups.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var slots = RoundOf32
                .Where(t => t.Home.Kind == BracketSlotKindEnum.GroupThird || t.Away.Kind == BracketSlotKindEnum.GroupThird)
                .Select(t => new ThirdSlot(t.Id, ThirdOptions(t)))
                .OrderBy(s => s.Options.Count(g => qualified.Contains(g)))
                .ThenBy(s => s.TieId)
                .ToList();

            var assigned = new Dictionary<int, string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!TryAssign(0))
                throw new InvalidOperationException($"No se pudieron asignar los grupos de terceros {string.Join(",", qualifiedThirdGroups.Order())} a los cruces oficiales de 2026.");

            return assigned;

            bool TryAssign(int index)
            {
                if (index == slots.Count)
                    return true;

                var slot = slots[index];
                foreach (var group in slot.Options.Where(qualified.Contains).OrderBy(GroupOrder))
                {
                    if (!used.Add(group))
                        continue;

                    assigned[slot.TieId] = group;
                    if (TryAssign(index + 1))
                        return true;

                    assigned.Remove(slot.TieId);
                    used.Remove(group);
                }

                return false;
            }
        }

        public static readonly IReadOnlyDictionary<int, OfficialKnockoutSchedule> OfficialSchedule =
            new Dictionary<int, OfficialKnockoutSchedule>
            {
                [73] = new(2026, 6, 28, "Los Angeles|Inglewood|SoFi"),
                [74] = new(2026, 6, 29, "Boston|Foxborough|Gillette"),
                [75] = new(2026, 6, 29, "Monterrey"),
                [76] = new(2026, 6, 29, "Houston"),
                [77] = new(2026, 6, 30, "New York|East Rutherford|MetLife"),
                [78] = new(2026, 6, 30, "Dallas|Arlington|AT&T"),
                [79] = new(2026, 6, 30, "Mexico City"),
                [80] = new(2026, 7, 1, "Atlanta"),
                [81] = new(2026, 7, 1, "San Francisco|Santa Clara|Levi"),
                [82] = new(2026, 7, 1, "Seattle"),
                [83] = new(2026, 7, 2, "Toronto"),
                [84] = new(2026, 7, 2, "Los Angeles|Inglewood|SoFi"),
                [85] = new(2026, 7, 2, "Vancouver"),
                [86] = new(2026, 7, 3, "Miami|Miami Gardens|Hard Rock"),
                [87] = new(2026, 7, 3, "Kansas City|Arrowhead"),
                [88] = new(2026, 7, 3, "Dallas|Arlington|AT&T"),
                [89] = new(2026, 7, 4, "Philadelphia"),
                [90] = new(2026, 7, 4, "Houston"),
                [91] = new(2026, 7, 5, "New York|East Rutherford|MetLife"),
                [92] = new(2026, 7, 5, "Mexico City"),
                [93] = new(2026, 7, 6, "Dallas|Arlington|AT&T"),
                [94] = new(2026, 7, 6, "Seattle"),
                [95] = new(2026, 7, 7, "Atlanta"),
                [96] = new(2026, 7, 7, "Vancouver"),
                [97] = new(2026, 7, 9, "Boston|Foxborough|Gillette"),
                [98] = new(2026, 7, 10, "Los Angeles|Inglewood|SoFi"),
                [99] = new(2026, 7, 11, "Miami|Miami Gardens|Hard Rock"),
                [100] = new(2026, 7, 11, "Kansas City|Arrowhead"),
                [101] = new(2026, 7, 14, "Dallas|Arlington|AT&T"),
                [102] = new(2026, 7, 15, "Atlanta"),
                [103] = new(2026, 7, 18, "Miami|Miami Gardens|Hard Rock"),
                [104] = new(2026, 7, 19, "New York|East Rutherford|MetLife")
            };

        private static BracketSlot Winner(string group) => new(BracketSlotKindEnum.GroupWinner, Group: group);
        private static BracketSlot RunnerUp(string group) => new(BracketSlotKindEnum.GroupRunnerUp, Group: group);
        private static BracketSlot Third(params string[] groups) => new(BracketSlotKindEnum.GroupThird, ThirdPlaceGroupOptions: groups);
        private static BracketSlot WinnerOf(int tieId) => new(BracketSlotKindEnum.WinnerOfTie, TieId: tieId);
        private static BracketSlot LoserOf(int tieId) => new(BracketSlotKindEnum.LoserOfTie, TieId: tieId);

        private static IReadOnlyList<string> ThirdOptions(BracketTie tie) =>
            tie.Home.Kind == BracketSlotKindEnum.GroupThird ? tie.Home.ThirdPlaceGroupOptions ?? [] : tie.Away.ThirdPlaceGroupOptions ?? [];

        private static int GroupOrder(string group) => group.Length == 0 ? int.MaxValue : group[0] - 'A';

        private sealed record ThirdSlot(int TieId, IReadOnlyList<string> Options);
        public sealed record OfficialKnockoutSchedule(int Year, int Month, int Day, string Location)
        {
            public DateOnly Date => new(Year, Month, Day);
        }
    }
}
