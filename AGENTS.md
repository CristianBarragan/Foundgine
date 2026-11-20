# Repository guidance for coding agents

## Project identity

Foundgine is a configurable semantic and execution boundary for application capabilities. It separates application meaning, persistence mapping, authorization and execution from the interfaces and agents that consume them. Fluent configuration is the primary developer experience in C# and Java. AI/MCP/agent integrations are optional consumers; do not frame the project as AI-only.

## Architectural principles

- Preserve the separation of semantic configuration, persistence mapping, authorization and execution.
- The application/host owns trusted identity, tenant context, secrets, domain invariants and policy.
- Treat caller input, retrieval, capability discovery and model output as untrusted; discovery is not authorization.
- Keep providers and interface adapters from creating or elevating authority.
- Prefer language-idiomatic fluent configuration; avoid introducing annotations as the primary configuration mechanism unless the current module explicitly requires them.
- Preserve existing ORM, database and domain infrastructure when designing integrations. Foundgine is not a universal replacement for them.
- Support incremental adoption and conventional use without AI or MCP.

## Working in this repository

- Read the nearest module README and the relevant architecture/wiki document before changing APIs.
- Verify exact API and feature maturity in the implementation, tests and CI. Do not infer parity between languages or providers.
- Keep .NET and Java behavior aligned where shared contracts require it, while respecting idiomatic implementation differences.
- Update documentation when public behavior, configuration, package layout, integration status or supported capabilities change.
- Avoid unsupported claims of universal performance, security superiority or replacement of existing technologies.
- Run the narrowest relevant tests first, then broader build/test commands where practical. Report commands actually run and results accurately.
- Do not mark a feature verified merely because it compiles in a different module or language.

## Documentation hierarchy

1. Implementation, tests and release/CI evidence for current behavior.
2. Module-specific README and API docs.
3. `docs/ARCHITECTURE.md`, `docs/wiki/` and language implementation maps.
4. Root README, `llms.txt` and `llms-full.md` for product framing and discovery.

If sources conflict, verify the implementation and update the affected docs. Keep the canonical product message consistent across README, website, wiki and AI context files.
