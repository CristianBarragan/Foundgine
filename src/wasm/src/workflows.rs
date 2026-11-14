//! Sans-IO Temporal workflow client. Mirrors `workflows.ts` / `workflows.py` (`WorkflowClient`).
//!
//! `WorkflowRoutes` builds the HTTP requests and the `parse_*` functions interpret responses; the host
//! sends them with its own `fetch`. `WatchPoller` and `WaitPoller` are the polling decision logic with an
//! injected clock (`now_ms`), so they are deterministic and testable. Sleeping, the `updates()` replay
//! history, `stop()` and callbacks belong to the host (the JS binding layer).
//!
//! `decide` has no caller-identity argument: the server stamps the decision subject from its trusted
//! security context, so the SDK offers no way to send one.

use crate::errors::FoundgineError;
use crate::http::{bearer_header, encode_path_segment, error_for, join_url};
use crate::mcp::{HttpRequest, HttpResponse};
use serde_json::{json, Map, Value};

const ROUTE_PREFIX: &str = "/foundgine/workflows";
pub const DEFAULT_INTERVAL_MS: u64 = 1000;
pub const DEFAULT_NOT_FOUND_GRACE_MS: u64 = 5000;
pub const DEFAULT_MAX_CONSECUTIVE_ERRORS: u32 = 5;

/// Temporal statuses after which polling stops. Anything else, including unknown values, is not terminal.
const TERMINAL: [&str; 6] = ["COMPLETED", "FAILED", "CANCELED", "CANCELLED", "TERMINATED", "TIMED_OUT"];

/// `isFinished`: case-insensitive membership in the terminal set. There is no result-based rule.
pub fn is_terminal_status(temporal_status: &str) -> bool {
    let upper = temporal_status.to_uppercase();
    TERMINAL.contains(&upper.as_str())
}

#[derive(Debug, Clone, PartialEq)]
pub struct WorkflowStartResult {
    pub workflow_id: String,
    pub status: String,
    pub already_existed: bool,
    pub raw: Map<String, Value>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct WorkflowStatus {
    pub workflow_id: String,
    pub temporal_status: String,
    /// Passed through untouched; `None` when the server omitted it.
    pub process: Option<Value>,
    pub raw: Map<String, Value>,
}

impl WorkflowStatus {
    pub fn is_finished(&self) -> bool {
        is_terminal_status(&self.temporal_status)
    }

    /// JSON form used as `last_status` of a `WatchTimeout` error.
    pub fn to_value(&self) -> Value {
        json!({
            "workflowId": self.workflow_id,
            "temporalStatus": self.temporal_status,
            "process": self.process,
            "raw": self.raw,
        })
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct WorkflowStepDescription {
    pub name: String,
    pub requires_approval: bool,
    pub has_compensation: bool,
    pub approval_timeout_seconds: Option<f64>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct WorkflowProcessDescription {
    pub name: String,
    pub steps: Vec<WorkflowStepDescription>,
}

// ---------------------------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------------------------

#[derive(Debug, Clone)]
pub struct WorkflowRoutes {
    base_url: String,
}

impl WorkflowRoutes {
    /// `base_url` is the already-normalized client base URL (see `client::normalize_base_url`).
    pub fn new(base_url: &str) -> Self {
        WorkflowRoutes { base_url: base_url.to_string() }
    }

    fn build(&self, method: &'static str, path: &str, body: Option<Value>, token: Option<&str>) -> HttpRequest {
        let mut headers = vec![("Accept".to_string(), "application/json".to_string())];
        if let Some(auth) = bearer_header(token) {
            headers.push(("Authorization".to_string(), auth));
        }
        if body.is_some() {
            headers.push(("Content-Type".to_string(), "application/json".to_string()));
        }
        HttpRequest { method, url: join_url(&self.base_url, &format!("{ROUTE_PREFIX}{path}")), headers, body: body.map(|b| b.to_string()) }
    }

    fn process_path(process: &str) -> String {
        format!("/{}", encode_path_segment(process))
    }

    fn key_path(process: &str, key: &str) -> String {
        format!("/{}/{}", encode_path_segment(process), encode_path_segment(key))
    }

    pub fn processes_request(&self, token: Option<&str>) -> HttpRequest {
        self.build("GET", "", None, token)
    }

    pub fn describe_request(&self, process: &str, token: Option<&str>) -> HttpRequest {
        self.build("GET", &Self::process_path(process), None, token)
    }

    /// `payload` defaults to `{}`.
    pub fn start_request(&self, process: &str, business_key: &str, payload: Option<Value>, token: Option<&str>) -> HttpRequest {
        let body = json!({ "businessKey": business_key, "payload": payload.unwrap_or_else(|| json!({})) });
        self.build("POST", &Self::process_path(process), Some(body), token)
    }

    pub fn status_request(&self, process: &str, business_key: &str, token: Option<&str>) -> HttpRequest {
        self.build("GET", &Self::key_path(process, business_key), None, token)
    }

    /// Body is exactly `{ "step", "approved" }` plus `"reason"` only when given. Never an identity field.
    pub fn decision_request(&self, process: &str, business_key: &str, step: &str, approved: bool, reason: Option<&str>, token: Option<&str>) -> HttpRequest {
        let mut body = Map::new();
        body.insert("step".into(), json!(step));
        body.insert("approved".into(), json!(approved));
        if let Some(r) = reason {
            body.insert("reason".into(), json!(r));
        }
        let path = format!("{}/decision", Self::key_path(process, business_key));
        self.build("POST", &path, Some(Value::Object(body)), token)
    }

    pub fn cancel_request(&self, process: &str, business_key: &str, token: Option<&str>) -> HttpRequest {
        let path = format!("{}/cancel", Self::key_path(process, business_key));
        self.build("POST", &path, None, token)
    }
}

// ---------------------------------------------------------------------------------------------
// Responses
// ---------------------------------------------------------------------------------------------

/// `#request`: non-success -> HTTP error; 204 or a blank body -> `{}`; otherwise a JSON object.
pub fn parse_object_response(resp: &HttpResponse) -> Result<Map<String, Value>, FoundgineError> {
    if !resp.ok() {
        return Err(error_for(resp.status, &resp.body));
    }
    if resp.status == 204 || resp.body.trim().is_empty() {
        return Ok(Map::new());
    }
    match serde_json::from_str::<Value>(&resp.body) {
        Ok(Value::Object(m)) => Ok(m),
        Ok(_) => Err(FoundgineError::other("Workflow endpoint returned a non-object JSON response")),
        Err(_) => Err(FoundgineError::other("Workflow endpoint returned invalid JSON")),
    }
}

/// For `decide` and `cancel` (the TS `empty = true` routes): only the status matters.
pub fn parse_empty_response(resp: &HttpResponse) -> Result<(), FoundgineError> {
    if resp.ok() {
        Ok(())
    } else {
        Err(error_for(resp.status, &resp.body))
    }
}

pub fn parse_processes(raw: &Map<String, Value>) -> Result<Vec<String>, FoundgineError> {
    let err = || FoundgineError::other("Workflow catalog response is missing processes");
    let items = match raw.get("processes") {
        Some(Value::Array(a)) => a,
        _ => return Err(err()),
    };
    items.iter().map(|v| v.as_str().map(String::from).ok_or_else(err)).collect()
}

pub fn parse_description(raw: &Map<String, Value>) -> Result<WorkflowProcessDescription, FoundgineError> {
    let (name, items) = match (raw.get("name"), raw.get("steps")) {
        (Some(Value::String(n)), Some(Value::Array(a))) => (n.clone(), a),
        _ => return Err(FoundgineError::other("Workflow process description is invalid")),
    };
    let mut steps = Vec::with_capacity(items.len());
    for value in items {
        let step = match value {
            Value::Object(m) => m,
            _ => return Err(FoundgineError::other("Workflow step description is invalid")),
        };
        let name = match step.get("name") {
            Some(Value::String(n)) => n.clone(),
            _ => return Err(FoundgineError::other("Workflow step description is missing name")),
        };
        steps.push(WorkflowStepDescription {
            name,
            requires_approval: step.get("requiresApproval") == Some(&Value::Bool(true)),
            has_compensation: step.get("hasCompensation") == Some(&Value::Bool(true)),
            approval_timeout_seconds: step.get("approvalTimeoutSeconds").and_then(Value::as_f64),
        });
    }
    Ok(WorkflowProcessDescription { name, steps })
}

pub fn parse_start(raw: Map<String, Value>) -> Result<WorkflowStartResult, FoundgineError> {
    let workflow_id = match raw.get("workflowId") {
        Some(Value::String(id)) => id.clone(),
        _ => return Err(FoundgineError::other("Workflow start returned no workflowId")),
    };
    let status = match raw.get("status") {
        Some(Value::String(s)) => s.clone(),
        _ => "Started".to_string(),
    };
    let already_existed = raw.get("alreadyExisted") == Some(&Value::Bool(true));
    Ok(WorkflowStartResult { workflow_id, status, already_existed, raw })
}

pub fn parse_status(raw: Map<String, Value>) -> Result<WorkflowStatus, FoundgineError> {
    let (workflow_id, temporal_status) = match (raw.get("workflowId"), raw.get("temporalStatus")) {
        (Some(Value::String(w)), Some(Value::String(t))) => (w.clone(), t.clone()),
        _ => return Err(FoundgineError::other("Workflow status response is missing workflowId or temporalStatus")),
    };
    let process = raw.get("process").cloned();
    Ok(WorkflowStatus { workflow_id, temporal_status, process, raw })
}

// ---------------------------------------------------------------------------------------------
// Polling decisions
// ---------------------------------------------------------------------------------------------

/// What the host does next.
#[derive(Debug, Clone, PartialEq)]
pub enum PollStep {
    /// Stop: the last status was terminal. The host resolves `done` with it.
    Finished,
    /// Wait this many milliseconds (abortable), then poll again.
    Sleep(u64),
    /// Stop and reject `done` with this error.
    Fail(FoundgineError),
}

#[derive(Debug, Clone, PartialEq)]
pub struct StatusObservation {
    /// True when this status differs from the previous one: record it for `updates()` and call `on_update`.
    pub is_new: bool,
    pub step: PollStep,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ErrorObservation {
    /// `Some(n)`: call `on_error(error, n)` (callback failures must be swallowed).
    pub notify_error: Option<u32>,
    pub step: PollStep,
}

#[derive(Debug, Clone, Copy)]
pub struct WatchOptions {
    pub interval_ms: u64,
    pub timeout_ms: Option<u64>,
    pub not_found_grace_ms: u64,
    pub max_consecutive_errors: u32,
}

impl Default for WatchOptions {
    fn default() -> Self {
        WatchOptions { interval_ms: DEFAULT_INTERVAL_MS, timeout_ms: None, not_found_grace_ms: DEFAULT_NOT_FOUND_GRACE_MS, max_consecutive_errors: DEFAULT_MAX_CONSECUTIVE_ERRORS }
    }
}

/// Decision logic of `watch()` (the TS `#poll`). The clock is injected.
#[derive(Debug, Clone)]
pub struct WatchPoller {
    options: WatchOptions,
    started_ms: u64,
    errors: u32,
    latest: Option<WorkflowStatus>,
    last_raw: Option<Map<String, Value>>,
}

impl WatchPoller {
    pub fn new(options: WatchOptions, now_ms: u64) -> Self {
        WatchPoller { options, started_ms: now_ms, errors: 0, latest: None, last_raw: None }
    }

    fn after_attempt(&self, now_ms: u64) -> PollStep {
        if let Some(limit) = self.options.timeout_ms {
            if now_ms.saturating_sub(self.started_ms) >= limit {
                let last = self.latest.as_ref().map(WorkflowStatus::to_value);
                return PollStep::Fail(FoundgineError::watch_timeout(last, None));
            }
        }
        PollStep::Sleep(self.options.interval_ms)
    }

    /// A status poll succeeded. Resets the error streak; `is_new` is false for a repeat of the previous raw status.
    pub fn observe_status(&mut self, status: &WorkflowStatus, now_ms: u64) -> StatusObservation {
        self.latest = Some(status.clone());
        self.errors = 0;
        let is_new = self.last_raw.as_ref() != Some(&status.raw);
        if is_new {
            self.last_raw = Some(status.raw.clone());
        }
        let step = if status.is_finished() { PollStep::Finished } else { self.after_attempt(now_ms) };
        StatusObservation { is_new, step }
    }

    /// A status poll failed. 401 is fatal at once; 404 is tolerated inside the grace window (a just-started
    /// workflow may not be visible yet) and does not count as an error; anything else counts and is fatal
    /// once the streak exceeds `max_consecutive_errors`.
    pub fn observe_error(&mut self, error: FoundgineError, now_ms: u64) -> ErrorObservation {
        if error.is_http_error() && error.status() == Some(401) {
            return ErrorObservation { notify_error: None, step: PollStep::Fail(error) };
        }
        if error.is_http_error() && error.status() == Some(404) {
            if now_ms.saturating_sub(self.started_ms) > self.options.not_found_grace_ms {
                return ErrorObservation { notify_error: None, step: PollStep::Fail(error) };
            }
            return ErrorObservation { notify_error: None, step: self.after_attempt(now_ms) };
        }
        self.errors += 1;
        let count = self.errors;
        if count > self.options.max_consecutive_errors {
            return ErrorObservation { notify_error: Some(count), step: PollStep::Fail(error) };
        }
        ErrorObservation { notify_error: Some(count), step: self.after_attempt(now_ms) }
    }
}

/// Decision logic of `wait_for_completion`: any error ends the wait (the host propagates it).
#[derive(Debug, Clone)]
pub struct WaitPoller {
    interval_ms: u64,
    timeout_ms: Option<u64>,
    started_ms: u64,
}

impl WaitPoller {
    pub fn new(interval_ms: Option<u64>, timeout_ms: Option<u64>, now_ms: u64) -> Self {
        WaitPoller { interval_ms: interval_ms.unwrap_or(DEFAULT_INTERVAL_MS), timeout_ms, started_ms: now_ms }
    }

    /// Finished, or `Fail(WatchTimeout(last status))` once the timeout has passed, else sleep.
    pub fn observe(&self, status: &WorkflowStatus, now_ms: u64) -> PollStep {
        if status.is_finished() {
            return PollStep::Finished;
        }
        if let Some(limit) = self.timeout_ms {
            if now_ms.saturating_sub(self.started_ms) >= limit {
                return PollStep::Fail(FoundgineError::watch_timeout(Some(status.to_value()), None));
            }
        }
        PollStep::Sleep(self.interval_ms)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn resp(status: u16, body: &str) -> HttpResponse {
        HttpResponse { status, headers: vec![], body: body.to_string() }
    }

    fn st(temporal: &str, extra: i64) -> WorkflowStatus {
        let raw = json!({"workflowId": "w1", "temporalStatus": temporal, "n": extra});
        parse_status(raw.as_object().unwrap().clone()).unwrap()
    }

    fn header<'a>(r: &'a HttpRequest, name: &str) -> Option<&'a str> {
        r.headers.iter().find(|(k, _)| k.eq_ignore_ascii_case(name)).map(|(_, v)| v.as_str())
    }

    #[test]
    fn terminal_set_is_case_insensitive_and_closed() {
        for s in ["COMPLETED", "failed", "Canceled", "CANCELLED", "terminated", "Timed_Out"] {
            assert!(is_terminal_status(s), "{s}");
        }
        for s in ["RUNNING", "ContinuedAsNew", "", "TIMEDOUT", "unknown"] {
            assert!(!is_terminal_status(s), "{s}");
        }
    }

    #[test]
    fn routes_and_encoding() {
        let r = WorkflowRoutes::new("https://h");
        assert_eq!(r.processes_request(None).url, "https://h/foundgine/workflows");
        let s = r.status_request("order approval", "k 1/2", Some("t"));
        assert_eq!(s.url, "https://h/foundgine/workflows/order%20approval/k%201%2F2");
        assert_eq!((s.method, header(&s, "authorization")), ("GET", Some("Bearer t")));
        assert!(s.body.is_none() && header(&s, "content-type").is_none());
        assert_eq!(r.describe_request("p", None).url, "https://h/foundgine/workflows/p");
        assert_eq!(r.cancel_request("p", "k", None).url, "https://h/foundgine/workflows/p/k/cancel");
        assert!(r.cancel_request("p", "k", None).body.is_none());
    }

    #[test]
    fn start_body_defaults_payload() {
        let r = WorkflowRoutes::new("https://h");
        let req = r.start_request("p", "k 1", None, None);
        assert_eq!(req.method, "POST");
        assert_eq!(header(&req, "content-type"), Some("application/json"));
        let v: Value = serde_json::from_str(&req.body.unwrap()).unwrap();
        assert_eq!(v, json!({"businessKey": "k 1", "payload": {}}));
    }

    #[test]
    fn decision_body_has_no_identity_and_omits_missing_reason() {
        let r = WorkflowRoutes::new("https://h");
        let a = r.decision_request("p", "k", "approve", true, None, None);
        assert_eq!(a.url, "https://h/foundgine/workflows/p/k/decision");
        assert_eq!(serde_json::from_str::<Value>(&a.body.unwrap()).unwrap(), json!({"step": "approve", "approved": true}));
        let b = r.decision_request("p", "k", "approve", false, Some("too risky"), None);
        assert_eq!(serde_json::from_str::<Value>(&b.body.unwrap()).unwrap(), json!({"step": "approve", "approved": false, "reason": "too risky"}));
    }

    #[test]
    fn object_response_rules() {
        assert!(parse_object_response(&resp(204, "")).unwrap().is_empty());
        assert!(parse_object_response(&resp(200, "  \n")).unwrap().is_empty());
        assert_eq!(parse_object_response(&resp(200, r#"{"a":1}"#)).unwrap()["a"], 1);
        assert!(matches!(parse_object_response(&resp(200, "[1]")), Err(FoundgineError::Other { .. })));
        assert!(matches!(parse_object_response(&resp(200, "{")), Err(FoundgineError::Other { .. })));
        assert_eq!(parse_object_response(&resp(404, r#"{"error":"nope"}"#)).unwrap_err().status(), Some(404));
        assert!(parse_empty_response(&resp(202, "")).is_ok());
        assert_eq!(parse_empty_response(&resp(403, "")).unwrap_err().status(), Some(403));
    }

    #[test]
    fn catalog_and_description() {
        let m = json!({"processes": ["a", "b"]});
        assert_eq!(parse_processes(m.as_object().unwrap()).unwrap(), vec!["a", "b"]);
        assert!(parse_processes(json!({"processes": ["a", 1]}).as_object().unwrap()).is_err());
        assert!(parse_processes(&Map::new()).is_err());
        let d = json!({"name": "order", "steps": [
            {"name": "s1", "requiresApproval": true, "approvalTimeoutSeconds": 30},
            {"name": "s2", "hasCompensation": true, "operation": "ignored"}
        ]});
        let d = parse_description(d.as_object().unwrap()).unwrap();
        assert_eq!(d.name, "order");
        assert_eq!((d.steps[0].requires_approval, d.steps[0].approval_timeout_seconds), (true, Some(30.0)));
        assert_eq!((d.steps[1].has_compensation, d.steps[1].approval_timeout_seconds), (true, None));
        assert!(parse_description(json!({"name": "x", "steps": [1]}).as_object().unwrap()).is_err());
        assert!(parse_description(json!({"name": "x", "steps": [{}]}).as_object().unwrap()).is_err());
        assert!(parse_description(json!({"steps": []}).as_object().unwrap()).is_err());
    }

    #[test]
    fn start_and_status_parsing() {
        let s = parse_start(json!({"workflowId": "w", "alreadyExisted": true}).as_object().unwrap().clone()).unwrap();
        assert_eq!((s.workflow_id.as_str(), s.status.as_str(), s.already_existed), ("w", "Started", true));
        assert!(parse_start(Map::new()).is_err());
        let w = parse_status(json!({"workflowId": "w", "temporalStatus": "Running", "process": {"k": 1}}).as_object().unwrap().clone()).unwrap();
        assert_eq!(w.process, Some(json!({"k": 1})));
        assert!(!w.is_finished());
        assert!(parse_status(json!({"workflowId": "w"}).as_object().unwrap().clone()).is_err());
    }

    #[test]
    fn watch_dedups_and_finishes() {
        let mut p = WatchPoller::new(WatchOptions::default(), 0);
        let a = p.observe_status(&st("RUNNING", 1), 10);
        assert!(a.is_new && a.step == PollStep::Sleep(1000));
        let same = p.observe_status(&st("RUNNING", 1), 1010);
        assert!(!same.is_new);
        let changed = p.observe_status(&st("RUNNING", 2), 2010);
        assert!(changed.is_new);
        let done = p.observe_status(&st("completed", 3), 3010);
        assert!(done.is_new && done.step == PollStep::Finished);
    }

    #[test]
    fn watch_timeout_carries_the_last_status() {
        let opts = WatchOptions { timeout_ms: Some(1500), ..WatchOptions::default() };
        let mut p = WatchPoller::new(opts, 0);
        assert_eq!(p.observe_status(&st("RUNNING", 1), 1000).step, PollStep::Sleep(1000));
        match p.observe_status(&st("RUNNING", 1), 2000).step {
            PollStep::Fail(FoundgineError::WatchTimeout { last_status, .. }) => {
                assert_eq!(last_status.unwrap()["temporalStatus"], "RUNNING");
            }
            other => panic!("expected timeout, got {other:?}"),
        }
    }

    #[test]
    fn watch_401_is_fatal_at_once() {
        let mut p = WatchPoller::new(WatchOptions::default(), 0);
        let o = p.observe_error(FoundgineError::http(401, None, None), 10);
        assert_eq!(o.notify_error, None);
        assert!(matches!(o.step, PollStep::Fail(FoundgineError::Http { status: 401, .. })));
    }

    #[test]
    fn watch_404_has_a_grace_window_and_is_not_counted() {
        let mut p = WatchPoller::new(WatchOptions::default(), 0);
        let inside = p.observe_error(FoundgineError::http(404, None, None), 4000);
        assert_eq!((inside.notify_error, inside.step), (None, PollStep::Sleep(1000)));
        let outside = p.observe_error(FoundgineError::http(404, None, None), 5001);
        assert!(matches!(outside.step, PollStep::Fail(FoundgineError::Http { status: 404, .. })));
        // exactly at the grace limit is still tolerated (TS: strictly greater fails)
        let mut q = WatchPoller::new(WatchOptions::default(), 0);
        assert!(matches!(q.observe_error(FoundgineError::http(404, None, None), 5000).step, PollStep::Sleep(_)));
    }

    #[test]
    fn watch_error_streak_resets_on_success_and_fails_past_the_limit() {
        let opts = WatchOptions { max_consecutive_errors: 2, ..WatchOptions::default() };
        let mut p = WatchPoller::new(opts, 0);
        let boom = || FoundgineError::http(500, None, None);
        assert_eq!(p.observe_error(boom(), 1).notify_error, Some(1));
        assert_eq!(p.observe_error(boom(), 2).notify_error, Some(2));
        p.observe_status(&st("RUNNING", 1), 3);
        let after_reset = p.observe_error(boom(), 4);
        assert_eq!((after_reset.notify_error, after_reset.step), (Some(1), PollStep::Sleep(1000)));
        p.observe_error(boom(), 5);
        let third = p.observe_error(boom(), 6);
        assert_eq!(third.notify_error, Some(3));
        assert!(matches!(third.step, PollStep::Fail(FoundgineError::Http { status: 500, .. })));
    }

    #[test]
    fn wait_poller() {
        let w = WaitPoller::new(Some(250), Some(1000), 0);
        assert_eq!(w.observe(&st("RUNNING", 1), 100), PollStep::Sleep(250));
        assert_eq!(w.observe(&st("FAILED", 1), 100), PollStep::Finished);
        match w.observe(&st("RUNNING", 1), 1000) {
            PollStep::Fail(FoundgineError::WatchTimeout { last_status: Some(_), .. }) => {}
            other => panic!("expected timeout, got {other:?}"),
        }
        assert_eq!(WaitPoller::new(None, None, 0).observe(&st("RUNNING", 1), 9_999_999), PollStep::Sleep(1000));
    }
}
