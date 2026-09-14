# Tool-call governance

This covers `ToolRegistry/`, `RiskScoring/`, `PolicyGateway/`, `Approvals/`,
`AuditLog/`, and `ToolCallGovernor.cs`. For the authority-recovery subsystem
(`Recovery/`), see `README.md` in this folder — that subsystem is unrelated
to tool-call governance and predates it.

## What this is

A governance layer for AI-agent tool calls: a policy firewall, risk
scorer, human approval workflow, tool registry, and audit trail that sit in
front of `Foundgine.Providers.Tools.MCP`. This is the same shape used by
other agent-tool harnesses (routing/gating/approving/auditing tool
invocations), applied to Foundgine's existing security-warrant model.

## What this is not

It is not a multi-backend data router, a scheduler, or part of
`FoundgineEngine`'s compile/authorize/execute pipeline. `FoundgineEngine`
still takes exactly one `IExecutionProvider` via DI at construction time,
unchanged. Nothing here modifies `Foundgine.Core` or
`Foundgine.Core.Execution`.

## Flow

```text
tool call (name + SecurityExecutionContext)
          |
          v
   IToolRegistry            -- is the tool registered and active?
          |
          v
   CompositeRiskScorer       -- explainable RiskScore from IRiskRule signals
          |
          v
   IPolicyGateway             -- Allow / RequireApproval / Deny
          |            |            |
        Deny   RequireApproval    Allow
          |            |            |
        stop    IApprovalStore   IRoutingEngine
                (human sign-off)  -> TaskContract
                       |
              ToolCallGovernor.ResumeAfterApproval
                       |
                 IRoutingEngine -> TaskContract

Every step is recorded to IAuditLog.
```

## Relationship to `Foundgine.Core.PlanApproval`

`PlanApproval` (in `Foundgine.Core`) binds an execution to an exact
authorized plan fingerprint at the moment of execution — it's a replay/
tamper guard, not a workflow. `ControlPlane.Approvals.ApprovalRequest` is
the human-in-the-loop step that can precede it: a governor may require an
`ApprovalRequest` to reach `Granted` before a `PlanApproval` is ever
created. The two compose; neither replaces the other.

## Extending

Register custom rules through `ToolGovernanceBuilder`:

```csharp
services.AddFoundgineToolGovernance(builder => builder
    .RegisterTool(new ToolDescriptor("orders.cancel", capabilities, RiskTier.High, ToolStatus.Active))
    .AddRiskRule<DestructiveCapabilityRiskRule>()
    .AddPolicyRule<RequireApprovalForHighRiskPolicy>()
    .AddRoutingRule<IsolateHighRiskRoutingRule>());
```

All five subsystems default to process-local, in-memory implementations.
Swap `IApprovalStore` and `IAuditLog` for durable implementations in any
deployment where pending approvals or audit history must survive a
restart.
