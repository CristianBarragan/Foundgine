# Foundgine SupplyChain Advanced — Java

Java port of `samples/Foundgine.SupplyChain.Advanced`.

This module currently ports the provider-neutral advanced application contract:

- domain model;
- deterministic SupplyChain fixture;
- tenant/warehouse authorization context;
- recursive supplier-risk and fulfillment-planning scenarios;
- manual semantic model and application traversals;
- adversarial regression tests.

The manual semantic model is intentional until Java AOT metadata discovery is
ported. PostgreSQL/MCP/open-intent mutation and the complete high-assurance
execution path are subsequent milestones.
