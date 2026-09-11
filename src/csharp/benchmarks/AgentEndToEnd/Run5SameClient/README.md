# Run 5 Same Client

This benchmark is **rebased directly on the working Run 5 client**.

## What changed

Nothing about the MCP transport/client implementation was replaced with `ModelContextProtocol.Client`.

The exact Run 5 client is reused:

- one shared `HttpClient`
- `SocketsHttpHandler`
- Streamable HTTP `/mcp`
- manual JSON-RPC `tools/call`
- `MCP-Protocol-Version: 2025-06-18`
- the same retry, framing, and response validation code

The only experimental change is the **logical operation presented to each endpoint**.

### Conventional

The client sends 8 identical `transfer_funds` MCP calls for one logical task.

### Foundgine

The same client sends 1 `transfer_funds_batch` MCP call containing the same 8 logical transfers.

Therefore:

```text
                    SAME CLIENT
                         |
              +----------+----------+
              |                     |
          EF Core               Foundgine
              |                     |
        8 MCP calls             1 MCP call
              |                     |
          8 transfers            8 transfers
```

## Why this version exists

Previous `Run5SameClient` attempts introduced `ModelContextProtocol.Client.McpClient`. Those attempts failed during MCP initialization because the SDK's discovery/handshake behavior is not identical to the proven Run 5 manual transport.

This benchmark deliberately does **not** introduce a new MCP client implementation. It reuses the known-good Run 5 client code so the experiment isolates the execution capability rather than client compatibility.

## Smoke test

```powershell
.\run-run5-same-client.ps1 -CustomerCounts 10 -Concurrency 8 -RunsPerTier 5 -Warmups 2
```

The script still accepts the original Run 5 matrix arguments.

## Measurements

Each task contains the same number of logical transfers. The runner records:

- logical operations
- MCP/tool calls
- request bytes
- response bytes
- total task payload
- wall-clock latency
- p50/p95/p99
- logical operations/sec
- success/failure

The report is written as `run5-same-client-metadata.json`.

## Important interpretation

This test does **not** claim that batching is free. It measures the effect of giving the same client a semantic batch capability at the MCP boundary.

It is complementary to the original Run 5 throughput benchmark.


## Payload interpretation

A batch call is expected to have a larger individual MCP request because it
carries multiple logical operations in one request. Therefore a negative value
must not be described as a "payload reduction".

The runner reports:

1. average payload per MCP call;
2. total payload per logical task;
3. payload per logical operation.

The primary batching signal is MCP/tool-call reduction. The payload metrics are
supporting measurements and make the cost of carrying the larger batch explicit.
