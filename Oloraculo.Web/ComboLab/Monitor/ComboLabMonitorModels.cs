using Oloraculo.Web.ComboLab.Markets;

namespace Oloraculo.Web.ComboLab.Monitor
{
    public enum ComboLabMonitorVerdict
    {
        ReadyForPricing,
        SourceBlocked,
        NeedsModel,
        UnknownFamily,
        NotComboEligible
    }

    public sealed record ComboLabMonitorMarketRow(
        string? MarketId,
        string? Slug,
        string? Question,
        string? ConditionId,
        string? SportsMarketType,
        PolymarketFootballMarketFamily Family,
        PolymarketFootballModelCoverage Coverage,
        bool ComboEligible,
        ComboLabMonitorVerdict Verdict,
        IReadOnlyList<PolymarketRejectReason> RejectReasons)
    {
        public string VerdictLabel => Verdict switch
        {
            ComboLabMonitorVerdict.ReadyForPricing => "READY_FOR_PRICING",
            ComboLabMonitorVerdict.SourceBlocked => "SOURCE_BLOCKED",
            ComboLabMonitorVerdict.NeedsModel => "NEEDS_MODEL",
            ComboLabMonitorVerdict.UnknownFamily => "UNKNOWN_FAMILY",
            ComboLabMonitorVerdict.NotComboEligible => "NOT_COMBO_ELIGIBLE",
            _ => Verdict.ToString().ToUpperInvariant()
        };
    }

    public sealed record ComboLabUniverseMonitorSnapshot(
        DateTimeOffset AsOfUtc,
        IReadOnlyList<string> RequestedSportsMarketTypes,
        PolymarketFootballUniverseReport Report,
        IReadOnlyList<ComboLabMonitorMarketRow> Rows,
        IReadOnlyList<string> Errors)
    {
        public bool HasErrors => Errors.Count > 0;
        public int ReadyForPricing => Rows.Count(row => row.Verdict == ComboLabMonitorVerdict.ReadyForPricing);
        public int NeedsModel => Rows.Count(row => row.Verdict == ComboLabMonitorVerdict.NeedsModel);
        public int SourceBlocked => Rows.Count(row => row.Verdict == ComboLabMonitorVerdict.SourceBlocked);
        public int UnknownFamily => Rows.Count(row => row.Verdict == ComboLabMonitorVerdict.UnknownFamily);
    }
}
