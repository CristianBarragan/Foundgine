//! wasm-bindgen layer (wasm32 only). A thin, mechanical shell around the sans-IO core: no logic lives here.
//!
//! Boundary rules, so the JS wrapper (`js/`) stays simple:
//! - Requests and responses cross as JSON strings. `HttpRequest` is `{method, url, headers: [[k, v]], body}`
//!   and `HttpResponse` is `{status, headers: [[k, v]], body}`.
//! - Every failure is thrown as a JSON string `{name, message, status?, body?, code?, data?, lastStatus?}`
//!   (see `FoundgineError::name`). The JS wrapper turns it into the matching `Error` subclass.
//! - Times are `f64` milliseconds (JS numbers), never `u64`, so nothing becomes a `BigInt`.
//! - Token resolution, `fetch`, sleeping, `AbortSignal` and reading stream bodies stay in JS.

use crate::client;
use crate::errors::FoundgineError;
use crate::http;
use crate::mcp::{HttpRequest, HttpResponse, McpProtocol};
use crate::sse::{SseEvent, SseParser};
use crate::workflows::{self, PollStep, WaitPoller, WatchOptions, WatchPoller, WorkflowRoutes, WorkflowStatus};
use serde_json::{json, Map, Value};
use wasm_bindgen::prelude::*;

// ---------------------------------------------------------------------------------------------
// JSON <-> core types
// ---------------------------------------------------------------------------------------------

fn error_value(e: &FoundgineError) -> Value {
    let mut m = Map::new();
    m.insert("name".into(), json!(e.name()));
    m.insert("message".into(), json!(e.message()));
    match e {
        FoundgineError::Http { status, body, .. } => {
            m.insert("status".into(), json!(status));
            m.insert("body".into(), json!(body));
        }
        FoundgineError::SessionExpired { body, .. } => {
            m.insert("status".into(), json!(404));
            m.insert("body".into(), json!(body));
        }
        FoundgineError::ResyncRequired { body, .. } => {
            m.insert("status".into(), json!(410));
            m.insert("body".into(), json!(body));
        }
        FoundgineError::Rpc { code, data, message } => {
            m.insert("code".into(), json!(code));
            m.insert("data".into(), json!(data));
            // `message` above is already the prefixed form; keep the bare server text too.
            m.insert("rpcMessage".into(), json!(message));
        }
        FoundgineError::WatchTimeout { last_status, .. } => {
            m.insert("lastStatus".into(), json!(last_status));
        }
        FoundgineError::Abort { .. } | FoundgineError::Other { .. } => {}
    }
    Value::Object(m)
}

fn throw(e: &FoundgineError) -> JsValue {
    JsValue::from_str(&error_value(e).to_string())
}

fn invalid(message: &str) -> JsValue {
    throw(&FoundgineError::other(message))
}

/// Rebuild the error the host caught, so the pollers can look at its status.
fn error_from_json(text: &str) -> Result<FoundgineError, JsValue> {
    let v: Value = serde_json::from_str(text).map_err(|_| invalid("error JSON is invalid"))?;
    let message = v.get("message").and_then(Value::as_str).map(String::from);
    let body = v.get("body").filter(|b| !b.is_null()).cloned();
    let status = v.get("status").and_then(Value::as_u64).map(|s| s as u16);
    Ok(match v.get("name").and_then(Value::as_str) {
        Some("FoundgineHttpError") => FoundgineError::http(status.unwrap_or(500), message, body),
        Some("FoundgineSessionExpiredError") => FoundgineError::session_expired(message, body),
        Some("FoundgineResyncRequiredError") => FoundgineError::resync_required(message, body),
        Some("FoundgineAbortError") => FoundgineError::abort(message),
        _ => FoundgineError::other(message.unwrap_or_else(|| "error".to_string())),
    })
}

fn parse_value(text: &str, what: &str) -> Result<Value, JsValue> {
    serde_json::from_str(text).map_err(|_| invalid(&format!("{what} is not valid JSON")))
}

fn parse_object(text: &str, what: &str) -> Result<Map<String, Value>, JsValue> {
    match parse_value(text, what)? {
        Value::Object(m) => Ok(m),
        _ => Err(invalid(&format!("{what} must be a JSON object"))),
    }
}

fn optional_value(text: Option<String>, what: &str) -> Result<Option<Value>, JsValue> {
    text.map(|t| parse_value(&t, what)).transpose()
}

fn request_value(r: &HttpRequest) -> Value {
    json!({
        "method": r.method,
        "url": r.url,
        "headers": r.headers.iter().map(|(k, v)| json!([k, v])).collect::<Vec<_>>(),
        "body": r.body,
    })
}

fn request_json(r: &HttpRequest) -> String {
    request_value(r).to_string()
}

fn response_from_json(text: &str) -> Result<HttpResponse, JsValue> {
    let v = parse_object(text, "response")?;
    let status = v.get("status").and_then(Value::as_u64).filter(|s| *s <= u16::MAX as u64).ok_or_else(|| invalid("response.status must be an HTTP status number"))? as u16;
    let mut headers = Vec::new();
    if let Some(Value::Array(pairs)) = v.get("headers") {
        for pair in pairs {
            if let (Some(k), Some(val)) = (pair.get(0).and_then(Value::as_str), pair.get(1).and_then(Value::as_str)) {
                headers.push((k.to_string(), val.to_string()));
            }
        }
    }
    let body = v.get("body").and_then(Value::as_str).unwrap_or("").to_string();
    Ok(HttpResponse { status, headers, body })
}

fn tool_value(t: &client::Tool) -> Value {
    json!({ "name": t.name, "description": t.description, "inputSchema": t.input_schema, "raw": t.raw })
}

fn tool_result_value(r: &client::ToolResult) -> Value {
    let mut m = Map::new();
    m.insert("content".into(), json!(r.content));
    if let Some(s) = &r.structured_content {
        m.insert("structuredContent".into(), s.clone());
    }
    m.insert("isError".into(), json!(r.is_error));
    m.insert("raw".into(), json!(r.raw));
    Value::Object(m)
}

fn event_value(e: &SseEvent) -> Value {
    json!({ "id": e.id, "event": e.event, "data": e.data })
}

fn step_value(step: &PollStep) -> Value {
    match step {
        PollStep::Finished => json!({ "kind": "finished" }),
        PollStep::Sleep(ms) => json!({ "kind": "sleep", "ms": *ms as f64 }),
        PollStep::Fail(e) => json!({ "kind": "fail", "error": error_value(e) }),
    }
}

fn ms(value: f64) -> u64 {
    if value.is_finite() && value > 0.0 {
        value as u64
    } else {
        0
    }
}

fn status_from_json(text: &str) -> Result<WorkflowStatus, JsValue> {
    let raw = parse_object(text, "status")?;
    workflows::parse_status(raw).map_err(|e| throw(&e))
}

// ---------------------------------------------------------------------------------------------
// Free functions
// ---------------------------------------------------------------------------------------------

/// `baseUrl must start with http:// or https://` is thrown as a plain `FoundgineError`.
#[wasm_bindgen(js_name = normalizeBaseUrl)]
pub fn normalize_base_url(base_url: &str) -> Result<String, JsValue> {
    client::normalize_base_url(base_url).map_err(|m| invalid(&m))
}

#[wasm_bindgen(js_name = joinUrl)]
pub fn join_url(base_url: &str, path: &str) -> String {
    http::join_url(base_url, path)
}

#[wasm_bindgen(js_name = encodePathSegment)]
pub fn encode_path_segment(segment: &str) -> String {
    http::encode_path_segment(segment)
}

/// A blank token means "no token": `undefined` is returned.
#[wasm_bindgen(js_name = normalizeToken)]
pub fn normalize_token(token: Option<String>) -> Option<String> {
    http::normalize_token(token.as_deref())
}

/// The error for a non-success response, as thrown JSON. Used for `fetch` failures the core never sees.
#[wasm_bindgen(js_name = errorFor)]
pub fn error_for(status: u16, text: &str) -> String {
    error_value(&http::error_for(status, text)).to_string()
}

#[wasm_bindgen(js_name = isTerminalStatus)]
pub fn is_terminal_status(temporal_status: &str) -> bool {
    workflows::is_terminal_status(temporal_status)
}

#[wasm_bindgen(js_name = listToolsParams)]
pub fn list_tools_params(cursor: Option<String>) -> String {
    client::list_tools_params(cursor.as_deref()).to_string()
}

#[wasm_bindgen(js_name = callToolParams)]
pub fn call_tool_params(name: &str, arguments_json: Option<String>) -> Result<String, JsValue> {
    let arguments = optional_value(arguments_json, "arguments")?;
    Ok(client::call_tool_params(name, arguments).to_string())
}

/// `{ tools: Tool[], nextCursor?: string }`.
#[wasm_bindgen(js_name = parseToolsPage)]
pub fn parse_tools_page(result_json: Option<String>) -> Result<String, JsValue> {
    let result = optional_value(result_json, "tools/list result")?;
    let (tools, next) = client::parse_tools_page(result.as_ref()).map_err(|e| throw(&e))?;
    let mut out = Map::new();
    out.insert("tools".into(), Value::Array(tools.iter().map(tool_value).collect()));
    if let Some(cursor) = next {
        out.insert("nextCursor".into(), json!(cursor));
    }
    Ok(Value::Object(out).to_string())
}

#[wasm_bindgen(js_name = parseToolResult)]
pub fn parse_tool_result(result_json: Option<String>) -> Result<String, JsValue> {
    let result = optional_value(result_json, "tools/call result")?;
    let parsed = client::parse_tool_result(result.as_ref()).map_err(|e| throw(&e))?;
    Ok(tool_result_value(&parsed).to_string())
}

#[wasm_bindgen(js_name = parseObjectResponse)]
pub fn parse_object_response(response_json: &str) -> Result<String, JsValue> {
    let resp = response_from_json(response_json)?;
    let raw = workflows::parse_object_response(&resp).map_err(|e| throw(&e))?;
    Ok(Value::Object(raw).to_string())
}

#[wasm_bindgen(js_name = parseEmptyResponse)]
pub fn parse_empty_response(response_json: &str) -> Result<(), JsValue> {
    let resp = response_from_json(response_json)?;
    workflows::parse_empty_response(&resp).map_err(|e| throw(&e))
}

#[wasm_bindgen(js_name = parseProcesses)]
pub fn parse_processes(raw_json: &str) -> Result<String, JsValue> {
    let raw = parse_object(raw_json, "catalog")?;
    let names = workflows::parse_processes(&raw).map_err(|e| throw(&e))?;
    Ok(json!(names).to_string())
}

#[wasm_bindgen(js_name = parseDescription)]
pub fn parse_description(raw_json: &str) -> Result<String, JsValue> {
    let raw = parse_object(raw_json, "description")?;
    let d = workflows::parse_description(&raw).map_err(|e| throw(&e))?;
    let steps: Vec<Value> = d
        .steps
        .iter()
        .map(|s| {
            json!({
                "name": s.name,
                "requiresApproval": s.requires_approval,
                "hasCompensation": s.has_compensation,
                "approvalTimeoutSeconds": s.approval_timeout_seconds,
            })
        })
        .collect();
    Ok(json!({ "name": d.name, "steps": steps }).to_string())
}

#[wasm_bindgen(js_name = parseStart)]
pub fn parse_start(raw_json: &str) -> Result<String, JsValue> {
    let raw = parse_object(raw_json, "start response")?;
    let r = workflows::parse_start(raw).map_err(|e| throw(&e))?;
    Ok(json!({ "workflowId": r.workflow_id, "status": r.status, "alreadyExisted": r.already_existed, "raw": r.raw }).to_string())
}

#[wasm_bindgen(js_name = parseStatus)]
pub fn parse_status(raw_json: &str) -> Result<String, JsValue> {
    let s = status_from_json(raw_json)?;
    let mut m = Map::new();
    m.insert("workflowId".into(), json!(s.workflow_id));
    m.insert("temporalStatus".into(), json!(s.temporal_status));
    if let Some(p) = &s.process {
        m.insert("process".into(), p.clone());
    }
    m.insert("raw".into(), json!(s.raw));
    m.insert("isFinished".into(), json!(s.is_finished()));
    Ok(Value::Object(m).to_string())
}

// ---------------------------------------------------------------------------------------------
// SSE
// ---------------------------------------------------------------------------------------------

#[wasm_bindgen]
pub struct SseCore {
    inner: SseParser,
}

#[wasm_bindgen]
impl SseCore {
    #[wasm_bindgen(constructor)]
    pub fn new() -> SseCore {
        SseCore { inner: SseParser::new() }
    }

    /// Feed raw bytes; returns a JSON array of `{id, event, data}`. Invalid UTF-8 is thrown.
    #[wasm_bindgen(js_name = pushBytes)]
    pub fn push_bytes(&mut self, chunk: &[u8]) -> Result<String, JsValue> {
        let events = self.inner.push_bytes(chunk).map_err(|e| throw(&FoundgineError::other(e.to_string())))?;
        Ok(Value::Array(events.iter().map(event_value).collect()).to_string())
    }

    #[wasm_bindgen(js_name = pushStr)]
    pub fn push_str(&mut self, chunk: &str) -> String {
        let events = self.inner.push_str(chunk);
        Value::Array(events.iter().map(event_value).collect()).to_string()
    }

    #[wasm_bindgen(getter, js_name = lastEventId)]
    pub fn last_event_id(&self) -> Option<String> {
        self.inner.last_event_id.clone()
    }

    /// The last valid `retry:` value in milliseconds.
    #[wasm_bindgen(getter, js_name = retryMs)]
    pub fn retry_ms(&self) -> Option<f64> {
        self.inner.retry_ms.map(|v| v as f64)
    }
}

impl Default for SseCore {
    fn default() -> Self {
        Self::new()
    }
}

// ---------------------------------------------------------------------------------------------
// MCP session
// ---------------------------------------------------------------------------------------------

#[wasm_bindgen]
pub struct McpCore {
    inner: McpProtocol,
}

#[wasm_bindgen]
impl McpCore {
    #[wasm_bindgen(constructor)]
    pub fn new(base_url: &str, path: Option<String>, protocol_version: Option<String>, user_agent: Option<String>) -> McpCore {
        McpCore { inner: McpProtocol::new(base_url, path.as_deref(), protocol_version.as_deref(), user_agent.as_deref()) }
    }

    #[wasm_bindgen(getter)]
    pub fn url(&self) -> String {
        self.inner.url().to_string()
    }

    #[wasm_bindgen(getter)]
    pub fn session(&self) -> Option<String> {
        self.inner.session().map(String::from)
    }

    #[wasm_bindgen(getter, js_name = isReady)]
    pub fn is_ready(&self) -> bool {
        self.inner.is_ready()
    }

    #[wasm_bindgen(getter, js_name = lastEventId)]
    pub fn last_event_id(&self) -> Option<String> {
        self.inner.last_event_id().map(String::from)
    }

    #[wasm_bindgen(setter, js_name = lastEventId)]
    pub fn set_last_event_id(&mut self, id: Option<String>) {
        self.inner.set_last_event_id(id);
    }

    /// `{ request, id }`.
    #[wasm_bindgen(js_name = initializeRequest)]
    pub fn initialize_request(&mut self, token: Option<String>) -> String {
        let (request, id) = self.inner.initialize_request(token.as_deref());
        json!({ "request": request_value(&request), "id": id as f64 }).to_string()
    }

    /// Returns the `notifications/initialized` request to send next.
    #[wasm_bindgen(js_name = completeInitialize)]
    pub fn complete_initialize(&mut self, id: f64, response_json: &str, token: Option<String>) -> Result<String, JsValue> {
        let resp = response_from_json(response_json)?;
        let next = self.inner.complete_initialize(ms(id), &resp, token.as_deref()).map_err(|e| throw(&e))?;
        Ok(request_json(&next))
    }

    /// `{ request, id }`. `params_json` is optional.
    #[wasm_bindgen(js_name = requestMessage)]
    pub fn request_message(&mut self, method: &str, params_json: Option<String>, token: Option<String>) -> Result<String, JsValue> {
        let params = optional_value(params_json, "params")?;
        let (request, id) = self.inner.request_message(method, params, token.as_deref());
        Ok(json!({ "request": request_value(&request), "id": id as f64 }).to_string())
    }

    #[wasm_bindgen(js_name = notifyMessage)]
    pub fn notify_message(&self, method: &str, params_json: Option<String>, token: Option<String>) -> Result<String, JsValue> {
        let params = optional_value(params_json, "params")?;
        Ok(request_json(&self.inner.notify_message(method, params, token.as_deref())))
    }

    #[wasm_bindgen(js_name = cancelRequest)]
    pub fn cancel_request(&self, id: f64, token: Option<String>) -> String {
        request_json(&self.inner.cancel_request(ms(id), token.as_deref()))
    }

    #[wasm_bindgen(js_name = checkNotificationResponse)]
    pub fn check_notification_response(&self, response_json: &str) -> Result<(), JsValue> {
        let resp = response_from_json(response_json)?;
        self.inner.check_notification_response(&resp).map_err(|e| throw(&e))
    }

    /// The JSON-encoded `result`, or `undefined` for 202/204 and a result-less message.
    #[wasm_bindgen(js_name = parseResponse)]
    pub fn parse_response(&mut self, id: f64, response_json: &str) -> Result<Option<String>, JsValue> {
        let resp = response_from_json(response_json)?;
        let result = self.inner.parse_response(ms(id), &resp).map_err(|e| throw(&e))?;
        Ok(result.map(|v| v.to_string()))
    }

    #[wasm_bindgen(js_name = streamRequest)]
    pub fn stream_request(&self, token: Option<String>) -> String {
        request_json(&self.inner.stream_request(token.as_deref()))
    }

    #[wasm_bindgen(js_name = checkStreamStatus)]
    pub fn check_stream_status(&mut self, response_json: &str) -> Result<(), JsValue> {
        let resp = response_from_json(response_json)?;
        self.inner.check_stream_status(&resp).map_err(|e| throw(&e))
    }

    /// The decoded JSON-RPC message of an SSE event (as JSON), or `undefined` when it is skipped.
    #[wasm_bindgen(js_name = recordStreamEvent)]
    pub fn record_stream_event(&mut self, id: Option<String>, event: Option<String>, data: String) -> Option<String> {
        let ev = SseEvent { id, event, data };
        self.inner.record_stream_event(&ev).map(|m| Value::Object(m).to_string())
    }

    /// The DELETE to send, or `undefined` when there is no session. Idempotent.
    #[wasm_bindgen(js_name = closeRequest)]
    pub fn close_request(&mut self, token: Option<String>) -> Option<String> {
        self.inner.close_request(token.as_deref()).map(|r| request_json(&r))
    }
}

// ---------------------------------------------------------------------------------------------
// Workflows
// ---------------------------------------------------------------------------------------------

#[wasm_bindgen]
pub struct WorkflowRoutesCore {
    inner: WorkflowRoutes,
}

#[wasm_bindgen]
impl WorkflowRoutesCore {
    #[wasm_bindgen(constructor)]
    pub fn new(base_url: &str) -> WorkflowRoutesCore {
        WorkflowRoutesCore { inner: WorkflowRoutes::new(base_url) }
    }

    #[wasm_bindgen(js_name = processesRequest)]
    pub fn processes_request(&self, token: Option<String>) -> String {
        request_json(&self.inner.processes_request(token.as_deref()))
    }

    #[wasm_bindgen(js_name = describeRequest)]
    pub fn describe_request(&self, process: &str, token: Option<String>) -> String {
        request_json(&self.inner.describe_request(process, token.as_deref()))
    }

    #[wasm_bindgen(js_name = startRequest)]
    pub fn start_request(&self, process: &str, business_key: &str, payload_json: Option<String>, token: Option<String>) -> Result<String, JsValue> {
        let payload = optional_value(payload_json, "payload")?;
        Ok(request_json(&self.inner.start_request(process, business_key, payload, token.as_deref())))
    }

    #[wasm_bindgen(js_name = statusRequest)]
    pub fn status_request(&self, process: &str, business_key: &str, token: Option<String>) -> String {
        request_json(&self.inner.status_request(process, business_key, token.as_deref()))
    }

    #[wasm_bindgen(js_name = decisionRequest)]
    pub fn decision_request(&self, process: &str, business_key: &str, step: &str, approved: bool, reason: Option<String>, token: Option<String>) -> String {
        request_json(&self.inner.decision_request(process, business_key, step, approved, reason.as_deref(), token.as_deref()))
    }

    #[wasm_bindgen(js_name = cancelRequest)]
    pub fn cancel_request(&self, process: &str, business_key: &str, token: Option<String>) -> String {
        request_json(&self.inner.cancel_request(process, business_key, token.as_deref()))
    }
}

#[wasm_bindgen]
pub struct WatchPollerCore {
    inner: WatchPoller,
}

#[wasm_bindgen]
impl WatchPollerCore {
    /// Pass `undefined` for an option to take the default (1000 ms interval, no timeout, 5000 ms 404 grace, 5 errors).
    #[wasm_bindgen(constructor)]
    pub fn new(interval_ms: Option<f64>, timeout_ms: Option<f64>, not_found_grace_ms: Option<f64>, max_consecutive_errors: Option<u32>, now_ms: f64) -> WatchPollerCore {
        let d = WatchOptions::default();
        let options = WatchOptions {
            interval_ms: interval_ms.map(ms).unwrap_or(d.interval_ms),
            timeout_ms: timeout_ms.map(ms),
            not_found_grace_ms: not_found_grace_ms.map(ms).unwrap_or(d.not_found_grace_ms),
            max_consecutive_errors: max_consecutive_errors.unwrap_or(d.max_consecutive_errors),
        };
        WatchPollerCore { inner: WatchPoller::new(options, ms(now_ms)) }
    }

    /// `{ isNew, step }`. Throws when the body is not a valid status; the host then calls `observeError`
    /// with the thrown error, because a parse failure counts like any other poll failure.
    #[wasm_bindgen(js_name = observeStatus)]
    pub fn observe_status(&mut self, raw_json: &str, now_ms: f64) -> Result<String, JsValue> {
        let status = status_from_json(raw_json)?;
        let o = self.inner.observe_status(&status, ms(now_ms));
        Ok(json!({ "isNew": o.is_new, "step": step_value(&o.step) }).to_string())
    }

    /// `{ notifyError, step }` where `notifyError` is the consecutive-error count for `on_error` (absent when none).
    #[wasm_bindgen(js_name = observeError)]
    pub fn observe_error(&mut self, error_json: &str, now_ms: f64) -> Result<String, JsValue> {
        let error = error_from_json(error_json)?;
        let o = self.inner.observe_error(error, ms(now_ms));
        let mut m = Map::new();
        if let Some(n) = o.notify_error {
            m.insert("notifyError".into(), json!(n));
        }
        m.insert("step".into(), step_value(&o.step));
        Ok(Value::Object(m).to_string())
    }
}

#[wasm_bindgen]
pub struct WaitPollerCore {
    inner: WaitPoller,
}

#[wasm_bindgen]
impl WaitPollerCore {
    #[wasm_bindgen(constructor)]
    pub fn new(interval_ms: Option<f64>, timeout_ms: Option<f64>, now_ms: f64) -> WaitPollerCore {
        WaitPollerCore { inner: WaitPoller::new(interval_ms.map(ms), timeout_ms.map(ms), ms(now_ms)) }
    }

    /// The next step as JSON: `{kind: "finished"}`, `{kind: "sleep", ms}` or `{kind: "fail", error}`.
    #[wasm_bindgen]
    pub fn observe(&self, raw_json: &str, now_ms: f64) -> Result<String, JsValue> {
        let status = status_from_json(raw_json)?;
        Ok(step_value(&self.inner.observe(&status, ms(now_ms))).to_string())
    }
}
