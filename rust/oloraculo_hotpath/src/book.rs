use core::cmp::Ordering;
use core::fmt;

pub const MICROS_PER_UNIT: i64 = 1_000_000;

/// Polymarket binary price stored as millionths of $1.00.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct PriceMicros(i64);

impl PriceMicros {
    pub const ZERO: Self = Self(0);
    pub const ONE: Self = Self(MICROS_PER_UNIT);

    pub fn new(micros: i64) -> Result<Self, String> {
        if !(0..=MICROS_PER_UNIT).contains(&micros) {
            return Err(format!("price_micros_out_of_range:{micros}"));
        }
        Ok(Self(micros))
    }

    pub const fn from_micros_unchecked(micros: i64) -> Self {
        Self(micros)
    }

    pub const fn micros(self) -> i64 {
        self.0
    }

    pub fn as_decimal_string(self) -> String {
        let whole = self.0 / MICROS_PER_UNIT;
        let frac = (self.0 % MICROS_PER_UNIT).abs();
        format!("{whole}.{frac:06}")
    }
}

impl fmt::Display for PriceMicros {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.as_decimal_string())
    }
}

/// Shares stored as millionths so future non-whole-size venues remain exact.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct QuantityMicros(u64);

impl QuantityMicros {
    pub const ZERO: Self = Self(0);

    pub const fn from_micros(micros: u64) -> Self {
        Self(micros)
    }

    pub const fn whole(shares: u64) -> Self {
        Self(shares * MICROS_PER_UNIT as u64)
    }

    pub const fn micros(self) -> u64 {
        self.0
    }

    pub fn as_shares_f64(self) -> f64 {
        self.0 as f64 / MICROS_PER_UNIT as f64
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct BookLevel {
    pub price: PriceMicros,
    pub size: QuantityMicros,
}

impl BookLevel {
    pub const fn new(price: PriceMicros, size: QuantityMicros) -> Self {
        Self { price, size }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DepthFill {
    pub requested: QuantityMicros,
    pub filled: QuantityMicros,
    pub cost_micros: i128,
    pub vwap: Option<PriceMicros>,
    pub worst_price: Option<PriceMicros>,
    pub consumed_levels: Vec<BookLevel>,
}

impl DepthFill {
    pub fn is_complete(&self) -> bool {
        self.filled >= self.requested
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct OrderBook {
    pub token_id: String,
    pub condition_id: Option<String>,
    pub recv_ts_ms: i64,
    pub bids: Vec<BookLevel>,
    pub asks: Vec<BookLevel>,
}

impl OrderBook {
    pub fn new(
        token_id: impl Into<String>,
        condition_id: Option<String>,
        recv_ts_ms: i64,
        mut bids: Vec<BookLevel>,
        mut asks: Vec<BookLevel>,
    ) -> Self {
        bids.sort_by(|a, b| b.price.cmp(&a.price).then_with(|| b.size.cmp(&a.size)));
        asks.sort_by(|a, b| a.price.cmp(&b.price).then_with(|| b.size.cmp(&a.size)));
        Self {
            token_id: token_id.into(),
            condition_id,
            recv_ts_ms,
            bids,
            asks,
        }
    }

    pub fn best_bid(&self) -> Option<BookLevel> {
        self.bids.first().copied()
    }

    pub fn best_ask(&self) -> Option<BookLevel> {
        self.asks.first().copied()
    }

    pub fn buy(&self, shares: QuantityMicros) -> DepthFill {
        consume_levels(self.asks.iter().copied(), shares, None)
    }

    pub fn sell(&self, shares: QuantityMicros) -> DepthFill {
        consume_levels(self.bids.iter().copied(), shares, None)
    }

    pub fn buy_up_to(&self, shares: QuantityMicros, max_price: PriceMicros) -> DepthFill {
        consume_levels(self.asks.iter().copied(), shares, Some(max_price))
    }
}

fn consume_levels<I>(levels: I, requested: QuantityMicros, max_price: Option<PriceMicros>) -> DepthFill
where
    I: IntoIterator<Item = BookLevel>,
{
    if requested == QuantityMicros::ZERO {
        return DepthFill {
            requested,
            filled: QuantityMicros::ZERO,
            cost_micros: 0,
            vwap: None,
            worst_price: None,
            consumed_levels: Vec::new(),
        };
    }

    let mut remaining = requested.micros();
    let mut filled = 0_u64;
    let mut cost_micros = 0_i128;
    let mut worst_price = None;
    let mut consumed_levels = Vec::new();

    for level in levels {
        if let Some(max_price) = max_price {
            if level.price > max_price {
                continue;
            }
        }
        if level.size == QuantityMicros::ZERO {
            continue;
        }
        let take = remaining.min(level.size.micros());
        if take == 0 {
            break;
        }
        filled += take;
        cost_micros += (take as i128 * level.price.micros() as i128) / MICROS_PER_UNIT as i128;
        worst_price = Some(level.price);
        consumed_levels.push(BookLevel::new(level.price, QuantityMicros::from_micros(take)));
        remaining -= take;
        if remaining == 0 {
            break;
        }
    }

    let vwap = if filled == 0 {
        None
    } else {
        let rounded = ((cost_micros * MICROS_PER_UNIT as i128) + (filled as i128 / 2)) / filled as i128;
        Some(PriceMicros::from_micros_unchecked(rounded as i64))
    };

    DepthFill {
        requested,
        filled: QuantityMicros::from_micros(filled),
        cost_micros,
        vwap,
        worst_price,
        consumed_levels,
    }
}

#[allow(dead_code)]
fn cmp_price_desc(a: &BookLevel, b: &BookLevel) -> Ordering {
    b.price.cmp(&a.price)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn p(micros: i64) -> PriceMicros {
        PriceMicros::new(micros).unwrap()
    }

    #[test]
    fn sorts_book_sides_canonically() {
        let book = OrderBook::new(
            "tok",
            Some("cond".to_string()),
            10,
            vec![
                BookLevel::new(p(430_000), QuantityMicros::whole(100)),
                BookLevel::new(p(450_000), QuantityMicros::whole(50)),
            ],
            vec![
                BookLevel::new(p(480_000), QuantityMicros::whole(100)),
                BookLevel::new(p(460_000), QuantityMicros::whole(50)),
            ],
        );

        assert_eq!(book.best_bid().unwrap().price, p(450_000));
        assert_eq!(book.best_ask().unwrap().price, p(460_000));
        assert_eq!(book.bids[1].price, p(430_000));
        assert_eq!(book.asks[1].price, p(480_000));
    }

    #[test]
    fn buy_consumes_depth_and_computes_vwap() {
        let book = OrderBook::new(
            "tok",
            None,
            10,
            vec![],
            vec![
                BookLevel::new(p(460_000), QuantityMicros::whole(150)),
                BookLevel::new(p(480_000), QuantityMicros::whole(250)),
            ],
        );

        let fill = book.buy(QuantityMicros::whole(400));
        assert!(fill.is_complete());
        assert_eq!(fill.filled, QuantityMicros::whole(400));
        assert_eq!(fill.vwap, Some(p(472_500)));
        assert_eq!(fill.worst_price, Some(p(480_000)));
        assert_eq!(fill.consumed_levels.len(), 2);
    }

    #[test]
    fn buy_up_to_reports_partial_capacity() {
        let book = OrderBook::new(
            "tok",
            None,
            10,
            vec![],
            vec![
                BookLevel::new(p(460_000), QuantityMicros::whole(150)),
                BookLevel::new(p(480_000), QuantityMicros::whole(250)),
            ],
        );

        let fill = book.buy_up_to(QuantityMicros::whole(400), p(460_000));
        assert!(!fill.is_complete());
        assert_eq!(fill.filled, QuantityMicros::whole(150));
        assert_eq!(fill.vwap, Some(p(460_000)));
    }
}
