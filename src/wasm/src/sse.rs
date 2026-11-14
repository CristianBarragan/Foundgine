//! Incremental WHATWG-compatible SSE parser. Mirrors `sse.ts` / `sse.py`.
//!
//! Feed text or raw UTF-8 bytes in chunks of any size. Lines end at `\r\n`, `\r` or `\n` only (never at
//! U+0085, U+2028 and friends), so payloads the server sends raw survive. Multi-byte characters split
//! across chunks are reassembled; invalid UTF-8 is fatal, as with the TS `TextDecoder({ fatal: true })`.

use std::fmt;

/// One dispatched event. `id` and `event` are `None` when the block omitted them.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SseEvent {
    pub id: Option<String>,
    pub event: Option<String>,
    pub data: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SseError {
    InvalidUtf8,
}

impl fmt::Display for SseError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str("invalid UTF-8 in event stream")
    }
}

impl std::error::Error for SseError {}

#[derive(Debug, Default)]
pub struct SseParser {
    buffer: String,
    pending: Vec<u8>,
    started: bool,
    skip_lf: bool,
    data: Vec<String>,
    id: Option<String>,
    event: Option<String>,
    /// The last `id:` seen in a dispatched block (also set when the block had no data).
    pub last_event_id: Option<String>,
    /// The last valid `retry:` value, in milliseconds.
    pub retry_ms: Option<u64>,
}

impl SseParser {
    pub fn new() -> Self {
        Self::default()
    }

    /// Feed raw bytes. A trailing incomplete UTF-8 sequence is held back until the next chunk.
    pub fn push_bytes(&mut self, chunk: &[u8]) -> Result<Vec<SseEvent>, SseError> {
        self.pending.extend_from_slice(chunk);
        let valid_len = match std::str::from_utf8(&self.pending) {
            Ok(_) => self.pending.len(),
            Err(e) => {
                if e.error_len().is_some() {
                    self.pending.clear();
                    return Err(SseError::InvalidUtf8);
                }
                e.valid_up_to()
            }
        };
        let text = std::str::from_utf8(&self.pending[..valid_len]).map_err(|_| SseError::InvalidUtf8)?.to_owned();
        self.pending.drain(..valid_len);
        Ok(self.push_str(&text))
    }

    /// Feed already-decoded text.
    pub fn push_str(&mut self, chunk: &str) -> Vec<SseEvent> {
        let mut text = chunk;
        if !self.started && !text.is_empty() {
            self.started = true;
            if let Some(rest) = text.strip_prefix('\u{FEFF}') {
                text = rest;
            }
        }
        self.buffer.push_str(text);
        self.drain()
    }

    fn drain(&mut self) -> Vec<SseEvent> {
        let mut events = Vec::new();
        let input = std::mem::take(&mut self.buffer);
        let bytes = input.as_bytes();
        let mut pos = 0usize;
        while pos < bytes.len() {
            if self.skip_lf {
                self.skip_lf = false;
                if bytes[pos] == b'\n' {
                    pos += 1;
                    continue;
                }
            }
            let mut end = pos;
            while end < bytes.len() && bytes[end] != b'\r' && bytes[end] != b'\n' {
                end += 1;
            }
            if end == bytes.len() {
                break;
            }
            if bytes[end] == b'\r' {
                self.skip_lf = true;
            }
            // `\r` and `\n` are ASCII, so `pos` and `end` are always char boundaries.
            if let Some(event) = self.line(&input[pos..end]) {
                events.push(event);
            }
            pos = end + 1;
        }
        self.buffer = input[pos..].to_string();
        events
    }

    fn line(&mut self, line: &str) -> Option<SseEvent> {
        if line.is_empty() {
            return self.dispatch();
        }
        if line.starts_with(':') {
            return None;
        }
        let (name, mut value) = match line.find(':') {
            Some(i) => (&line[..i], &line[i + 1..]),
            None => (line, ""),
        };
        if let Some(rest) = value.strip_prefix(' ') {
            value = rest;
        }
        match name {
            "data" => self.data.push(value.to_string()),
            "id" => {
                if !value.contains('\0') {
                    self.id = Some(value.to_string());
                }
            }
            "event" => self.event = Some(value.to_string()),
            "retry" => {
                // TS: /^[0-9]+$/. A value too large for u64 is ignored (TS would keep a float).
                if !value.is_empty() && value.bytes().all(|b| b.is_ascii_digit()) {
                    if let Ok(n) = value.parse::<u64>() {
                        self.retry_ms = Some(n);
                    }
                }
            }
            _ => {}
        }
        None
    }

    fn dispatch(&mut self) -> Option<SseEvent> {
        let id = self.id.take();
        let event = self.event.take();
        let data = std::mem::take(&mut self.data);
        if id.is_some() {
            self.last_event_id = id.clone();
        }
        if data.is_empty() {
            return None;
        }
        Some(SseEvent { id, event, data: data.join("\n") })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ev(id: Option<&str>, event: Option<&str>, data: &str) -> SseEvent {
        SseEvent { id: id.map(String::from), event: event.map(String::from), data: data.to_string() }
    }

    #[test]
    fn parses_a_whole_frame() {
        let mut p = SseParser::new();
        let out = p.push_str("id: 7\nevent: message\ndata: hello\n\n");
        assert_eq!(out, vec![ev(Some("7"), Some("message"), "hello")]);
        assert_eq!(p.last_event_id.as_deref(), Some("7"));
    }

    #[test]
    fn crlf_split_across_chunks_is_one_break() {
        let mut p = SseParser::new();
        assert!(p.push_str("data: a\r").is_empty());
        assert!(p.push_str("\ndata: b\r").is_empty());
        let out = p.push_str("\n\r\n");
        assert_eq!(out, vec![ev(None, None, "a\nb")]);
    }

    #[test]
    fn comments_and_heartbeats_are_ignored() {
        let mut p = SseParser::new();
        assert!(p.push_str(": hb\n\n").is_empty());
        assert_eq!(p.push_str("data: x\n\n"), vec![ev(None, None, "x")]);
    }

    #[test]
    fn only_one_leading_space_is_removed() {
        let mut p = SseParser::new();
        assert_eq!(p.push_str("data:  two\n\n"), vec![ev(None, None, " two")]);
    }

    #[test]
    fn exotic_line_separators_stay_payload() {
        let mut p = SseParser::new();
        let s = "data: a\u{0B}b\u{85}c\u{2028}d\n\n";
        assert_eq!(p.push_str(s), vec![ev(None, None, "a\u{0B}b\u{85}c\u{2028}d")]);
    }

    #[test]
    fn bom_at_stream_start_is_ignored_once() {
        let mut p = SseParser::new();
        assert_eq!(p.push_str("\u{FEFF}data: x\n\n"), vec![ev(None, None, "x")]);
    }

    #[test]
    fn multibyte_character_split_across_byte_chunks() {
        let mut p = SseParser::new();
        let frame = "data: h\u{e9}llo \u{1F600}\n\n".as_bytes().to_vec();
        let mut out = Vec::new();
        for b in frame {
            out.extend(p.push_bytes(&[b]).unwrap());
        }
        assert_eq!(out, vec![ev(None, None, "h\u{e9}llo \u{1F600}")]);
    }

    #[test]
    fn invalid_utf8_is_fatal() {
        let mut p = SseParser::new();
        assert_eq!(p.push_bytes(&[b'd', 0xFF]), Err(SseError::InvalidUtf8));
    }

    #[test]
    fn id_with_nul_is_ignored_and_empty_block_keeps_last_event_id() {
        let mut p = SseParser::new();
        assert!(p.push_str("id: a\0b\ndata: x\n\n").len() == 1);
        assert_eq!(p.last_event_id, None);
        assert!(p.push_str("id: 5\n\n").is_empty());
        assert_eq!(p.last_event_id.as_deref(), Some("5"));
    }

    #[test]
    fn retry_accepts_digits_only() {
        let mut p = SseParser::new();
        p.push_str("retry: 1500\n\n");
        assert_eq!(p.retry_ms, Some(1500));
        p.push_str("retry: 12x\n\n");
        assert_eq!(p.retry_ms, Some(1500));
    }
}
