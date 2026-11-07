# Agent benchmark concurrency resilience

The end-to-end benchmark is designed to distinguish application saturation from benchmark-client socket exhaustion.

## HTTP client

Run 1, Run 2 and Run 3 use a shared `HttpClient` backed by `SocketsHttpHandler` rather than creating an `HttpClient` per GraphQL request. The handler uses connection pooling, a bounded `MaxConnectionsPerServer` of 128, pooled connection lifetime/idle timeouts, and a connect timeout.

## Transient transport failures

GraphQL HTTP requests retry transient `HttpRequestException` failures up to four attempts with exponential backoff (50/100/200 ms). Retries are counted in the report. A worker failure no longer tears down the entire Run 2 concurrency batch; failed workers are recorded with an error class/message so the benchmark can continue and report success rate.

## Concurrency telemetry

Run 2 reports configured concurrency, worker count, success/failure count, RPS, p50/p95/p99/max wall time, tool calls, estimated token load, peak active HTTP requests and retry count.

## Docker telemetry

The Docker sampler records CPU %, memory usage/limit, memory %, network RX/TX, block I/O and PIDs for PostgreSQL and the Foundgine warm service. The summary additionally estimates CPU-seconds and memory GB-seconds from timestamped samples. These are resource-efficiency metrics, not direct watt-hour measurements.

## Interpretation

A C=64 result that remains slow after client-side pooling is fixed is evidence worth investigating inside Foundgine/PostgreSQL. A socket exhaustion result before this fix can instead measure the benchmark harness's connection-management behavior rather than Foundgine itself.
