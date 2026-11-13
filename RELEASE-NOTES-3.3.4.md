# Foundgine 3.3.4 release notes

**Release date:** November 14, 2026
**Release line:** .NET 10, Java 27, TypeScript, Python and Rust/WASM transport clients

## Summary

Foundgine 3.3.4 is a cross-SDK maintenance release. It aligns the release metadata for the platform and transport-client SDKs, fixes the Python SDK lint/format failures reported by CI, corrects the C# and Java test-report assertions, and carries the transport-client API and URL-handling parity changes.

This release does **not** claim that the TypeScript, Python or Rust/WASM clients implement the C#/Java semantic runtime, mutation engine or persistence providers. Their contract is the MCP and workflow transport surface. The C# and Java platform implementations retain their own provider-specific capability gates. See [SDK-PARITY.md](SDK-PARITY.md) for the current parity inventory and known gaps.

## Changes

### Python

- Fixed Ruff `RUF036` union ordering in `src/foundgine/_http.py`.
- Applied Ruff formatting to `_http.py` and `types.py`.

### TypeScript and Rust/WASM

- Standardized URL path-segment encoding to RFC 3986 unreserved characters.
- Standardized base/path boundary slash normalization.
- Added Rust/WASM `listCapabilities()` and `execute()` canonical API names while preserving `listTools()` and `callTool()` aliases.
- Added top-level notifications and notification replay/reconnect options to the Rust/WASM facade.

### CI and release metadata

- C# CI now checks that TRX reports contain actual executed tests instead of requiring two absent test class names.
- Java CI now checks the actual `FoundgineTemporalWorkflowTest` Surefire report and requires a non-zero test count.
- Updated current .NET, Maven, MCP, documentation and install metadata to `3.3.4`.
- Updated TypeScript, Python, Rust crate and WASM package metadata and SDK user-agent versions to `3.3.4`.
- Re-enabled the Java Maven Central publish job for `v*` release tags, gated on Java build, security penetration and PostgreSQL E2E success.
- Made npm and PyPI publishing wait for the Rust/WASM SDK test job in addition to the relevant SDK and server checks.

## Package release tags

The repository intentionally uses separate tag prefixes for package publishing:

- `v3.3.4` — .NET/NuGet and Java/Maven release workflows.
- `sdk-v3.3.4` — TypeScript `@foundgine/sdk` npm publishing workflow.
- `py-v3.3.4` — Python `foundgine-sdk` PyPI publishing workflow.

The Rust/WASM crate remains `publish = false` and the `@foundgine/sdk-wasm` npm package remains private; they are built/tested as part of the SDK CI but are not published by these workflows. All release tags are required to point to a commit reachable from `main`.

## Verification status for this prepared source checkpoint

The release archive is prepared from the repository checkpoint plus the SDK-parity changes and the latest Python formatting/lint fixes. The following tools are not installed in this execution environment: .NET SDK, Maven/JDK 27 and Cargo/Rust. Therefore the .NET, Java and Rust/WASM suites and official package builds have not been verified here. The GitHub Actions release workflows must pass on the exact commit before any tags are pushed or packages are published.

Run the following gates in CI:

- Python: `ruff check . --output-format=full`, `ruff format --check .`, `mypy`, and `pytest` on Python 3.11–3.14.
- TypeScript: `npm test` on Node 20 and 22.
- Rust/WASM: `cargo test --all-targets`, `cargo fmt --check`, native and wasm32 Clippy, WASM build and Node tests.
- C#: `dotnet test src/csharp/Foundgine.sln --configuration Release` and NuGet pack validation.
- Java: Maven 27 reactor tests, PostgreSQL integration tests and Central Portal deployment validation.

Do not publish if any required gate fails. Do not describe full platform/client feature parity as complete; the remaining gaps are listed in `SDK-PARITY.md`.
