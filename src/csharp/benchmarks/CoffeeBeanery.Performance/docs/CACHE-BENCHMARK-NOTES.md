# Provider-plan cache benchmark notes

The warm provider-plan cache is intended to remove repeated provider compilation, not to make every workload faster by definition. For very small plans, compilation can be cheap enough that cache lookup overhead is visible.

The benchmark cache path was tightened in three ways:

- cache hits no longer perform a capacity trim scan; trimming happens only when an entry is added
- the warm API uses the cache single-flight `GetOrAdd` path instead of `TryGet` followed by `Set`
- concurrent requests for the same uncached plan share one `Lazy<ProviderPlan>` compilation

The cache still stores only provider execution plans. Semantic resolution and authorization remain request-scoped.

The benchmark also separates warm-up from measurement. Warm-up is diagnostic and does not prevent measurement from starting. Requests still in flight at the measurement deadline are reported as `drained` and excluded from measured RPS/latencies.

## Interpreting cache results

If `Foundgine - no cache` remains faster than `Foundgine - provider-plan cache` at low concurrency, that is not automatically a cache defect. It can mean the provider compilation cost is smaller than the cache lookup/fingerprint cost for that workload. The important comparison is whether the warm path improves under realistic concurrency and/or with more expensive provider compilation.
