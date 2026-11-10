# Approvals, audit and governance

> **In plain words.** Three questions: *Who said yes to this change?* (approvals), *Can we prove what happened?* (audit), and *Is the AI agent allowed to call this tool right now?* (governance). Foundgine makes sure the yes and its receipt are saved **together or not at all**, and that audit failure blocks the action instead of being ignored.

## Gateway approvals (atomic)

- `IFoundgineGatewayApprovalStore` is the ordinary store; `IAtomicFoundgineGatewayApprovalStore` is the stronger contract **required** for creating approvals. A store implementing only the old interface is rejected before any handle is returned.
- The atomic call receives non-secret `ApprovalRequested` evidence (event id, subject, tenant, plan fingerprint, timestamps, expiry) and must commit approval and evidence in one transaction.
- **`PostgresFoundgineGatewayApprovalStore`** (C# and Java; **implemented-unverified**, Java integration-tested): inserts the approval row and its outbox row on one connection and transaction. It rejects standalone writes. Consumption is a conditional `DELETE ... RETURNING` bound to handle digest, subject, tenant and expiry; it commits only after the codec decodes, so concurrent consumers cannot both succeed. Migration `004_gateway_approvals.sql`.
- Global-scope approvals use the reserved outbox partition `__foundgine_global__`; a real tenant with that name is rejected. The outbox `message_key` is the event id (not the lookup digest). Event ids use `N` format in both runtimes. `PurgeExpiredAsync` / `purgeExpired` must be scheduled by the host.
- `InMemoryFoundgineGatewayApprovalStore`: development only, process-local.
- Residual risks: slow codecs hold a row lock; expiry uses the application clock; payloads are plaintext unless a codec encrypts.

## Encrypted approval payloads

`FoundgineGatewayApprovalPayloadCodec` is the port; the host supplies the inner AOT-safe serializer. `EncryptingFoundgineGatewayApprovalPayloadCodec` (C# and Java) wraps it with **AES-256-GCM** and key-id rotation via `IFoundgineGatewayApprovalKeyProvider`. Envelope: `{"v":1,"kid":..,"n":..,"c":..}`, AAD `foundgine.approval.v1|<kid>`. Shared vectors in `src/conformance/approval-envelope-vectors.json` (3 seal, 12 reject cases). Limit: the ciphertext is not bound to subject/tenant/digest (the store predicates are); doing so needs envelope v2.

## Audit log

- `IAuditLog.Record` is synchronous; default `InMemoryAuditLog` is process-local.
- **`BufferedDurableAuditLog`** (both runtimes): bounded queue (C# `Channel`, Java `ArrayBlockingQueue`), lossless backpressure, one sink writer, per-event acknowledgement via `IAuditEventStore` / `AuditEventStore`, sink failures propagate, drain on close. Events enter the query view only after the sink acknowledges.
- Point-in-time metrics: queue depth, accepted, persisted, failed, backpressure waits and wait time. Observational only.
- No production durable sink ships; supply a transactional or append-only one.
- The gateway has its own `IFoundgineGatewayAuditSink`: a critical hook, so publication fails if the sink rejects.

## Tool-call governance (control plane)

`ToolCallGovernor` coordinates: tool registry (`IToolRegistry`, `ToolDescriptor`), risk rules (`IRiskRule`, `RiskScore`), policy gateway (`IPolicyGateway`, `PolicyDecision`), approvals (`IApprovalStore`, `ApprovalRequest`) and audit (`AuditEvent`). Audit writes happen before the final deny/approve/route branch, and the allow-path `Routed` audit happens before the allow result returns, so a sink failure never yields an allow. Open item: approval creation then audit is not yet atomic; a transactional approval+audit outbox is needed. Java adds authorization-context integrity key rings with rotation authorisation. See [AI agents and MCP](agents-mcp).

## Where to look

C#: `Foundgine.Providers/Gateway/Approvals`, `EventStore/Schema`, `Foundgine.Runtime/ControlPlane`. Java: `foundgine-providers/.../gateway/approvals`, `foundgine-runtime/.../controlplane`. Tests: `PostgresGatewayApprovalStoreIntegrationTests`, `ApprovalPayloadEnvelopeConformanceTests` and Java equivalents.
