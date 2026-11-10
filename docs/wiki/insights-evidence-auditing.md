# Insights, evidence and auditing

> **In plain words.** Foundgine answers three "show me" questions. *What happened?* (**evidence and receipts**: a small record of each execution). *Who decided and who approved?* (**audit**: a tamper-resistant diary). *How is it behaving right now?* (**diagnostics**: metrics and traces). Records hold **fingerprints, not your data**, so they can be kept and shipped without copying customer rows.

## 1. Evidence: proof of one execution

- **`ExecutionEvidence`** is attached to every execution: provider name, plan fingerprint, which plan nodes were permission-checked, rows returned, elapsed milliseconds, and optional fingerprints for the provider operation, intent, authorization, warrant and security invariants.
- **`ExecutionReceipt`** is the longer, immutable record: request id, status, versions of the model, capability, intent and plan, intent/plan/authorization/result fingerprints, provider, start and end time, affected nodes, effects, and (for approved changes) who approved and when.
- Neither holds request objects or result rows. If you need the data, read it through an authorized query.

## 2. Audit: who decided what

| Audit | What it records | Durable? |
|---|---|---|
| **Gateway audit** (`IFoundgineGatewayAuditSink`) | Gateway events (execution started/completed/failed, authorization denied, provider changes) | Only if you supply a durable sink. It is a **critical hook**: if the sink rejects an event, publishing fails instead of silently skipping |
| **Tool-call audit** (`IAuditLog`, `AuditEvent`) | Each governance step for an AI tool call: risk scored, policy evaluated, approval requested/decided, routed, denied | In-memory by default; `BufferedDurableAuditLog` adds a bounded queue and per-event acknowledgement |
| **Approval evidence** | `ApprovalRequested` stored in the same transaction as the approval | Yes, with the PostgreSQL approval store |

More detail in [Approvals, audit and governance](approvals-audit-governance).

## 3. Diagnostics: how it is behaving

- Gateway: `FoundgineGatewayDiagnostics` offers an `ActivitySource` and `Meter`, using .NET APIs directly (no OpenTelemetry SDK in the package). Your host subscribes its own OpenTelemetry setup. Metrics include events published, hook failures, events enqueued to subscribers, outbox messages and publish duration.
- Temporal: source name `Foundgine.Temporal` (workflows started, operations run).
- Labels are low-cardinality on purpose: no payloads, SQL, credentials, user ids or tenant ids.

## 4. Live events

- The event stream is **best-effort** and bounded; a slow subscriber can lose old events. For replay, use `Last-Event-ID` or `?after=` on `GET /foundgine/events`.
- Visibility is fail-closed: owner events reach only the owner, tenant events reach that tenant, discovery changes reach every signed-in caller, other system events are for operators only.
- `GET /foundgine/executions/{id}` shows a minimal status (category, timestamps, state, outcome code) to the owner only; unknown and not-yours both return 404.

## Rules of thumb

- An in-memory event stream is **not** an audit log.
- Never store secrets or free-form caller attributes in audit records.
- Audit failure should **block** the action, not be ignored.
- Treat metrics as hints, not proof; proof is the receipt plus your durable audit store.

## Where to look

C#: `Foundgine.Core/Execution` (evidence, receipt), `Foundgine.Providers/Gateway/Audit` and `Diagnostics`, `Foundgine.Runtime/ControlPlane/AuditLog`. Java equivalents sit in `foundgine-providers` and `foundgine-runtime`.

## Related pages

- [Approvals, audit and governance](approvals-audit-governance)
- [Gateway, events and live updates](gateway-events)
- [Provider catalogue](providers)
- [ORM providers (EF Core and JPA)](orm-providers)
- [Operations and extension points](operations)
