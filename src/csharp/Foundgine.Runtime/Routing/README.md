# Foundgine.Runtime.Routing

Decides **how** a governed tool call runs — not **where** it executes.

## Scope

Routing produces a `TaskContract`:

- **Mode** — Foreground (caller awaits inline) or Background (handed off).
- **Runtime** — Local, Remote, or Isolated.
- **Worker** — New or Resume.
- **Lifecycle** — cancelable/observable/retry policy.
- **PolicyTags** — carried through from risk/policy evaluation.

## What it does not do

Routing never selects a backend execution provider. Backend selection is
already solved by `IExecutionProvider`, chosen once by the host via DI when
`FoundgineEngine` is constructed. Routing sits entirely upstream of that —
in front of `Foundgine.Providers.Tools.MCP`, as part of
`Foundgine.Runtime.ControlPlane.ToolCallGovernor` — and never touches
`FoundgineEngine`, `SemanticRequest`, or the compile/authorize/execute
pipeline.

## Honesty note

Foundgine does not ship a background-worker or scheduler runtime.
`TaskContract` is the *decision*; actually spinning up an isolated or
background worker per the contract is host-owned infrastructure, the same
division `Foundgine.Runtime.ControlPlane.Recovery` uses for authority
infrastructure.

## Extending

Implement `IRoutingRule` and register it with
`ToolGovernanceBuilder.AddRoutingRule<T>()`. Rules are evaluated in
registration order; the first non-abstaining rule wins. An empty rule set
falls back to `TaskContract.Default` (foreground, local, new) rather than
blocking execution.
