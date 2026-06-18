use crate::book::{PriceMicros, QuantityMicros};
use crate::scalp::{ScalpCandidate, ScalpVerdict};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SnapshotCandidate {
    pub verdict: ScalpVerdict,
    pub event_id: String,
    pub event_title: String,
    pub market_id: String,
    pub market_family: String,
    pub entry_outcome: String,
    pub hedge_outcome: String,
    pub target_shares: QuantityMicros,
    pub entry_vwap: Option<PriceMicros>,
    pub hedge_vwap_now: Option<PriceMicros>,
    pub pair_cost_now: Option<PriceMicros>,
    pub locked_profit_per_share_now: Option<PriceMicros>,
    pub roi_bps_now: Option<i64>,
    pub blockers: Vec<String>,
}

impl From<&ScalpCandidate> for SnapshotCandidate {
    fn from(candidate: &ScalpCandidate) -> Self {
        Self {
            verdict: candidate.verdict,
            event_id: candidate.event_id.clone(),
            event_title: candidate.event_title.clone(),
            market_id: candidate.market_id.clone(),
            market_family: candidate.market_family.clone(),
            entry_outcome: candidate.entry_outcome.clone(),
            hedge_outcome: candidate.hedge_outcome.clone(),
            target_shares: candidate.target_shares,
            entry_vwap: candidate.entry_vwap,
            hedge_vwap_now: candidate.hedge_vwap_now,
            pair_cost_now: candidate.pair_cost_now,
            locked_profit_per_share_now: candidate.locked_profit_per_share_now,
            roi_bps_now: candidate.roi_bps_now,
            blockers: candidate.blockers.iter().map(|blocker| blocker.as_str().to_string()).collect(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SportsScalpSnapshot {
    pub as_of_ts_ms: i64,
    pub mode: &'static str,
    pub candidates: Vec<SnapshotCandidate>,
}

impl SportsScalpSnapshot {
    pub fn from_candidates(as_of_ts_ms: i64, candidates: &[ScalpCandidate]) -> Self {
        Self {
            as_of_ts_ms,
            mode: "WATCH_ONLY_NO_ORDER_PATH",
            candidates: candidates.iter().map(SnapshotCandidate::from).collect(),
        }
    }

    /// Dependency-free JSON contract for the read-only cockpit/API surface.
    /// This performs no I/O and cannot place/cancel/approve orders.
    pub fn to_json(&self) -> String {
        let mut out = String::new();
        out.push('{');
        push_json_key_i64(&mut out, "as_of_ts_ms", self.as_of_ts_ms, false);
        push_json_key_str(&mut out, "mode", self.mode, true);
        out.push_str(",\"candidates\":[");
        for (idx, candidate) in self.candidates.iter().enumerate() {
            if idx > 0 {
                out.push(',');
            }
            candidate.push_json(&mut out);
        }
        out.push_str("]}");
        out
    }
}

impl SnapshotCandidate {
    fn push_json(&self, out: &mut String) {
        out.push('{');
        push_json_key_str(out, "verdict", self.verdict.as_str(), false);
        push_json_key_str(out, "event_id", &self.event_id, true);
        push_json_key_str(out, "event_title", &self.event_title, true);
        push_json_key_str(out, "market_id", &self.market_id, true);
        push_json_key_str(out, "market_family", &self.market_family, true);
        push_json_key_str(out, "entry_outcome", &self.entry_outcome, true);
        push_json_key_str(out, "hedge_outcome", &self.hedge_outcome, true);
        push_json_key_u64(out, "target_shares_micros", self.target_shares.micros(), true);
        push_json_key_price(out, "entry_vwap", self.entry_vwap, true);
        push_json_key_price(out, "hedge_vwap_now", self.hedge_vwap_now, true);
        push_json_key_price(out, "pair_cost_now", self.pair_cost_now, true);
        push_json_key_price(out, "locked_profit_per_share_now", self.locked_profit_per_share_now, true);
        push_json_key_opt_i64(out, "roi_bps_now", self.roi_bps_now, true);
        out.push_str(",\"blockers\":[");
        for (idx, blocker) in self.blockers.iter().enumerate() {
            if idx > 0 {
                out.push(',');
            }
            push_json_string(out, blocker);
        }
        out.push_str("]}");
    }
}

fn push_json_key_i64(out: &mut String, key: &str, value: i64, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    out.push_str(&value.to_string());
}

fn push_json_key_u64(out: &mut String, key: &str, value: u64, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    out.push_str(&value.to_string());
}

fn push_json_key_opt_i64(out: &mut String, key: &str, value: Option<i64>, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    match value {
        Some(value) => out.push_str(&value.to_string()),
        None => out.push_str("null"),
    }
}

fn push_json_key_str(out: &mut String, key: &str, value: &str, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    push_json_string(out, value);
}

fn push_json_key_price(out: &mut String, key: &str, value: Option<PriceMicros>, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    match value {
        Some(value) => out.push_str(&value.micros().to_string()),
        None => out.push_str("null"),
    }
}

fn push_json_string(out: &mut String, value: &str) {
    out.push('"');
    for ch in value.chars() {
        match ch {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\n' => out.push_str("\\n"),
            '\r' => out.push_str("\\r"),
            '\t' => out.push_str("\\t"),
            ch if ch.is_control() => out.push_str(&format!("\\u{:04x}", ch as u32)),
            ch => out.push(ch),
        }
    }
    out.push('"');
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::book::{BookLevel, OrderBook};
    use crate::scalp::{ScalpInput, ScalpPlanner};

    fn p(micros: i64) -> PriceMicros {
        PriceMicros::new(micros).unwrap()
    }

    #[test]
    fn snapshot_json_is_watch_only_contract() {
        let entry = OrderBook::new(
            "under",
            Some("cond".to_string()),
            10,
            vec![],
            vec![BookLevel::new(p(480_000), QuantityMicros::whole(400))],
        );
        let hedge = OrderBook::new(
            "over",
            Some("cond".to_string()),
            10,
            vec![],
            vec![BookLevel::new(p(440_000), QuantityMicros::whole(400))],
        );
        let candidate = ScalpPlanner::plan(ScalpInput {
            event_id: "event\"1",
            event_title: "A vs B",
            market_id: "market-1",
            market_family: "MatchTotal",
            entry_outcome: "Under",
            hedge_outcome: "Over",
            entry_book: Some(&entry),
            hedge_book: Some(&hedge),
            target_shares: QuantityMicros::whole(400),
        });

        let snapshot = SportsScalpSnapshot::from_candidates(123, &[candidate]);
        let json = snapshot.to_json();

        assert!(json.contains("\"mode\":\"WATCH_ONLY_NO_ORDER_PATH\""));
        assert!(json.contains("TRADE_NOW_ANALYSIS_ONLY"));
        assert!(json.contains("event\\\"1"));
        assert!(json.contains("\"pair_cost_now\":920000"));
        assert!(!json.contains("approval"));
        assert!(!json.contains("order"));
    }
}
