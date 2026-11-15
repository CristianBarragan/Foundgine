# @foundgine/typescript

A thin TypeScript client for Foundgine MCP and Temporal workflow HTTP transports

## Install

```sh
npm install @foundgine/typescript
```

## MCP client

```ts
import { FoundgineClient } from "@foundgine/typescript";

const client = new FoundgineClient({
  baseUrl: "https://example.invalid",
  token: async () => process.env.FOUNDGINE_TOKEN,
});

try {
  const tools = await client.listCapabilities();
  const result = await client.execute("query_semantic", { entity: "Customer", limit: 20 });
  console.log(tools, result.content, result.isError);
} finally {
  await client.close();
}
```

`token` may be a string or a sync/async provider; it is resolved for every request. Redirects are not followed. The SDK uses the supplied `fetch` implementation when provided, otherwise the platform `fetch`. The caller owns custom `fetch` resources.

## Notifications

```ts
const controller = new AbortController();
for await (const message of client.notifications({ signal: controller.signal })) {
  console.log(message);
}
```

The notification stream resumes using `Last-Event-ID`. Stop it with the supplied `AbortSignal`. If you break early, call `return()` on the async iterator (or use `try`/`finally`) so the stream is released.

## Temporal workflows

The workflow client follows the HTTP routes exposed by Foundgine Temporal:

```ts
const { started, watch } = await client.workflows.startAndWatch(
  "order-approval",
  "order-1042",
  { orderId: "order-1042" },
  { intervalMs: 1_000, timeoutMs: 120_000 },
);
console.log(started.workflowId);
for await (const status of watch.updates()) {
  console.log(status.temporalStatus);
}
const finalStatus = await watch.done;
```

The catalog can also be inspected before starting work:

```ts
const names = await client.workflows.processes();
const definition = await client.workflows.describe(names[0]!);
console.log(definition.steps.map((step) => [step.name, step.requiresApproval]));
```

Also available: `processes`, `describe`, `start`, `status`, `decide`, `cancel`, `waitForCompletion`, and `watch`. Catalog descriptions expose safe step metadata only; internal operation identifiers are not returned by the server. `watch()` returns immediately; its `updates()` iterator replays distinct observed states to late consumers. Call `stop()` to abort polling. `onUpdate` and `onError` callback failures are isolated from the polling loop. Process and business-key path segments are percent-encoded. `decide` has no caller-identity field: the server stamps the decision subject from its trusted security context.

## Status and known limits

Implemented and tested: typed errors, SSE parser, MCP session initialization, JSON-RPC requests, paged capability listing (`listCapabilities`), tool execution (`execute`), cancellation, resumable notification streaming, Temporal start/status/decision/cancel routes, `waitForCompletion`, `watch`, `startAndWatch`, and the workflow catalog (`processes()`, `describe()`).

Not yet done or verified: exhaustive shared-vector coverage for the SDK, browser/Web Worker/Service Worker execution checks, and package installation from the npm registry in the offline authoring environment. This is a development package, not a release-ready SDK.