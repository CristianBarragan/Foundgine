//! Runs the shared `format` vectors from `src/conformance/mcp-sse-scenarios.json` through the parser:
//! whole frame, one byte at a time, and all frames in one stream. Injected `id:`/`event:` text inside
//! a payload must stay payload. Set FOUNDGINE_CONFORMANCE_DIR to override the vector directory.
//! The replay-buffer vectors are server state machines and are not driven from a client.

use foundgine_sdk_wasm::SseParser;
use serde_json::Value;
use std::path::PathBuf;

fn vectors() -> Value {
    let dir = std::env::var("FOUNDGINE_CONFORMANCE_DIR").map(PathBuf::from).unwrap_or_else(|_| PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../conformance"));
    let path = dir.join("mcp-sse-scenarios.json");
    let text = std::fs::read_to_string(&path).unwrap_or_else(|e| panic!("shared vectors not found at {}: {e}", path.display()));
    serde_json::from_str(&text).expect("vectors are valid JSON")
}

/// (sequence, original data, frame) for every `format` step.
fn format_cases() -> Vec<(String, String, String)> {
    let v = vectors();
    let mut cases = Vec::new();
    for scenario in v["scenarios"].as_array().expect("scenarios") {
        for step in scenario["steps"].as_array().expect("steps") {
            if step["op"] == "format" {
                cases.push((step["sequence"].as_str().unwrap().to_string(), step["data"].as_str().unwrap().to_string(), step["expect"].as_str().unwrap().to_string()));
            }
        }
    }
    assert!(!cases.is_empty(), "no format vectors found: the test would pass silently");
    cases
}

/// What the client must see: the payload with every line break normalised to `\n`.
fn normalised(data: &str) -> String {
    data.replace("\r\n", "\n").replace('\r', "\n")
}

#[test]
fn whole_frame() {
    for (seq, data, frame) in format_cases() {
        let mut p = SseParser::new();
        let out = p.push_str(&frame);
        assert_eq!(out.len(), 1, "seq {seq}");
        assert_eq!(out[0].id.as_deref(), Some(seq.as_str()));
        assert_eq!(out[0].event, None, "seq {seq}: injected event: must stay payload");
        assert_eq!(out[0].data, normalised(&data), "seq {seq}");
    }
}

#[test]
fn one_byte_chunks() {
    for (seq, data, frame) in format_cases() {
        let mut p = SseParser::new();
        let mut out = Vec::new();
        for b in frame.as_bytes() {
            out.extend(p.push_bytes(&[*b]).unwrap());
        }
        assert_eq!(out.len(), 1, "seq {seq}");
        assert_eq!(out[0].id.as_deref(), Some(seq.as_str()));
        assert_eq!(out[0].data, normalised(&data), "seq {seq}");
    }
}

#[test]
fn all_frames_in_one_stream() {
    let cases = format_cases();
    let stream: String = cases.iter().map(|c| c.2.as_str()).collect();
    let mut p = SseParser::new();
    let out = p.push_bytes(stream.as_bytes()).unwrap();
    assert_eq!(out.len(), cases.len());
    for (e, (seq, data, _)) in out.iter().zip(cases.iter()) {
        assert_eq!(e.id.as_deref(), Some(seq.as_str()));
        assert_eq!(e.event, None);
        assert_eq!(e.data, normalised(data));
    }
}
