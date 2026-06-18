using Oloraculo.Web.ComboLab.Markets;

namespace Oloraculo.Web.ComboLab.Scalp
{
    public static class UtcClock
    {
        public static DateTimeOffset Now() => DateTimeOffset.UtcNow;
    }

    public sealed record SportsScalpNearWindow(
        DateTimeOffset NowUtc,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc)
    {
        public static SportsScalpNearWindow Create(DateTimeOffset nowUtc, int nearWindowPastHours = 4, int nearWindowFutureHours = 4)
        {
            var utc = nowUtc.ToUniversalTime();
            return new SportsScalpNearWindow(
                utc,
                utc.AddHours(-nearWindowPastHours),
                utc.AddHours(nearWindowFutureHours));
        }

        public NearEventDecision Evaluate(PolymarketEventSnapshot ev, bool includeDebugOldEvents = false)
        {
            ArgumentNullException.ThrowIfNull(ev);
            var startUtc = ev.StartTimeUtc?.ToUniversalTime();

            if (!ev.Active)
                return Excluded(ev, startUtc, "inactive_or_not_tradable", includeDebugOldEvents);

            if (ev.Closed || ev.Ended || ev.Archived)
                return Excluded(ev, startUtc, "closed_or_resolved", includeDebugOldEvents);

            if (ev.Live)
                return Included(ev, startUtc, "live_or_playing");

            if (!startUtc.HasValue)
            {
                return new NearEventDecision(
                    IsNear: false,
                    IsDiagnosticVisible: includeDebugOldEvents,
                    IncludeReason: null,
                    ExcludeReason: "missing_start_time",
                    DiagnosticBucket: "missing_start_time_needs_review",
                    UtcStartTime: null,
                    WindowStartUtc,
                    WindowEndUtc,
                    IsLive: false,
                    PolyActive: ev.Active,
                    PolyClosed: ev.Closed,
                    PolyArchived: ev.Archived);
            }

            if (startUtc.Value >= WindowStartUtc && startUtc.Value <= WindowEndUtc)
                return Included(ev, startUtc, "start_within_near_window");

            return Excluded(ev, startUtc, "outside_near_window", includeDebugOldEvents);
        }

        private NearEventDecision Included(PolymarketEventSnapshot ev, DateTimeOffset? startUtc, string reason) => new(
            IsNear: true,
            IsDiagnosticVisible: true,
            IncludeReason: reason,
            ExcludeReason: null,
            DiagnosticBucket: null,
            UtcStartTime: startUtc,
            WindowStartUtc,
            WindowEndUtc,
            IsLive: ev.Live,
            PolyActive: ev.Active,
            PolyClosed: ev.Closed,
            PolyArchived: ev.Archived);

        private NearEventDecision Excluded(PolymarketEventSnapshot ev, DateTimeOffset? startUtc, string reason, bool includeDebugOldEvents) => new(
            IsNear: false,
            IsDiagnosticVisible: includeDebugOldEvents,
            IncludeReason: null,
            ExcludeReason: reason,
            DiagnosticBucket: null,
            UtcStartTime: startUtc,
            WindowStartUtc,
            WindowEndUtc,
            IsLive: ev.Live,
            PolyActive: ev.Active,
            PolyClosed: ev.Closed,
            PolyArchived: ev.Archived);
    }

    public sealed record NearEventDecision(
        bool IsNear,
        bool IsDiagnosticVisible,
        string? IncludeReason,
        string? ExcludeReason,
        string? DiagnosticBucket,
        DateTimeOffset? UtcStartTime,
        DateTimeOffset WindowStartUtc,
        DateTimeOffset WindowEndUtc,
        bool IsLive,
        bool PolyActive,
        bool PolyClosed,
        bool PolyArchived);
}
