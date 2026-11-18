# Foundgine.Extensions

The optional host integrations for Foundgine 3.x, shipped as one NuGet package (the counterpart of the `foundgine-extensions` Maven artifact):

- **GraphQL (Hot Chocolate):** translates GraphQL operations into Foundgine semantic intent and provides the secure query and mutation executors (`Foundgine.Extensions.GraphQL.HotChocolate`).
- **Temporal:** outbox-to-workflow bridge, saga and approval workflow, operation activities and start/status/decision/cancel endpoints (`Foundgine.Extensions.Temporal`).
- **Keycloak:** JWT validation, role and tenant mapping and issuance of short-lived signed Foundgine warrants, including for Temporal workers (`Foundgine.Extensions.Keycloak`).

Namespaces are unchanged. The package depends on `Foundgine.Core`, `Foundgine.Runtime` and `Foundgine.Providers`.

```bash
dotnet add package Foundgine.Extensions
```
