# Foundgine Temporal integration

Optional durable orchestration for Foundgine, inside `Foundgine.Extensions`. Applications that never call
`AddFoundgineTemporal*` never connect to Temporal (the client connects lazily).

## Division of responsibility

| Component | Responsibility |
|---|---|
| EventStore (`Foundgine.Providers`) | Stores business events |
| Outbox (`Foundgine.Providers`) | Reliably hands committed events to infrastructure (at-least-once) |
| **Temporal (this folder)** | Orchestrates long-running processes: retries, timers, approvals, compensation |
| Foundgine runtime / gateway execution | Authorizes and executes every data operation |

## Reused from Foundgine.Providers

- `IFoundgineOutboxPublisher` — `FoundgineTemporalOutboxPublisher` is the dispatcher's publisher; the outbox message id
  becomes the workflow business key, so redelivery is absorbed.
- `IFoundgineProvider` / `FoundgineProviderKind.Workflow` — `FoundgineTemporalProvider` declares
  DurableExecution, WorkflowSignals, WorkflowQueries, Retry, TenantIsolation.
- `AddFoundgineOutboxDispatcher` — keeps the outbox's retry and dead-letter handling in front of Temporal.
- `IFoundgineGatewayExecution` / `ISecurityExecutionContextProvider` — operations call these inside activities.

## Model

A process is a `FoundgineProcessDefinition`: ordered steps, each a named `IFoundgineTemporalOperation`, with optional
compensation and an optional approval gate. `FoundgineProcessWorkflow` runs any definition. Clients pick a process by
name; they cannot submit operations.

## Security notes

- Workflow input carries tenant and initiator ids only, never warrants. The worker re-mints authority through
  `IFoundgineTemporalSecurityContextFactory` for every activity attempt and for every Foundgine call inside it (warrants are single-use).
- Workflow ids are `{prefix}/{tenant}/{process}/{key}` built from the authenticated tenant; HTTP routes cannot address
  another tenant's workflow.
- The approval endpoint stamps `DecidedBy` from the trusted context; protect it with `ApprovalPolicy`.
- Run the API and worker as separate processes. `AddFoundgineTemporalWorker` replaces `ISecurityExecutionContextProvider`
  with an activity-scoped one.
- Workflow payloads live in Temporal history: keep them small and free of secrets; configure a payload codec
  (encryption) for regulated data.

## HTTP

```
POST /foundgine/workflows/{process}              { "businessKey": "...", "payload": { ... } }  -> 202
GET  /foundgine/workflows/{process}/{key}
POST /foundgine/workflows/{process}/{key}/decision { "step": "approve", "approved": true }
POST /foundgine/workflows/{process}/{key}/cancel
```

## Local development

`docker compose -f docker-compose.temporal.yml up` then run `Samples/Temporal.Worker` and `Samples/Temporal.Api`.

## With Keycloak

`AddFoundgineKeycloakWarrants(...).UseForTemporalWorker()` supplies the worker's `IFoundgineTemporalSecurityContextFactory`, and
`AddFoundgineKeycloak(...).AddRolePolicy("workflow-approver", ...)` protects the approval endpoint. See `Keycloak/README.md`, including the Keycloak Admin operations for customer onboarding (`AddKeycloakAdminOperations()`).
