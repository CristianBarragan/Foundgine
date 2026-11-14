//! Pure parts of `FoundgineClient`. Mirrors `client.ts` / `client.py`: base-URL validation and the
//! request/response shapes for `tools/list` (paged) and `tools/call`. The async driver (paging loop,
//! `close()` once, owning the `McpProtocol` and workflow client) is the JS binding layer's job.

use crate::errors::FoundgineError;
use serde_json::{json, Map, Value};

#[derive(Debug, Clone, PartialEq)]
pub struct Tool {
    pub name: String,
    pub description: Option<String>,
    pub input_schema: Map<String, Value>,
    pub raw: Map<String, Value>,
}

#[derive(Debug, Clone, PartialEq)]
pub struct ToolResult {
    pub content: Vec<Map<String, Value>>,
    /// Present only when the server sent a `structuredContent` key (even `null`).
    pub structured_content: Option<Value>,
    pub is_error: bool,
    pub raw: Map<String, Value>,
}

/// TS: must match `^https?://` (any case); one trailing `/` is removed.
pub fn normalize_base_url(base_url: &str) -> Result<String, String> {
    let lower = base_url.to_ascii_lowercase();
    if !(lower.starts_with("http://") || lower.starts_with("https://")) {
        return Err("baseUrl must start with http:// or https://".to_string());
    }
    Ok(base_url.strip_suffix('/').unwrap_or(base_url).to_string())
}

/// Params of `tools/list`: `{}` for the first page, `{ "cursor": c }` afterwards.
pub fn list_tools_params(cursor: Option<&str>) -> Value {
    match cursor {
        Some(c) if !c.is_empty() => json!({ "cursor": c }),
        _ => json!({}),
    }
}

/// One `tools/list` page: the valid tools (entries without a string `name` are skipped) and the next
/// cursor (`None` when absent or empty, which ends the paging loop).
pub fn parse_tools_page(result: Option<&Value>) -> Result<(Vec<Tool>, Option<String>), FoundgineError> {
    let obj = match result {
        Some(Value::Object(m)) => m,
        _ => return Err(FoundgineError::other("tools/list returned no result object")),
    };
    let mut tools = Vec::new();
    if let Some(Value::Array(items)) = obj.get("tools") {
        for item in items {
            let raw = match item {
                Value::Object(m) => m,
                _ => continue,
            };
            let name = match raw.get("name") {
                Some(Value::String(n)) => n.clone(),
                _ => continue,
            };
            let description = match raw.get("description") {
                Some(Value::String(d)) => Some(d.clone()),
                _ => None,
            };
            let input_schema = match raw.get("inputSchema") {
                Some(Value::Object(m)) => m.clone(),
                _ => Map::new(),
            };
            tools.push(Tool { name, description, input_schema, raw: raw.clone() });
        }
    }
    let next = match obj.get("nextCursor") {
        Some(Value::String(c)) if !c.is_empty() => Some(c.clone()),
        _ => None,
    };
    Ok((tools, next))
}

/// Params of `tools/call`; `arguments` defaults to `{}`.
pub fn call_tool_params(name: &str, arguments: Option<Value>) -> Value {
    json!({ "name": name, "arguments": arguments.unwrap_or_else(|| json!({})) })
}

/// A tool that fails returns `is_error = true` as data; only a non-object result is an SDK error.
pub fn parse_tool_result(result: Option<&Value>) -> Result<ToolResult, FoundgineError> {
    let raw = match result {
        Some(Value::Object(m)) => m,
        _ => return Err(FoundgineError::other("tools/call returned no result object")),
    };
    let content = match raw.get("content") {
        Some(Value::Array(items)) => items
            .iter()
            .filter_map(|x| match x {
                Value::Object(m) => Some(m.clone()),
                _ => None,
            })
            .collect(),
        _ => Vec::new(),
    };
    Ok(ToolResult { content, structured_content: raw.get("structuredContent").cloned(), is_error: raw.get("isError") == Some(&Value::Bool(true)), raw: raw.clone() })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn base_url_rules() {
        assert_eq!(normalize_base_url("https://h/").unwrap(), "https://h");
        assert_eq!(normalize_base_url("HTTP://h").unwrap(), "HTTP://h");
        assert!(normalize_base_url("ftp://h").is_err());
        assert!(normalize_base_url("h").is_err());
    }

    #[test]
    fn list_params_and_paging() {
        assert_eq!(list_tools_params(None), json!({}));
        assert_eq!(list_tools_params(Some("c2")), json!({"cursor": "c2"}));
        let page = json!({"tools": [
            {"name": "a", "description": "A", "inputSchema": {"type": "object"}},
            {"description": "no name"}, 5, {"name": "b", "inputSchema": []}
        ], "nextCursor": "c2"});
        let (tools, next) = parse_tools_page(Some(&page)).unwrap();
        assert_eq!(tools.iter().map(|t| t.name.as_str()).collect::<Vec<_>>(), vec!["a", "b"]);
        assert_eq!(tools[0].description.as_deref(), Some("A"));
        assert_eq!(tools[1].description, None);
        assert!(tools[1].input_schema.is_empty());
        assert_eq!(next.as_deref(), Some("c2"));
        let (_, last) = parse_tools_page(Some(&json!({"tools": [], "nextCursor": ""}))).unwrap();
        assert_eq!(last, None);
        assert!(parse_tools_page(None).is_err());
    }

    #[test]
    fn call_params_default_arguments() {
        assert_eq!(call_tool_params("t", None), json!({"name": "t", "arguments": {}}));
        assert_eq!(call_tool_params("t", Some(json!({"limit": 2}))), json!({"name": "t", "arguments": {"limit": 2}}));
    }

    #[test]
    fn tool_result_failure_is_data() {
        let r = parse_tool_result(Some(&json!({"content": [{"type": "text", "text": "x"}, 3], "isError": true}))).unwrap();
        assert!(r.is_error);
        assert_eq!(r.content.len(), 1);
        assert_eq!(r.structured_content, None);
        let r = parse_tool_result(Some(&json!({"structuredContent": null}))).unwrap();
        assert_eq!(r.structured_content, Some(Value::Null));
        assert!(!r.is_error);
        assert!(parse_tool_result(Some(&json!([1]))).is_err());
        assert!(parse_tool_result(None).is_err());
    }
}
