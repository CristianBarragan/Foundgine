//! Error taxonomy. Mirrors `errors.ts` / `errors.py`.
//!
//! TS and Python use a class hierarchy; Rust uses one enum. `is_http_error()` is true for the three
//! variants that are `FoundgineHttpError` or a subclass of it (the TS `instanceof FoundgineHttpError`).

use serde_json::Value;
use std::fmt;

pub const DEFAULT_SESSION_EXPIRED: &str = "MCP session expired or unknown";
pub const DEFAULT_RESYNC_REQUIRED: &str = "Event replay buffer no longer holds the requested event; resync required";
pub const DEFAULT_ABORT: &str = "Operation aborted";
pub const DEFAULT_WATCH_TIMEOUT: &str = "Timed out waiting for the workflow to finish";

#[derive(Debug, Clone, PartialEq)]
pub enum FoundgineError {
    /// The base `FoundgineError`: a failure that is none of the specific classes below.
    Other { message: String },
    /// `FoundgineHttpError`: a non-success HTTP status.
    Http { status: u16, message: String, body: Option<Value> },
    /// `FoundgineSessionExpiredError` (HTTP 404 on an MCP session call).
    SessionExpired { message: String, body: Option<Value> },
    /// `FoundgineResyncRequiredError` (HTTP 410 on the notification stream).
    ResyncRequired { message: String, body: Option<Value> },
    /// `FoundgineRpcError`: a JSON-RPC protocol error. Tool failures are data, not this.
    Rpc { code: i64, message: String, data: Option<Value> },
    /// `FoundgineAbortError`: the caller aborted the operation.
    Abort { message: String },
    /// `FoundgineWatchTimeoutError`: a workflow watch hit its timeout.
    WatchTimeout { last_status: Option<Value>, message: String },
}

impl FoundgineError {
    /// `new FoundgineHttpError(status, message?, body?)`; the message defaults to `HTTP <status>`.
    pub fn http(status: u16, message: Option<String>, body: Option<Value>) -> Self {
        let message = message.unwrap_or_else(|| format!("HTTP {status}"));
        FoundgineError::Http { status, message, body }
    }

    pub fn session_expired(message: Option<String>, body: Option<Value>) -> Self {
        FoundgineError::SessionExpired { message: message.unwrap_or_else(|| DEFAULT_SESSION_EXPIRED.to_string()), body }
    }

    pub fn resync_required(message: Option<String>, body: Option<Value>) -> Self {
        FoundgineError::ResyncRequired { message: message.unwrap_or_else(|| DEFAULT_RESYNC_REQUIRED.to_string()), body }
    }

    pub fn other(message: impl Into<String>) -> Self {
        FoundgineError::Other { message: message.into() }
    }

    pub fn rpc(code: i64, message: impl Into<String>, data: Option<Value>) -> Self {
        FoundgineError::Rpc { code, message: message.into(), data }
    }

    pub fn abort(message: Option<String>) -> Self {
        FoundgineError::Abort { message: message.unwrap_or_else(|| DEFAULT_ABORT.to_string()) }
    }

    pub fn watch_timeout(last_status: Option<Value>, message: Option<String>) -> Self {
        FoundgineError::WatchTimeout { last_status, message: message.unwrap_or_else(|| DEFAULT_WATCH_TIMEOUT.to_string()) }
    }

    /// The TS class name (`error.name`), useful to the JS binding layer.
    pub fn name(&self) -> &'static str {
        match self {
            FoundgineError::Other { .. } => "FoundgineError",
            FoundgineError::Http { .. } => "FoundgineHttpError",
            FoundgineError::SessionExpired { .. } => "FoundgineSessionExpiredError",
            FoundgineError::ResyncRequired { .. } => "FoundgineResyncRequiredError",
            FoundgineError::Rpc { .. } => "FoundgineRpcError",
            FoundgineError::Abort { .. } => "FoundgineAbortError",
            FoundgineError::WatchTimeout { .. } => "FoundgineWatchTimeoutError",
        }
    }

    /// HTTP status for the three HTTP-derived variants (404 and 410 for the subclasses).
    pub fn status(&self) -> Option<u16> {
        match self {
            FoundgineError::Http { status, .. } => Some(*status),
            FoundgineError::SessionExpired { .. } => Some(404),
            FoundgineError::ResyncRequired { .. } => Some(410),
            _ => None,
        }
    }

    /// True where TS `instanceof FoundgineHttpError` is true.
    pub fn is_http_error(&self) -> bool {
        self.status().is_some()
    }

    /// The error message exactly as the TS `error.message` (the RPC variant is prefixed).
    pub fn message(&self) -> String {
        match self {
            FoundgineError::Other { message }
            | FoundgineError::Http { message, .. }
            | FoundgineError::SessionExpired { message, .. }
            | FoundgineError::ResyncRequired { message, .. }
            | FoundgineError::Abort { message }
            | FoundgineError::WatchTimeout { message, .. } => message.clone(),
            FoundgineError::Rpc { code, message, .. } => format!("JSON-RPC error {code}: {message}"),
        }
    }
}

/// Display never includes a token or an `Authorization` header: errors do not carry request headers.
impl fmt::Display for FoundgineError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.message())
    }
}

impl std::error::Error for FoundgineError {}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn http_default_message() {
        let e = FoundgineError::http(503, None, None);
        assert_eq!(e.message(), "HTTP 503");
        assert_eq!(e.status(), Some(503));
        assert_eq!(e.name(), "FoundgineHttpError");
    }

    #[test]
    fn subclasses_are_http_errors_with_fixed_status() {
        let s = FoundgineError::session_expired(None, None);
        let r = FoundgineError::resync_required(None, None);
        assert!(s.is_http_error() && r.is_http_error());
        assert_eq!((s.status(), r.status()), (Some(404), Some(410)));
        assert_eq!(s.message(), DEFAULT_SESSION_EXPIRED);
        assert_eq!(r.message(), DEFAULT_RESYNC_REQUIRED);
    }

    #[test]
    fn rpc_message_is_prefixed_and_not_http() {
        let e = FoundgineError::rpc(-32601, "Method not found", None);
        assert_eq!(e.to_string(), "JSON-RPC error -32601: Method not found");
        assert!(!e.is_http_error());
    }

    #[test]
    fn other_is_the_base_error() {
        let e = FoundgineError::other("boom");
        assert_eq!((e.name(), e.message(), e.is_http_error()), ("FoundgineError", "boom".to_string(), false));
    }

    #[test]
    fn abort_and_watch_timeout_are_not_http() {
        assert!(!FoundgineError::abort(None).is_http_error());
        let t = FoundgineError::watch_timeout(Some(serde_json::json!({"s": 1})), None);
        assert!(!t.is_http_error());
        assert_eq!(t.message(), DEFAULT_WATCH_TIMEOUT);
    }
}
