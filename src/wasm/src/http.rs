//! Pure HTTP helpers shared by the MCP and workflow clients. Mirrors `http.ts` / `http.py`.
//! Token resolution and `fetch` itself live in the JS binding layer (a later checkpoint).

use crate::errors::FoundgineError;
use serde_json::Value;

/// Join URL components while normalizing repeated boundary slashes (Python/TypeScript parity).
pub fn join_url(base_url: &str, path: &str) -> String {
    let base = base_url.trim_end_matches('/');
    let path = path.trim_start_matches('/');
    format!("{base}/{path}")
}

/// Percent-encode a path segment, leaving only RFC 3986 unreserved characters (`A-Za-z0-9-._~`).
/// Same as Python `quote(s, safe="")`. TS `encodeURIComponent` also leaves `! * ' ( )` raw; servers
/// decode both identically, so this is a deliberate, stricter choice: `/`, `?`, `#` and non-ASCII can
/// never change the route.
pub fn encode_path_segment(segment: &str) -> String {
    let mut out = String::with_capacity(segment.len());
    for b in segment.bytes() {
        if b.is_ascii_alphanumeric() || matches!(b, b'-' | b'.' | b'_' | b'~') {
            out.push(b as char);
        } else {
            out.push_str(&format!("%{b:02X}"));
        }
    }
    out
}

/// An empty token (or none) means "no token"; the SDK then sends no `Authorization` header.
pub fn normalize_token(token: Option<&str>) -> Option<String> {
    match token {
        Some(t) if !t.is_empty() => Some(t.to_string()),
        _ => None,
    }
}

/// `Authorization` header value for a token, if any.
pub fn bearer_header(token: Option<&str>) -> Option<String> {
    normalize_token(token).map(|t| format!("Bearer {t}"))
}

/// `errorFor`: build the HTTP error for a non-success response from its status and body text.
/// The detail is `error` (string) or `error.message` from a JSON body, else the first 200 characters
/// of a non-JSON body (TS cuts at 200 UTF-16 units; this cuts at 200 characters).
pub fn error_for(status: u16, text: &str) -> FoundgineError {
    let parsed: Option<Value> = if text.is_empty() { None } else { serde_json::from_str(text).ok() };
    let mut detail: Option<String> = None;
    if let Some(Value::Object(map)) = &parsed {
        match map.get("error") {
            Some(Value::String(s)) => detail = Some(s.clone()),
            Some(Value::Object(inner)) => {
                if let Some(Value::String(m)) = inner.get("message") {
                    detail = Some(m.clone());
                }
            }
            _ => {}
        }
    }
    // TS: `!detail && text && body === undefined` (JSON that parsed, even `null`, never takes this path).
    if detail.as_deref().map_or(true, str::is_empty) && !text.is_empty() && parsed.is_none() {
        let cut: String = text.chars().take(200).collect();
        let trimmed = cut.trim();
        detail = if trimmed.is_empty() { None } else { Some(trimmed.to_string()) };
    }
    let message = match detail.as_deref() {
        Some(d) if !d.is_empty() => format!("HTTP {status}: {d}"),
        _ => format!("HTTP {status}"),
    };
    FoundgineError::http(status, Some(message), parsed)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn join_url_normalizes_boundary_slashes() {
        assert_eq!(join_url("https://h/", "/mcp"), "https://h/mcp");
        assert_eq!(join_url("https://h///", "///mcp"), "https://h/mcp");
        assert_eq!(join_url("https://h", "mcp"), "https://h/mcp");
    }

    #[test]
    fn path_segments_cannot_change_the_route() {
        assert_eq!(encode_path_segment("k 1"), "k%201");
        assert_eq!(encode_path_segment("a/b?c#d"), "a%2Fb%3Fc%23d");
        assert_eq!(encode_path_segment("\u{e9}"), "%C3%A9");
        assert_eq!(encode_path_segment("A-z_0.9~"), "A-z_0.9~");
    }

    #[test]
    fn tokens() {
        assert_eq!(bearer_header(Some("tok")).as_deref(), Some("Bearer tok"));
        assert_eq!(bearer_header(Some("")), None);
        assert_eq!(bearer_header(None), None);
    }

    #[test]
    fn error_detail_from_string_error() {
        let e = error_for(403, r#"{"error":"forbidden"}"#);
        assert_eq!(e.message(), "HTTP 403: forbidden");
        assert_eq!(e.status(), Some(403));
    }

    #[test]
    fn error_detail_from_nested_message_and_body_kept() {
        let e = error_for(400, r#"{"error":{"message":"bad"}}"#);
        assert_eq!(e.message(), "HTTP 400: bad");
        match e {
            FoundgineError::Http { body, .. } => assert_eq!(body, Some(json!({"error": {"message": "bad"}}))),
            _ => panic!("expected Http"),
        }
    }

    #[test]
    fn non_json_body_is_trimmed_and_cut() {
        assert_eq!(error_for(502, "  Bad Gateway \n").message(), "HTTP 502: Bad Gateway");
        let long = "x".repeat(500);
        assert_eq!(error_for(500, &long).message(), format!("HTTP 500: {}", "x".repeat(200)));
    }

    #[test]
    fn empty_or_json_without_detail_is_status_only() {
        assert_eq!(error_for(404, "").message(), "HTTP 404");
        assert_eq!(error_for(404, "null").message(), "HTTP 404");
        assert_eq!(error_for(409, r#"{"x":1}"#).message(), "HTTP 409");
    }
}
