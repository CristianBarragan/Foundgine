//! Sans-IO MCP session over HTTP. Mirrors `mcp.ts` / `mcp.py` (`McpSession`).
//!
//! This type does no I/O. The host (the JS binding layer in a later checkpoint, or a test) sends each
//! `HttpRequest` with its own `fetch` and hands the `HttpResponse` back. Token resolution is the host's job
//! too: pass the resolved token (or `None`). Because the host drives calls one at a time through a promise
//! chain, "one initialize for concurrent first calls" is enforced there, not here.

use crate::errors::FoundgineError;
use crate::http::{bearer_header, error_for, join_url};
use crate::sse::{SseEvent, SseParser};
use serde_json::{json, Map, Value};

pub const DEFAULT_PROTOCOL_VERSION: &str = "2025-06-18";
pub const SESSION_HEADER: &str = "Mcp-Session-Id";
pub const PROTOCOL_HEADER: &str = "MCP-Protocol-Version";
pub const DEFAULT_USER_AGENT: &str = "foundgine-sdk-wasm/3.3.4";
pub const DEFAULT_RECONNECT_DELAY_MS: u64 = 1000;
const ACCEPT_BOTH: &str = "application/json, text/event-stream";
const ACCEPT_STREAM: &str = "text/event-stream";
const NOT_AN_OBJECT: &str = "The server answered with a JSON-RPC message that is not an object";

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HttpRequest {
    pub method: &'static str,
    pub url: String,
    pub headers: Vec<(String, String)>,
    pub body: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HttpResponse {
    pub status: u16,
    pub headers: Vec<(String, String)>,
    pub body: String,
}

impl HttpResponse {
    /// `response.ok`: 200 to 299.
    pub fn ok(&self) -> bool {
        (200..300).contains(&self.status)
    }

    /// Case-insensitive header lookup.
    pub fn header(&self, name: &str) -> Option<&str> {
        self.headers.iter().find(|(k, _)| k.eq_ignore_ascii_case(name)).map(|(_, v)| v.as_str())
    }
}

#[derive(Debug, Clone)]
pub struct McpProtocol {
    url: String,
    requested_version: String,
    user_agent: String,
    version: Option<String>,
    session: Option<String>,
    ready: bool,
    next_id: u64,
    last_event_id: Option<String>,
}

impl McpProtocol {
    pub fn new(base_url: &str, path: Option<&str>, protocol_version: Option<&str>, user_agent: Option<&str>) -> Self {
        McpProtocol {
            url: join_url(base_url, path.unwrap_or("/mcp")),
            requested_version: protocol_version.unwrap_or(DEFAULT_PROTOCOL_VERSION).to_string(),
            user_agent: user_agent.unwrap_or(DEFAULT_USER_AGENT).to_string(),
            version: None,
            session: None,
            ready: false,
            next_id: 1,
            last_event_id: None,
        }
    }

    pub fn url(&self) -> &str {
        &self.url
    }
    pub fn session(&self) -> Option<&str> {
        self.session.as_deref()
    }
    pub fn is_ready(&self) -> bool {
        self.ready
    }
    pub fn last_event_id(&self) -> Option<&str> {
        self.last_event_id.as_deref()
    }
    pub fn set_last_event_id(&mut self, id: Option<String>) {
        self.last_event_id = id;
    }

    fn take_id(&mut self) -> u64 {
        let id = self.next_id;
        self.next_id += 1;
        id
    }

    fn headers(&self, session: Option<&str>, json_body: bool, accept: &str, token: Option<&str>) -> Vec<(String, String)> {
        let mut h = vec![("Accept".to_string(), accept.to_string()), ("User-Agent".to_string(), self.user_agent.clone())];
        if let Some(auth) = bearer_header(token) {
            h.push(("Authorization".to_string(), auth));
        }
        if let Some(s) = session {
            h.push((SESSION_HEADER.to_string(), s.to_string()));
        }
        if self.ready {
            if let Some(v) = &self.version {
                h.push((PROTOCOL_HEADER.to_string(), v.clone()));
            }
        }
        if json_body {
            h.push(("Content-Type".to_string(), "application/json".to_string()));
        }
        h
    }

    fn post(&self, message: Value, token: Option<&str>) -> HttpRequest {
        HttpRequest { method: "POST", url: self.url.clone(), headers: self.headers(self.session.as_deref(), true, ACCEPT_BOTH, token), body: Some(message.to_string()) }
    }

    fn expire(&mut self, session: Option<&str>) {
        if self.session.as_deref() == session {
            self.session = None;
            self.ready = false;
        }
    }

    /// Step 1 of initialization: the `initialize` POST (no session, no protocol header yet).
    pub fn initialize_request(&mut self, token: Option<&str>) -> (HttpRequest, u64) {
        let id = self.take_id();
        let payload = json!({
            "jsonrpc": "2.0", "id": id, "method": "initialize",
            "params": {
                "protocolVersion": self.requested_version,
                "capabilities": {},
                "clientInfo": { "name": "foundgine-wasm", "version": "3.3.4" }
            }
        });
        let request = HttpRequest { method: "POST", url: self.url.clone(), headers: self.headers(None, true, ACCEPT_BOTH, token), body: Some(payload.to_string()) };
        (request, id)
    }

    /// Step 2: interpret the initialize response, mark the session ready and return the
    /// `notifications/initialized` POST the host must send next (check it with `check_notification_response`).
    pub fn complete_initialize(&mut self, id: u64, resp: &HttpResponse, token: Option<&str>) -> Result<HttpRequest, FoundgineError> {
        if !resp.ok() {
            return Err(error_for(resp.status, &resp.body));
        }
        let wire: Value = serde_json::from_str(&resp.body).map_err(|_| FoundgineError::other("The server answered the initialize request with a body that is not JSON"))?;
        if wire.get("id") != Some(&json!(id)) {
            return Err(FoundgineError::other("The server returned an unexpected initialize response id"));
        }
        self.version = Some(wire.get("result").and_then(|r| r.get("protocolVersion")).and_then(Value::as_str).unwrap_or(&self.requested_version).to_string());
        self.session = resp.header(SESSION_HEADER).map(String::from);
        self.ready = true;
        Ok(self.post(json!({ "jsonrpc": "2.0", "method": "notifications/initialized" }), token))
    }

    /// A JSON-RPC request. The host must have completed initialization first.
    pub fn request_message(&mut self, method: &str, params: Option<Value>, token: Option<&str>) -> (HttpRequest, u64) {
        let id = self.take_id();
        let mut message = Map::new();
        message.insert("jsonrpc".into(), json!("2.0"));
        message.insert("id".into(), json!(id));
        message.insert("method".into(), json!(method));
        if let Some(p) = params {
            message.insert("params".into(), p);
        }
        (self.post(Value::Object(message), token), id)
    }

    /// A JSON-RPC notification (no id).
    pub fn notify_message(&self, method: &str, params: Option<Value>, token: Option<&str>) -> HttpRequest {
        let mut message = Map::new();
        message.insert("jsonrpc".into(), json!("2.0"));
        message.insert("method".into(), json!(method));
        if let Some(p) = params {
            message.insert("params".into(), p);
        }
        self.post(Value::Object(message), token)
    }

    /// Best-effort `notifications/cancelled` for an in-flight request id.
    pub fn cancel_request(&self, id: u64, token: Option<&str>) -> HttpRequest {
        self.notify_message("notifications/cancelled", Some(json!({ "requestId": id, "reason": "cancelled by client" })), token)
    }

    /// For notification POSTs: any non-success status is an HTTP error.
    pub fn check_notification_response(&self, resp: &HttpResponse) -> Result<(), FoundgineError> {
        if resp.ok() {
            Ok(())
        } else {
            Err(error_for(resp.status, &resp.body))
        }
    }

    /// Interpret the response to a request. `Ok(None)` for 202/204 and for a result-less message.
    /// A 404 with a live session clears the session (the next call re-initializes) and becomes
    /// `SessionExpired`; a JSON-RPC `error` becomes `Rpc`; tool failures are data, not errors.
    pub fn parse_response(&mut self, id: u64, resp: &HttpResponse) -> Result<Option<Value>, FoundgineError> {
        if !resp.ok() {
            let error = error_for(resp.status, &resp.body);
            if resp.status == 404 && self.session.is_some() {
                let session = self.session.clone();
                self.expire(session.as_deref());
                return Err(match error {
                    FoundgineError::Http { message, body, .. } => FoundgineError::session_expired(Some(message), body),
                    other => other,
                });
            }
            return Err(error);
        }
        if resp.status == 202 || resp.status == 204 {
            return Ok(None);
        }
        let content_type = resp.header("content-type").map(|v| v.split(';').next().unwrap_or("").trim().to_lowercase());
        let wire: Option<Value> = if content_type.as_deref() == Some("text/event-stream") {
            let mut parser = SseParser::new();
            match parser.push_str(&resp.body).into_iter().find(|e| !e.data.is_empty()) {
                Some(e) => Some(serde_json::from_str(&e.data).map_err(|_| FoundgineError::other("The server answered with an event that is not JSON"))?),
                None => None,
            }
        } else {
            Some(serde_json::from_str(&resp.body).map_err(|_| FoundgineError::other("The server answered with a body that is not JSON"))?)
        };
        let obj = match &wire {
            Some(Value::Object(m)) => m,
            _ => return Err(FoundgineError::other(NOT_AN_OBJECT)),
        };
        if let Some(Value::Object(err)) = obj.get("error") {
            let code = err.get("code").and_then(Value::as_i64).unwrap_or(-32603);
            let message = err.get("message").and_then(Value::as_str).unwrap_or("").to_string();
            return Err(FoundgineError::rpc(code, message, err.get("data").cloned()));
        }
        if obj.get("id") != Some(&json!(id)) {
            return Err(FoundgineError::other("The server returned a JSON-RPC response with an unexpected id"));
        }
        Ok(obj.get("result").cloned())
    }

    /// The GET that opens the server-to-client notification stream (sends `Last-Event-ID` to resume).
    pub fn stream_request(&self, token: Option<&str>) -> HttpRequest {
        let mut headers = self.headers(self.session.as_deref(), false, ACCEPT_STREAM, token);
        if let Some(id) = &self.last_event_id {
            headers.push(("Last-Event-ID".to_string(), id.clone()));
        }
        HttpRequest { method: "GET", url: self.url.clone(), headers, body: None }
    }

    /// Check the status of the stream GET: 404 clears the session and is `SessionExpired`; 410 is
    /// `ResyncRequired`; 400, 403, 409 and others are plain HTTP errors that keep the session.
    pub fn check_stream_status(&mut self, resp: &HttpResponse) -> Result<(), FoundgineError> {
        if resp.ok() {
            return Ok(());
        }
        let error = error_for(resp.status, &resp.body);
        let (message, body) = match error.clone() {
            FoundgineError::Http { message, body, .. } => (message, body),
            other => return Err(other),
        };
        match resp.status {
            404 => {
                let session = self.session.clone();
                self.expire(session.as_deref());
                Err(FoundgineError::session_expired(Some(message), body))
            }
            410 => Err(FoundgineError::resync_required(Some(message), body)),
            _ => Err(error),
        }
    }

    /// Handle one stream event: undecodable JSON is skipped without advancing the resume id; otherwise
    /// the id is recorded and a JSON object message is returned (non-object messages are dropped).
    pub fn record_stream_event(&mut self, event: &SseEvent) -> Option<Map<String, Value>> {
        let message: Value = serde_json::from_str(&event.data).ok()?;
        if let Some(id) = &event.id {
            self.last_event_id = Some(id.clone());
        }
        match message {
            Value::Object(m) => Some(m),
            _ => None,
        }
    }

    /// Session teardown. Clears the session first (idempotent); `None` when there was no session.
    /// The host sends the DELETE and ignores any failure.
    pub fn close_request(&mut self, token: Option<&str>) -> Option<HttpRequest> {
        let session = self.session.take();
        self.ready = false;
        let session = session?;
        Some(HttpRequest { method: "DELETE", url: self.url.clone(), headers: self.headers(Some(&session), false, ACCEPT_BOTH, token), body: None })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn resp(status: u16, headers: &[(&str, &str)], body: &str) -> HttpResponse {
        HttpResponse { status, headers: headers.iter().map(|(k, v)| (k.to_string(), v.to_string())).collect(), body: body.to_string() }
    }

    fn header<'a>(r: &'a HttpRequest, name: &str) -> Option<&'a str> {
        r.headers.iter().find(|(k, _)| k.eq_ignore_ascii_case(name)).map(|(_, v)| v.as_str())
    }

    fn ready(token: Option<&str>) -> McpProtocol {
        let mut p = McpProtocol::new("https://h/", None, None, None);
        let (_, id) = p.initialize_request(token);
        let r = resp(200, &[(SESSION_HEADER, "s1")], &format!(r#"{{"jsonrpc":"2.0","id":{id},"result":{{"protocolVersion":"2025-06-18"}}}}"#));
        p.complete_initialize(id, &r, token).unwrap();
        p
    }

    #[test]
    fn initialize_flow() {
        let mut p = McpProtocol::new("https://h/", None, None, None);
        assert_eq!(p.url(), "https://h/mcp");
        let (req, id) = p.initialize_request(Some("tok"));
        assert_eq!((req.method, id), ("POST", 1));
        assert_eq!(header(&req, "authorization"), Some("Bearer tok"));
        assert_eq!(header(&req, SESSION_HEADER), None);
        assert_eq!(header(&req, PROTOCOL_HEADER), None);
        let ok = resp(200, &[("mcp-session-id", "s1")], r#"{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-03-26"}}"#);
        let initialized = p.complete_initialize(id, &ok, Some("tok")).unwrap();
        assert!(p.is_ready());
        assert_eq!(p.session(), Some("s1"));
        assert_eq!(header(&initialized, SESSION_HEADER), Some("s1"));
        assert_eq!(header(&initialized, PROTOCOL_HEADER), Some("2025-03-26"));
        assert!(initialized.body.unwrap().contains("notifications/initialized"));
    }

    #[test]
    fn initialize_rejects_wrong_id_and_http_errors() {
        let mut p = McpProtocol::new("https://h", None, None, None);
        let (_, id) = p.initialize_request(None);
        assert!(p.complete_initialize(id, &resp(200, &[], r#"{"id":99,"result":{}}"#), None).is_err());
        let e = p.complete_initialize(id, &resp(401, &[], ""), None).unwrap_err();
        assert_eq!(e.status(), Some(401));
        assert!(!p.is_ready());
    }

    #[test]
    fn request_ids_increase_and_params_are_optional() {
        let mut p = ready(None);
        let (a, ida) = p.request_message("tools/list", None, None);
        let (_, idb) = p.request_message("tools/call", Some(json!({"name": "x"})), None);
        assert_eq!((ida, idb), (2, 3));
        assert!(!a.body.unwrap().contains("params"));
    }

    #[test]
    fn plain_json_result() {
        let mut p = ready(None);
        let (_, id) = p.request_message("tools/list", None, None);
        let r = resp(200, &[("content-type", "application/json; charset=utf-8")], &format!(r#"{{"jsonrpc":"2.0","id":{id},"result":{{"tools":[]}}}}"#));
        assert_eq!(p.parse_response(id, &r).unwrap(), Some(json!({"tools": []})));
    }

    #[test]
    fn event_stream_body_uses_first_data_event() {
        let mut p = ready(None);
        let (_, id) = p.request_message("x", None, None);
        let body = format!(": hb\n\nid: 1\ndata: {{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":7}}\n\n");
        let r = resp(200, &[("Content-Type", "Text/Event-Stream")], &body);
        assert_eq!(p.parse_response(id, &r).unwrap(), Some(json!(7)));
    }

    #[test]
    fn accepted_and_no_content_are_empty() {
        let mut p = ready(None);
        let (_, id) = p.request_message("x", None, None);
        assert_eq!(p.parse_response(id, &resp(202, &[], "")).unwrap(), None);
        assert_eq!(p.parse_response(id, &resp(204, &[], "")).unwrap(), None);
    }

    #[test]
    fn rpc_error_wrong_id_and_bad_bodies() {
        let mut p = ready(None);
        let (_, id) = p.request_message("x", None, None);
        let e = p.parse_response(id, &resp(200, &[], r#"{"id":2,"error":{"code":-32601,"message":"nope","data":1}}"#)).unwrap_err();
        assert_eq!(e, FoundgineError::rpc(-32601, "nope", Some(json!(1))));
        let e = p.parse_response(id, &resp(200, &[], r#"{"id":2,"error":{}}"#)).unwrap_err();
        assert_eq!(e, FoundgineError::rpc(-32603, "", None));
        assert!(matches!(p.parse_response(id, &resp(200, &[], r#"{"id":77,"result":1}"#)), Err(FoundgineError::Other { .. })));
        assert!(matches!(p.parse_response(id, &resp(200, &[], "not json")), Err(FoundgineError::Other { .. })));
        assert!(matches!(p.parse_response(id, &resp(200, &[], "[1]")), Err(FoundgineError::Other { .. })));
        let empty_stream = resp(200, &[("content-type", "text/event-stream")], ": hb\n\n");
        assert!(matches!(p.parse_response(id, &empty_stream), Err(FoundgineError::Other { .. })));
    }

    #[test]
    fn http_404_with_session_expires_it_and_next_call_must_reinitialize() {
        let mut p = ready(None);
        let (_, id) = p.request_message("x", None, None);
        let e = p.parse_response(id, &resp(404, &[], r#"{"error":"gone"}"#)).unwrap_err();
        assert!(matches!(e, FoundgineError::SessionExpired { .. }));
        assert_eq!(e.message(), "HTTP 404: gone");
        assert!(!p.is_ready());
        assert_eq!(p.session(), None);
    }

    #[test]
    fn http_404_without_session_stays_a_plain_http_error() {
        let mut p = McpProtocol::new("https://h", None, None, None);
        let e = p.parse_response(1, &resp(404, &[], "")).unwrap_err();
        assert!(matches!(e, FoundgineError::Http { status: 404, .. }));
    }

    #[test]
    fn stream_gate_statuses() {
        for status in [400u16, 403, 409] {
            let mut p = ready(None);
            let e = p.check_stream_status(&resp(status, &[], "")).unwrap_err();
            assert!(matches!(e, FoundgineError::Http { .. }) && e.status() == Some(status));
            assert_eq!(p.session(), Some("s1"), "session kept on {status}");
        }
        let mut p = ready(None);
        assert!(matches!(p.check_stream_status(&resp(404, &[], "")), Err(FoundgineError::SessionExpired { .. })));
        assert_eq!(p.session(), None);
        let mut p = ready(None);
        assert!(matches!(p.check_stream_status(&resp(410, &[], "")), Err(FoundgineError::ResyncRequired { .. })));
        assert!(p.check_stream_status(&resp(200, &[], "")).is_ok());
    }

    #[test]
    fn stream_request_resumes_with_last_event_id() {
        let mut p = ready(Some("t"));
        assert_eq!(header(&p.stream_request(Some("t")), "Last-Event-ID"), None);
        let ev = SseEvent { id: Some("9".into()), event: None, data: r#"{"method":"n"}"#.into() };
        assert!(p.record_stream_event(&ev).is_some());
        let r = p.stream_request(Some("t"));
        assert_eq!((r.method, header(&r, "last-event-id"), header(&r, "accept")), ("GET", Some("9"), Some("text/event-stream")));
        assert_eq!(header(&r, SESSION_HEADER), Some("s1"));
    }

    #[test]
    fn undecodable_and_non_object_stream_events() {
        let mut p = ready(None);
        let bad = SseEvent { id: Some("5".into()), event: None, data: "{oops".into() };
        assert!(p.record_stream_event(&bad).is_none());
        assert_eq!(p.last_event_id(), None, "bad JSON must not advance the resume id");
        let arr = SseEvent { id: Some("6".into()), event: None, data: "[1]".into() };
        assert!(p.record_stream_event(&arr).is_none());
        assert_eq!(p.last_event_id(), Some("6"));
    }

    #[test]
    fn close_is_idempotent_and_omits_the_protocol_header() {
        let mut p = ready(Some("t"));
        let del = p.close_request(Some("t")).unwrap();
        assert_eq!((del.method, header(&del, SESSION_HEADER), header(&del, PROTOCOL_HEADER)), ("DELETE", Some("s1"), None));
        assert!(p.close_request(Some("t")).is_none());
        assert!(!p.is_ready());
    }

    #[test]
    fn cancel_notification_shape() {
        let p = ready(None);
        let req = p.cancel_request(4, None);
        let v: Value = serde_json::from_str(&req.body.unwrap()).unwrap();
        assert_eq!(v["method"], "notifications/cancelled");
        assert_eq!(v["params"], json!({"requestId": 4, "reason": "cancelled by client"}));
        assert!(v.get("id").is_none());
    }
}
