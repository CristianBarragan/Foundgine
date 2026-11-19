# MCP consolidation status

The canonical semantic MCP API is located at `Semantic/Api/Mcp`. It exposes `/mcp`, `/intent`, `/intent/explain`, `/intent/capabilities`, `/health`, and `/health/ready`.

`/health/ready` was restored to the canonical host because the Advanced sample agent and startup scripts use it. This canonical host is backed by the sample's in-memory intent service; its readiness indicates host/service initialization. The PostgreSQL-backed benchmark host retains its database readiness check.

The PostgreSQL benchmark host is a separate sibling project at `../Foundgine.SupplyChain.Advanced.Benchmark/`. It is not part of the Advanced sample solution or its canonical semantic API. No consolidation is intended.

Validation in this environment: source-level route check and ZIP integrity check only. .NET build and runtime tests were not available, so no build/test success is claimed.
