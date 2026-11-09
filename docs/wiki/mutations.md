# Mutations and controlled writes

## Purpose

Foundgine mutations model writes as explicit semantic operations instead of exposing arbitrary database commands. The design supports reviewable plans, dry runs, approvals, and controlled execution.

## Lifecycle

```text
Propose -> Validate -> Dry-run -> Approve -> Execute approved plan -> Record evidence
```

The host application decides who may propose, approve, and execute. These roles should be independently authorized when the risk model requires separation of duties.

## Mutation intent and operation graph

A mutation intent identifies the semantic target and requested field/value changes. The planner produces operations, dependencies, effects, and a plan. A graph is useful when a business action requires multiple ordered operations. Dependencies must be explicit; execution order must not depend on incidental collection order.

## Dry-run

Dry-run validates the request and estimates or previews its effects without committing the write. It should identify validation failures, affected resources/counts where safely available, and any policy or provider constraints. Dry-run is not a lock and does not guarantee the underlying data will remain unchanged before approval/execution.

## Approval binding

Approval should bind to the exact plan identity/fingerprint, relevant scope, requester, approver, expiry, and policy context. If the plan changes, the old approval must not authorize the modified plan. Approval storage must prevent replay and enforce expiry/revocation semantics.

## Execution

Execution revalidates all security-critical conditions, including caller authority, warrant validity, scope, plan integrity, approval status, resource limits, and provider conformance. Do not assume a successful dry-run or earlier approval removes the need for execution-time checks.

## Batched mutations

Batching introduces atomicity and partial-failure questions. Provider-specific batch boundaries, correlation identifiers, transaction semantics, and result projection must be explicit. Do not claim all-or-nothing behavior unless the provider and transaction boundary guarantee it.

## API surfaces

The runtime exposes mutation abstractions; providers may expose MCP or gateway mutation endpoints; extensions adapt GraphQL or workflow execution. Keep the semantic mutation plan as the common contract rather than creating a separate business mutation engine in every adapter.

## Where to look in source

- C#: `Foundgine.Core/Semantic/Mutation`, `Foundgine.Runtime/Api/IFoundgineMutations.cs`, `MutationBuilder.cs`, `PlanApproval.cs`, `DryRunResult.cs`, `FoundgineMutationEngine.cs`, provider SQL mutation compiler/execution folders.
- Java: `foundgine-core/.../semantic/mutation`, `foundgine-runtime` mutation APIs/engine, and provider mutation packages.
- Related architecture: [Runtime](../RUNTIME.md), [Security](../SECURITY.md), [Public API](../PUBLIC-API.md).
