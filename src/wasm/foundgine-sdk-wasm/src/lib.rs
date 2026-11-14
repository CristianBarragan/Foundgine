//! Foundgine WebAssembly SDK core. Mirrors `@foundgine/sdk` (TypeScript) and `foundgine-sdk` (Python)
//! module for module: `errors`, `sse`, `http`, `mcp`, `client`, `workflows`.
//!
//! This crate is transport only: it hosts no MCP server and has no business logic. Every module is pure (no I/O, no JS), so `cargo test` runs them natively and the same code runs
//! unchanged on `wasm32-unknown-unknown`.

pub mod client;
pub mod errors;
pub mod http;
pub mod mcp;
pub mod sse;
pub mod workflows;

/// wasm-bindgen shell around the modules above; compiled for `wasm32` only.
#[cfg(target_arch = "wasm32")]
pub mod bindings;

pub use client::{Tool, ToolResult};
pub use errors::FoundgineError;
pub use mcp::{HttpRequest, HttpResponse, McpProtocol};
pub use sse::{SseError, SseEvent, SseParser};
pub use workflows::{WaitPoller, WatchPoller, WorkflowRoutes};
