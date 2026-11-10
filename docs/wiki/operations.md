# Operations, configuration, diagnostics and testing

## Configuration

Foundgine options configure model registration, providers, capabilities, retrieval budgets, security integrations, gateway behavior, event delivery, and diagnostics. Keep configuration explicit and validate it at startup. Secrets such as signing keys, passphrases, database credentials, and service tokens belong in a secret manager or protected environment configuration, never in source control.

## Resource governance

Treat limits as part of the security model. Bound request size, candidate count, alias/token count, graph/path exploration, retrieval time, query execution, result count, mutation batch size, and workflow activity duration. A limit being reached should produce a clear bounded outcome, not an unbounded retry or unsafe fallback.

## Diagnostics and audit

Use structured diagnostics with correlation identifiers. Distinguish operational telemetry (latency, counts, errors) from security audit evidence (principal, decision, scope, plan identity, approval, execution outcome). Avoid putting secrets, raw warrants, tokens, or sensitive values into logs.

## Testing layers

- Unit tests: validators, semantic rules, planners, canonicalization, and individual components.
- Provider conformance tests: same semantic behavior across provider implementations.
- Integration tests: real databases, identity providers, event stores, workflow engines, and transports.
- End-to-end tests: caller-to-execution flows with authorization and evidence.
- Security tests: tenant isolation, replay, warrant tampering, approval binding, privilege escalation, prompt/tool injection, and fail-closed behavior.
- AOT tests: generated metadata, trimming, publish, and runtime consumption.

## Release verification

Build and test the C# and Java implementations independently. Verify NuGet and Maven package contents, analyzer/processor assets, dependency versions, signing, and consumer restore/compile. A static configuration check is not equivalent to a successful build or integration test.

## Adding a capability

1. State the user-visible capability and its security boundary.
2. Identify the smallest stable core contract.
3. Implement orchestration in runtime only when it is cross-provider behavior.
4. Put vendor-specific behavior in providers/extensions.
5. Add startup validation and bounded resource controls.
6. Add positive, negative, and provider conformance tests.
7. Add a sample and update this wiki plus the public API reference.

## Where to look

- C# tests: `src/csharp/tests`, `src/csharp/security`, `src/csharp/benchmarks`.
- Java tests and samples: modules under `src/java`, `src/java/samples`, and `src/java/security`.
- Existing guides: [Testing](../TESTING), [Current status](../CURRENT-STATUS), [Roadmap](../ROADMAP).

See also: [Insights, evidence and auditing](insights-evidence-auditing), [ORM providers](orm-providers), [Extensions](extensions).
