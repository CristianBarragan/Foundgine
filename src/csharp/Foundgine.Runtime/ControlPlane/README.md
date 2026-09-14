# Foundgine.Runtime.ControlPlane

This folder holds two unrelated control-plane subsystems that happen to
share the name:

- **`Recovery/`** — authority/control-plane recovery infrastructure,
  described below.
- **`ToolRegistry/`, `RiskScoring/`, `PolicyGateway/`, `Approvals/`,
  `AuditLog/`, `ToolCallGovernor.cs`** — AI-agent tool-call governance. See
  [`GOVERNANCE.md`](./GOVERNANCE.md) in this folder for that subsystem.

The rest of this README covers `Recovery/` only.

`Foundgine.Runtime.ControlPlane` is optional provider-agnostic infrastructure for managing and recovering an authorization authority/control plane.

## What is in this package

The package contains recovery and authority-lifecycle primitives for:

- authority anchors and sequence/term state;
- witness sets, credentials and quorum attestations;
- publication integrity and publication-key lifecycle/rotation/retirement;
- credential authentication, lifecycle and revocation;
- journal integrity, consensus and reconciliation;
- promotion, failover and promotion atomicity;
- cross-instance concurrency and commit atomicity;
- repair/rejoin safety and repair ordering;
- reconfiguration authentication, lifecycle and audit ledgers;
- authority freshness and evidence transitions.

## Architectural boundary

This is **not required by the Foundgine semantic execution core**.

```text
external authority/control plane
          ↓
validated security context
          ↓
Foundgine.Core.Semantic / Foundgine.Core.Execution
```

Use it when an application needs high-assurance authority recovery or distributed authorization-control-plane infrastructure. Ordinary applications can supply their own host-owned security context without this package.

## Install

```bash
dotnet add package Foundgine.Runtime.ControlPlane
```
