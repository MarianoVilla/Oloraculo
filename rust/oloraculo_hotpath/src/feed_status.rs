#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FeedReadiness {
    Ready,
    Planned,
    ConfigMissing,
    NotImplemented,
    Blocked,
}

impl FeedReadiness {
    pub const fn as_str(self) -> &'static str {
        match self {
            Self::Ready => "READY",
            Self::Planned => "PLANNED",
            Self::ConfigMissing => "CONFIG_MISSING",
            Self::NotImplemented => "NOT_IMPLEMENTED",
            Self::Blocked => "BLOCKED",
        }
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct FeedStatusRow {
    pub source_id: String,
    pub source: String,
    pub role: String,
    pub readiness: FeedReadiness,
    pub present: bool,
    pub auth_present: Option<bool>,
    pub config_present: bool,
    pub latest_recv_ts_ms: Option<i64>,
    pub age_ms: Option<i64>,
    pub rows_last_minute: Option<u64>,
    pub join_coverage: Option<f64>,
    pub last_error_redacted: String,
    pub blocker: String,
    pub blockers: Vec<String>,
    pub detail: String,
    pub secret_policy: &'static str,
}

impl FeedStatusRow {
    #[allow(clippy::too_many_arguments)]
    pub fn canonical(
        source_id: impl Into<String>,
        source: impl Into<String>,
        role: impl Into<String>,
        readiness: FeedReadiness,
        present: bool,
        auth_present: Option<bool>,
        config_present: bool,
        blockers: Vec<String>,
    ) -> Self {
        let blocker = blockers.first().cloned().unwrap_or_default();
        Self {
            source_id: source_id.into(),
            source: source.into(),
            role: role.into(),
            readiness,
            present,
            auth_present,
            config_present,
            latest_recv_ts_ms: None,
            age_ms: None,
            rows_last_minute: None,
            join_coverage: None,
            last_error_redacted: String::new(),
            blocker,
            blockers,
            detail: String::new(),
            secret_policy: "PRESENCE_ONLY_NO_VALUES",
        }
    }

    pub fn sanitized(
        source: impl Into<String>,
        role: impl Into<String>,
        readiness: FeedReadiness,
        auth_present: Option<bool>,
        config_present: bool,
        blocker: impl Into<String>,
    ) -> Self {
        let source = source.into();
        let blocker = blocker.into();
        let blockers = if blocker.is_empty() {
            Vec::new()
        } else {
            vec![blocker]
        };
        Self::canonical(
            canonical_source_id(&source),
            source,
            role,
            readiness,
            readiness == FeedReadiness::Ready,
            auth_present,
            config_present,
            blockers,
        )
    }

    pub fn with_redacted_error(mut self, error: &str) -> Self {
        self.last_error_redacted = redact_error(error);
        self
    }

    pub fn with_detail(mut self, detail: impl Into<String>) -> Self {
        self.detail = detail.into();
        self
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct FeedStatusSnapshot {
    pub schema_version: u32,
    pub as_of_utc: String,
    pub mode: &'static str,
    pub rows: Vec<FeedStatusRow>,
}

impl FeedStatusSnapshot {
    pub fn new(as_of_ts_ms: i64, rows: Vec<FeedStatusRow>) -> Self {
        Self::new_at(unix_millis_to_utc(as_of_ts_ms), rows)
    }

    pub fn new_at(as_of_utc: impl Into<String>, rows: Vec<FeedStatusRow>) -> Self {
        Self {
            schema_version: 1,
            as_of_utc: as_of_utc.into(),
            mode: "SANITIZED_STATUS_ONLY",
            rows,
        }
    }

    pub fn config_missing_count(&self) -> usize {
        self.rows.iter().filter(|row| row.readiness == FeedReadiness::ConfigMissing).count()
    }

    pub fn ready_count(&self) -> usize {
        self.rows.iter().filter(|row| row.readiness == FeedReadiness::Ready).count()
    }

    pub fn planned_count(&self) -> usize {
        self.rows.iter().filter(|row| row.readiness == FeedReadiness::Planned).count()
    }

    pub fn not_implemented_count(&self) -> usize {
        self.rows.iter().filter(|row| row.readiness == FeedReadiness::NotImplemented).count()
    }

    pub fn blocked_count(&self) -> usize {
        self.rows.iter().filter(|row| row.readiness == FeedReadiness::Blocked).count()
    }

    pub fn to_json(&self) -> String {
        let mut out = String::new();
        out.push('{');
        push_json_key_u32(&mut out, "schema_version", self.schema_version, false);
        push_json_key_str(&mut out, "as_of_utc", &self.as_of_utc, true);
        push_json_key_str(&mut out, "generated_at_utc", &self.as_of_utc, true);
        out.push_str(",\"rows\":[");
        for (idx, row) in self.rows.iter().enumerate() {
            if idx > 0 {
                out.push(',');
            }
            row.push_json(&mut out);
        }
        out.push(']');
        push_json_key_str(&mut out, "mode", self.mode, true);
        push_json_key_usize(&mut out, "ready_count", self.ready_count(), true);
        push_json_key_usize(&mut out, "planned_count", self.planned_count(), true);
        push_json_key_usize(&mut out, "missing_config_count", self.config_missing_count(), true);
        push_json_key_usize(&mut out, "not_implemented_count", self.not_implemented_count(), true);
        push_json_key_usize(&mut out, "blocked_count", self.blocked_count(), true);
        out.push('}');
        out
    }
}

impl FeedStatusRow {
    fn push_json(&self, out: &mut String) {
        out.push('{');
        push_json_key_str(out, "source_id", &self.source_id, false);
        push_json_key_str(out, "source", &self.source, true);
        push_json_key_str(out, "role", &self.role, true);
        push_json_key_str(out, "readiness", self.readiness.as_str(), true);
        push_json_key_str(out, "state", self.readiness.as_str(), true);
        push_json_key_bool(out, "present", self.present, true);
        push_json_key_opt_bool(out, "auth_present", self.auth_present, true);
        push_json_key_bool(out, "config_present", self.config_present, true);
        push_json_key_opt_utc_millis(out, "latest_recv_ts_utc", self.latest_recv_ts_ms, true);
        push_json_key_opt_i64(out, "age_ms", self.age_ms, true);
        push_json_key_opt_u64(out, "rows_last_minute", self.rows_last_minute, true);
        push_json_key_opt_f64(out, "join_coverage", self.join_coverage, true);
        push_json_key_str(out, "last_error_redacted", &self.last_error_redacted, true);
        push_json_key_str(out, "blocker", &self.blocker, true);
        out.push_str(",\"blockers\":[");
        for (idx, blocker) in self.blockers.iter().enumerate() {
            if idx > 0 {
                out.push(',');
            }
            push_json_string(out, blocker);
        }
        out.push(']');
        push_json_key_str(out, "detail", &self.detail, true);
        push_json_key_str(out, "secret_policy", self.secret_policy, true);
        out.push('}');
    }
}

pub fn redact_error(error: &str) -> String {
    let lowered = error.to_ascii_lowercase();
    let secret_markers = ["bearer", "token", "xauth", "secret", "private_key", "private-key", "api_key", "api-key"];
    if secret_markers.iter().any(|marker| lowered.contains(marker)) || looks_like_private_key(error) || looks_like_jwt(error) {
        return "<redacted credential-like error>".to_string();
    }
    if error.chars().count() > 240 {
        let mut out: String = error.chars().take(240).collect();
        out.push_str("...");
        out
    } else {
        error.to_string()
    }
}

fn looks_like_private_key(error: &str) -> bool {
    error.split_whitespace().any(|part| {
        let value = part.trim_matches(|ch: char| !ch.is_ascii_hexdigit() && ch != 'x');
        value.starts_with("0x") && value.len() == 66 && value[2..].chars().all(|ch| ch.is_ascii_hexdigit())
    })
}

fn looks_like_jwt(error: &str) -> bool {
    error.split_whitespace().any(|part| {
        let dots = part.matches('.').count();
        dots == 2 && part.starts_with("eyJ") && part.len() > 30
    })
}

fn canonical_source_id(source: &str) -> String {
    source
        .chars()
        .map(|ch| if ch.is_ascii_alphanumeric() { ch.to_ascii_lowercase() } else { '_' })
        .collect::<String>()
        .split('_')
        .filter(|part| !part.is_empty())
        .collect::<Vec<_>>()
        .join("_")
}

fn unix_millis_to_utc(ms: i64) -> String {
    let seconds = ms.div_euclid(1_000);
    let millis = ms.rem_euclid(1_000);
    let days = seconds.div_euclid(86_400);
    let second_of_day = seconds.rem_euclid(86_400);
    let (year, month, day) = civil_from_days(days);
    let hour = second_of_day / 3_600;
    let minute = (second_of_day % 3_600) / 60;
    let second = second_of_day % 60;
    format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}.{millis:03}+00:00")
}

fn civil_from_days(days_since_epoch: i64) -> (i64, u32, u32) {
    let z = days_since_epoch + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1_460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = mp + if mp < 10 { 3 } else { -9 };
    let year = y + if month <= 2 { 1 } else { 0 };
    (year, month as u32, day as u32)
}

fn push_json_key_u32(out: &mut String, key: &str, value: u32, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    out.push_str(&value.to_string());
}

fn push_json_key_usize(out: &mut String, key: &str, value: usize, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    out.push_str(&value.to_string());
}

fn push_json_key_opt_utc_millis(out: &mut String, key: &str, value: Option<i64>, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    match value {
        Some(value) => push_json_string(out, &unix_millis_to_utc(value)),
        None => out.push_str("null"),
    }
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

fn push_json_key_opt_u64(out: &mut String, key: &str, value: Option<u64>, comma: bool) {
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

fn push_json_key_opt_f64(out: &mut String, key: &str, value: Option<f64>, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    match value {
        Some(value) if value.is_finite() => out.push_str(&value.to_string()),
        _ => out.push_str("null"),
    }
}

fn push_json_key_bool(out: &mut String, key: &str, value: bool, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    out.push_str(if value { "true" } else { "false" });
}

fn push_json_key_opt_bool(out: &mut String, key: &str, value: Option<bool>, comma: bool) {
    if comma {
        out.push(',');
    }
    push_json_string(out, key);
    out.push(':');
    match value {
        Some(true) => out.push_str("true"),
        Some(false) => out.push_str("false"),
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

    #[test]
    fn row_contract_is_sanitized_presence_only() {
        let row = FeedStatusRow::sanitized(
            "Databet sportsbook",
            "external live odds/state",
            FeedReadiness::ConfigMissing,
            Some(false),
            false,
            "AUTH_CONFIG_MISSING",
        );
        assert_eq!(row.secret_policy, "PRESENCE_ONLY_NO_VALUES");
        assert_eq!(row.auth_present, Some(false));
        assert!(row.last_error_redacted.is_empty());
    }

    #[test]
    fn redacts_credential_like_errors() {
        assert_eq!(redact_error("xauth: set"), "<redacted credential-like error>");
        assert_eq!(redact_error("api_key=set"), "<redacted credential-like error>");
        assert_eq!(redact_error("normal timeout"), "normal timeout");
    }

    #[test]
    fn snapshot_counts_missing_config() {
        let snapshot = FeedStatusSnapshot::new(
            10,
            vec![
                FeedStatusRow::sanitized("A", "role", FeedReadiness::ConfigMissing, Some(false), false, "missing"),
                FeedStatusRow::sanitized("B", "role", FeedReadiness::Planned, Some(true), true, "pending"),
            ],
        );
        assert_eq!(snapshot.mode, "SANITIZED_STATUS_ONLY");
        assert_eq!(snapshot.config_missing_count(), 1);
    }

    #[test]
    fn feed_status_json_uses_canonical_contract() {
        let row = FeedStatusRow::canonical(
            "databet_sportsbook",
            "Databet sportsbook",
            "external live odds/state",
            FeedReadiness::Planned,
            false,
            Some(true),
            true,
            vec!["COLLECTOR_NOT_ENABLED".to_string()],
        )
        .with_detail("collector configured but not running");
        let snapshot = FeedStatusSnapshot::new_at("2026-06-18T03:04:05+00:00", vec![row]);
        let json = snapshot.to_json();
        let fixture = include_str!("../../../docs/source-of-truth/fixtures/feed_status_snapshot_v1.json")
            .trim_end_matches(['\r', '\n']);

        assert!(json.contains("\"schema_version\":1"));
        assert!(json.contains("\"as_of_utc\":\"2026-06-18T03:04:05+00:00\""));
        assert!(json.contains("\"source_id\":\"databet_sportsbook\""));
        assert!(json.contains("\"readiness\":\"PLANNED\""));
        assert!(json.contains("\"state\":\"PLANNED\""));
        assert!(json.contains("\"present\":false"));
        assert!(json.contains("\"blockers\":[\"COLLECTOR_NOT_ENABLED\"]"));
        assert!(json.contains("\"secret_policy\":\"PRESENCE_ONLY_NO_VALUES\""));
        assert!(!json.contains("raw-secret-value"));
        assert_eq!(fixture, json);
    }

    #[test]
    fn redaction_truncates_non_ascii_without_panicking() {
        let long = "é".repeat(300);
        let redacted = redact_error(&long);

        assert!(redacted.ends_with("..."));
        assert!(redacted.len() > 3);
    }
}
