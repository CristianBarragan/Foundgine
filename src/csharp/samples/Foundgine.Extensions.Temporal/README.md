# Temporal + Keycloak integration sketches

These two projects are **integration sketches**, not a complete runnable application. They
show how a host API and a separate Temporal worker register the Foundgine Temporal
extension, process definitions, operations, and Keycloak warrant integration. The `Program.cs`
files intentionally contain placeholder comments for the application's Foundgine runtime,
semantic model, provider, database, event store, outbox and gateway registrations.

Do not use these files as a copy/paste runnable template until those host-owned services are
configured. For the extension contracts and security model, start with
[`Foundgine.Extensions.Temporal/README.md`](../../Foundgine.Extensions.Temporal/README.md)
and [`Foundgine.Extensions.Keycloak/README.md`](../../Foundgine.Extensions.Keycloak/README.md).

## Project roles

- `Temporal.Api`: API host sketch; registers process definitions, topic routing, outbox dispatch,
  approval policy and workflow endpoints.
- `Temporal.Worker`: worker sketch; registers process definitions, operations and Keycloak-backed
  worker authority.
- Shared process definitions are compiled into the API project through linked source files so the
  API starts workflows using the same definitions the worker executes.

## Configuration

The `appsettings.json` files contain local non-secret defaults. Set credentials and key paths
through environment variables or a secret manager. In particular, configure
`Foundgine__Keycloak__ServiceClientSecret` for the worker; no secret is committed here.

Before treating this as a runnable sample, add the application-specific registrations, a local
compose environment for Temporal and Keycloak, and an end-to-end verification test.
