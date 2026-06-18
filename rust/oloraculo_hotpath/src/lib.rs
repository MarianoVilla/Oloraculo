//! Oloraculo-owned read-only hotpath primitives.
//!
//! This crate is intentionally small and dependency-free for Phase 1. It owns
//! deterministic CLOB book depth math and sports two-leg scalp planning. It has
//! no order placement, no credentials, no network clients, and no local raw-data
//! archive behavior.

pub mod book;
pub mod feed_status;
pub mod scalp;
pub mod snapshot;

pub use book::{DepthFill, OrderBook, PriceMicros, QuantityMicros, BookLevel};
pub use feed_status::{FeedReadiness, FeedStatusRow, FeedStatusSnapshot};
pub use scalp::{HedgeTargets, ScalpBlocker, ScalpCandidate, ScalpInput, ScalpPlanner, ScalpVerdict};
pub use snapshot::{SnapshotCandidate, SportsScalpSnapshot};
