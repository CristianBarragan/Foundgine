# Foundgine.Providers.AgentFramework

Microsoft Agent Framework (MAF) adapter for Foundgine. It gives a MAF agent the Foundgine semantic execution boundary as its **only** data access: the agent proposes intent, Foundgine decides what is allowed and executes it.

## What is in this package

- `FoundgineAgentTools` (`Tools`) — the model-facing tools, as Microsoft.Extensions.AI `AITool`s.
- `FoundgineAgentFactory` (`Agents`) — builds a MAF `ChatClientAgent` over those tools.
- `AddFoundgineAgentFramework()` (`Hosting`) — DI registration, including a keyed scoped `AIAgent`.
- `FoundgineAgentFrameworkProvider` (`Hosting`) — declares a Foundgine `Agent` provider (`foundgine.maf.agent`) for gateway discovery and startup negotiation.

## Tools

| Tool | Purpose | Gated |
|---|---|---|
| `foundgine_capabilities` | Discover the semantic surface visible to the current caller | no |
| `foundgine_query_semantic` | Run a structured, provider-neutral read intent | no |
| `foundgine_query` | Natural-language / open intent (only when lexical grounding is registered) | no |
| `foundgine_mutation_dry_run` | Validate and authorize a mutation; returns `planFingerprint` and effects | no |
| `foundgine_mutation_execute` | Execute a dry-run mutation | **MAF human approval** |

Mutation tools are **off by default** (`EnableMutations = false`).

## Namespaces

| Folder | Namespace | Contents |
|---|---|---|
| `Hosting/` | `Foundgine.Providers.AgentFramework.Hosting` | `AddFoundgineAgentFramework`, `FoundgineAgentFrameworkOptions`, `FoundgineAgentFrameworkProvider` |
| `Tools/` | `Foundgine.Providers.AgentFramework.Tools` | `FoundgineAgentTools` |
| `Agents/` | `Foundgine.Providers.AgentFramework.Agents` | `FoundgineAgentFactory`, `FoundgineAgentInstructions` |

## Use

```csharp
using Foundgine.Providers.AgentFramework.Hosting;
using Microsoft.Agents.AI;

builder.Services.AddFoundgine(/* semantic model, providers */);
builder.Services.AddSingleton<ISecurityExecutionContextProvider, HttpContextSecurityProvider>(); // host-owned
builder.Services.AddSingleton<IChatClient>(/* any Microsoft.Extensions.AI chat client */);

builder.Services.AddFoundgineAgentFramework(o =>
{
    o.AgentName = "orders";
    o.EnableMutations = true; // optional; execution is always approval-gated
});

// per request / scope
var agent = scope.ServiceProvider.GetRequiredKeyedService<AIAgent>("orders");
var session = await agent.CreateSessionAsync();
var response = await agent.RunAsync("Which purchase orders are overdue?", session);
```

Without DI: `FoundgineAgentFactory.Create(chatClient, tools, options)`.

## Security model

- **Identity is host-owned.** Every tool call resolves `ISecurityExecutionContextProvider` at call time and fails closed if it returns `null`. The model cannot supply tenant, subject, audience, warrant or authorization context.
- **A model cannot approve its own plan.** There is no model-callable `approve` tool. `foundgine_mutation_execute` is wrapped in `ApprovalRequiredAIFunction`, so MAF pauses the run and returns an approval request for the host/user to answer.
- **Approver identity is not model input.** On approval, Foundgine records the host-resolved subject as `approvedBy`.
- **Plan binding.** `execute` re-plans the mutation and rejects it unless `planFingerprint` matches the dry run; `ExecuteApprovedAsync` then re-authorizes before any provider work.
- **Scoped lifetime.** Tools and the agent are scoped so a per-request security context is never captured by a singleton.

Foundgine retrieval and approval are not authorization: this adapter changes the transport, not the authority model.

## Handling approvals

When a run needs approval, the `AgentResponse` contains an approval request content item (named `FunctionApprovalRequestContent` or `ToolApprovalRequestContent` depending on the MAF release). Show the call to the user, then send back `request.CreateResponse(approved)` as a user message on the same session. See the MAF "function tools with human-in-the-loop approvals" guide.

## Install

```bash
dotnet add package Foundgine.Providers.AgentFramework
```
