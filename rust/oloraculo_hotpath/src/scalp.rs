use crate::book::{OrderBook, PriceMicros, QuantityMicros, MICROS_PER_UNIT};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ScalpVerdict {
    TradeNow,
    Watch,
    Blocked,
    Reject,
}

impl ScalpVerdict {
    pub const fn as_str(self) -> &'static str {
        match self {
            Self::TradeNow => "TRADE_NOW_ANALYSIS_ONLY",
            Self::Watch => "WATCH",
            Self::Blocked => "BLOCKED",
            Self::Reject => "REJECT",
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ScalpBlocker {
    MissingEntryBook,
    MissingHedgeBook,
    NoEntryAsk,
    NoHedgeAsk,
    InsufficientEntryDepth,
    InsufficientHedgeDepth,
}

impl ScalpBlocker {
    pub const fn as_str(&self) -> &'static str {
        match self {
            Self::MissingEntryBook => "MISSING_ENTRY_BOOK",
            Self::MissingHedgeBook => "MISSING_HEDGE_BOOK",
            Self::NoEntryAsk => "NO_ENTRY_ASK",
            Self::NoHedgeAsk => "NO_HEDGE_ASK",
            Self::InsufficientEntryDepth => "INSUFFICIENT_ENTRY_DEPTH",
            Self::InsufficientHedgeDepth => "INSUFFICIENT_HEDGE_DEPTH",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct HedgeTargets {
    pub breakeven: Option<PriceMicros>,
    pub roi_2: Option<PriceMicros>,
    pub roi_5: Option<PriceMicros>,
    pub roi_8: Option<PriceMicros>,
    pub roi_10: Option<PriceMicros>,
    pub roi_12: Option<PriceMicros>,
}

impl HedgeTargets {
    pub const EMPTY: Self = Self {
        breakeven: None,
        roi_2: None,
        roi_5: None,
        roi_8: None,
        roi_10: None,
        roi_12: None,
    };
}

#[derive(Debug, Clone)]
pub struct ScalpInput<'a> {
    pub event_id: &'a str,
    pub event_title: &'a str,
    pub market_id: &'a str,
    pub market_family: &'a str,
    pub entry_outcome: &'a str,
    pub hedge_outcome: &'a str,
    pub entry_book: Option<&'a OrderBook>,
    pub hedge_book: Option<&'a OrderBook>,
    pub target_shares: QuantityMicros,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ScalpCandidate {
    pub verdict: ScalpVerdict,
    pub event_id: String,
    pub event_title: String,
    pub market_id: String,
    pub market_family: String,
    pub entry_outcome: String,
    pub hedge_outcome: String,
    pub entry_token_id: Option<String>,
    pub hedge_token_id: Option<String>,
    pub target_shares: QuantityMicros,
    pub entry_best_ask: Option<PriceMicros>,
    pub hedge_best_ask: Option<PriceMicros>,
    pub entry_vwap: Option<PriceMicros>,
    pub hedge_vwap_now: Option<PriceMicros>,
    pub entry_worst_ask: Option<PriceMicros>,
    pub hedge_worst_ask_now: Option<PriceMicros>,
    pub entry_fillable: QuantityMicros,
    pub hedge_fillable: QuantityMicros,
    pub pair_cost_now: Option<PriceMicros>,
    pub locked_profit_per_share_now: Option<PriceMicros>,
    pub locked_profit_micros_now: Option<i128>,
    pub roi_bps_now: Option<i64>,
    pub hedge_targets: HedgeTargets,
    pub blockers: Vec<ScalpBlocker>,
    pub mode: &'static str,
}

pub struct ScalpPlanner;

impl ScalpPlanner {
    pub fn hedge_targets(entry_price: Option<PriceMicros>) -> HedgeTargets {
        let Some(entry_price) = entry_price else {
            return HedgeTargets::EMPTY;
        };
        if entry_price <= PriceMicros::ZERO || entry_price >= PriceMicros::ONE {
            return HedgeTargets::EMPTY;
        }

        HedgeTargets {
            breakeven: price_sub_one(entry_price),
            roi_2: required_hedge(entry_price, 200),
            roi_5: required_hedge(entry_price, 500),
            roi_8: required_hedge(entry_price, 800),
            roi_10: required_hedge(entry_price, 1_000),
            roi_12: required_hedge(entry_price, 1_200),
        }
    }

    pub fn plan(input: ScalpInput<'_>) -> ScalpCandidate {
        let mut blockers = Vec::new();
        let entry_best_ask = input.entry_book.and_then(OrderBook::best_ask).map(|level| level.price);
        let hedge_best_ask = input.hedge_book.and_then(OrderBook::best_ask).map(|level| level.price);

        if input.entry_book.is_none() {
            blockers.push(ScalpBlocker::MissingEntryBook);
        }
        if input.hedge_book.is_none() {
            blockers.push(ScalpBlocker::MissingHedgeBook);
        }
        if input.entry_book.is_some() && entry_best_ask.is_none() {
            blockers.push(ScalpBlocker::NoEntryAsk);
        }
        if input.hedge_book.is_some() && hedge_best_ask.is_none() {
            blockers.push(ScalpBlocker::NoHedgeAsk);
        }

        let entry_fill = input.entry_book.map(|book| book.buy(input.target_shares));
        let hedge_fill = input.hedge_book.map(|book| book.buy(input.target_shares));

        if let Some(fill) = &entry_fill {
            if !fill.is_complete() {
                blockers.push(ScalpBlocker::InsufficientEntryDepth);
            }
        }
        if let Some(fill) = &hedge_fill {
            if !fill.is_complete() {
                blockers.push(ScalpBlocker::InsufficientHedgeDepth);
            }
        }

        let entry_vwap = entry_fill.as_ref().and_then(|fill| fill.is_complete().then_some(fill.vwap).flatten());
        let hedge_vwap_now = hedge_fill.as_ref().and_then(|fill| fill.is_complete().then_some(fill.vwap).flatten());
        let pair_cost_now = match (entry_vwap, hedge_vwap_now) {
            (Some(entry), Some(hedge)) => PriceMicros::new(entry.micros() + hedge.micros()).ok(),
            _ => None,
        };
        let locked_profit_per_share_now = pair_cost_now.and_then(price_sub_one);
        let locked_profit_micros_now = locked_profit_per_share_now.map(|profit| {
            (profit.micros() as i128 * input.target_shares.micros() as i128) / MICROS_PER_UNIT as i128
        });
        let roi_bps_now = match (locked_profit_per_share_now, pair_cost_now) {
            (Some(profit), Some(cost)) if cost.micros() > 0 => Some(((profit.micros() as i128 * 10_000) / cost.micros() as i128) as i64),
            _ => None,
        };

        let verdict = if !blockers.is_empty() {
            ScalpVerdict::Blocked
        } else if pair_cost_now.map(|price| price < PriceMicros::ONE).unwrap_or(false) {
            ScalpVerdict::TradeNow
        } else {
            ScalpVerdict::Watch
        };

        ScalpCandidate {
            verdict,
            event_id: input.event_id.to_string(),
            event_title: input.event_title.to_string(),
            market_id: input.market_id.to_string(),
            market_family: input.market_family.to_string(),
            entry_outcome: input.entry_outcome.to_string(),
            hedge_outcome: input.hedge_outcome.to_string(),
            entry_token_id: input.entry_book.map(|book| book.token_id.clone()),
            hedge_token_id: input.hedge_book.map(|book| book.token_id.clone()),
            target_shares: input.target_shares,
            entry_best_ask,
            hedge_best_ask,
            entry_vwap,
            hedge_vwap_now,
            entry_worst_ask: entry_fill.as_ref().and_then(|fill| fill.worst_price),
            hedge_worst_ask_now: hedge_fill.as_ref().and_then(|fill| fill.worst_price),
            entry_fillable: entry_fill.as_ref().map(|fill| fill.filled).unwrap_or(QuantityMicros::ZERO),
            hedge_fillable: hedge_fill.as_ref().map(|fill| fill.filled).unwrap_or(QuantityMicros::ZERO),
            pair_cost_now,
            locked_profit_per_share_now,
            locked_profit_micros_now,
            roi_bps_now,
            hedge_targets: Self::hedge_targets(entry_vwap.or(entry_best_ask)),
            blockers,
            mode: "WATCH_ONLY_NO_ORDER_PATH",
        }
    }
}

fn price_sub_one(price: PriceMicros) -> Option<PriceMicros> {
    PriceMicros::new(MICROS_PER_UNIT - price.micros()).ok()
}

fn required_hedge(entry_price: PriceMicros, roi_bps: i64) -> Option<PriceMicros> {
    let denominator = 10_000_i128 + roi_bps as i128;
    let fair_pair_cost = ((MICROS_PER_UNIT as i128 * 10_000_i128) + (denominator / 2)) / denominator;
    let target = fair_pair_cost - entry_price.micros() as i128;
    if !(0..=MICROS_PER_UNIT as i128).contains(&target) {
        return None;
    }
    PriceMicros::new(target as i64).ok()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::book::BookLevel;

    fn p(micros: i64) -> PriceMicros {
        PriceMicros::new(micros).unwrap()
    }

    fn book(token: &str, asks: &[(i64, u64)]) -> OrderBook {
        OrderBook::new(
            token,
            Some("cond".to_string()),
            100,
            vec![],
            asks.iter()
                .map(|(price, shares)| BookLevel::new(p(*price), QuantityMicros::whole(*shares)))
                .collect(),
        )
    }

    #[test]
    fn hedge_targets_use_denominator_formula() {
        let targets = ScalpPlanner::hedge_targets(Some(p(480_000)));
        assert_eq!(targets.breakeven, Some(p(520_000)));
        assert_eq!(targets.roi_2, Some(p(500_392)));
        assert_eq!(targets.roi_5, Some(p(472_381)));
        assert_eq!(targets.roi_8, Some(p(445_926)));
        assert_eq!(targets.roi_10, Some(p(429_091)));
        assert_eq!(targets.roi_12, Some(p(412_857)));
    }

    #[test]
    fn planner_uses_400_share_vwap_and_reports_trade_now_analysis_only() {
        let entry = book("under", &[(480_000, 100), (490_000, 300)]);
        let hedge = book("over", &[(440_000, 100), (450_000, 300)]);

        let candidate = ScalpPlanner::plan(ScalpInput {
            event_id: "event-1",
            event_title: "World Cup A vs B",
            market_id: "market-1",
            market_family: "MatchTotal",
            entry_outcome: "Under",
            hedge_outcome: "Over",
            entry_book: Some(&entry),
            hedge_book: Some(&hedge),
            target_shares: QuantityMicros::whole(400),
        });

        assert_eq!(candidate.verdict, ScalpVerdict::TradeNow);
        assert_eq!(candidate.entry_vwap, Some(p(487_500)));
        assert_eq!(candidate.hedge_vwap_now, Some(p(447_500)));
        assert_eq!(candidate.pair_cost_now, Some(p(935_000)));
        assert_eq!(candidate.locked_profit_per_share_now, Some(p(65_000)));
        assert_eq!(candidate.locked_profit_micros_now, Some(26_000_000));
        assert_eq!(candidate.roi_bps_now, Some(695));
        assert_eq!(candidate.mode, "WATCH_ONLY_NO_ORDER_PATH");
        assert!(candidate.blockers.is_empty());
    }

    #[test]
    fn planner_blocks_when_depth_is_incomplete() {
        let entry = book("under", &[(480_000, 399)]);
        let hedge = book("over", &[(440_000, 400)]);

        let candidate = ScalpPlanner::plan(ScalpInput {
            event_id: "event-1",
            event_title: "World Cup A vs B",
            market_id: "market-1",
            market_family: "MatchTotal",
            entry_outcome: "Under",
            hedge_outcome: "Over",
            entry_book: Some(&entry),
            hedge_book: Some(&hedge),
            target_shares: QuantityMicros::whole(400),
        });

        assert_eq!(candidate.verdict, ScalpVerdict::Blocked);
        assert!(candidate.blockers.contains(&ScalpBlocker::InsufficientEntryDepth));
        assert_eq!(candidate.entry_fillable, QuantityMicros::whole(399));
        assert_eq!(candidate.entry_vwap, None);
    }
}
