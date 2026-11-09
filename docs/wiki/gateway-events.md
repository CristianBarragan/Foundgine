# Gateway, facade, events and live updates

## Purpose

The Foundgine Gateway is a stable facade through which external products and callers can discover and exercise configured Foundgine capabilities. It avoids requiring every consumer to know internal runtime, storage, authorization, or provider implementation details.

## Gateway responsibilities

- Expose stable read and mutation contracts over supported transports.
- Resolve a provider through a registry/negotiation mechanism.
- Validate provider startup configuration and declared capabilities.
- Apply authentication/authorization and scope propagation at the boundary.
- Route execution through Foundgine rather than duplicating business/data access logic.
- Provide diagnostics, audit integration, approvals, and event visibility.

The gateway is not a replacement for the semantic model or authorization system. It is an application-facing boundary that delegates to them.

## Provider registration and negotiation

A provider describes which capabilities it implements. Startup validation should detect missing or conflicting registrations early. Negotiation chooses a compatible provider for a request; it must not silently select a provider that cannot preserve the request's required semantics or security guarantees.

## Events and instant updates

Gateway events can communicate that a capability execution or relevant state transition occurred. Event streams should define ordering, identity, visibility, retention, replay behavior, and scope binding. A consumer may use events to refresh a UI or trigger downstream work, but should fetch authoritative state through an authorized read when correctness requires it.

## Visibility and scope digests

Event visibility must be evaluated against trusted caller scope. If scope digests are persisted, their keying strategy, rotation, and compatibility behavior matter. Unkeyed hashes of predictable tenant/subject values can be guessable; keyed digests reduce that exposure but require operational key management.

## Outbox and delivery

The outbox pattern records an event in the same durable transaction as the state change, then dispatches it asynchronously. Dispatch is generally at-least-once, so consumers should be idempotent and deduplicate using stable event identifiers. Do not promise exactly-once delivery across independent systems without a concrete protocol guaranteeing it.

## HTTP and serialization

Gateway contracts should be explicit and versionable. In .NET AOT deployments, source-generated JSON metadata is important for trimming-safe serialization. Transport adapters should map wire contracts to semantic contracts and avoid leaking provider-specific persistence models.

## Where to look in source

- C#: `Foundgine.Providers/Gateway`, `Foundgine.Providers/Outbox`, `Foundgine.Providers/EventStore`, gateway sample.
- Java: `foundgine-providers/.../gateway`, HTTP routing, event publisher and related event/outbox packages.
- See also: [Architecture](../ARCHITECTURE.md), [Runtime](../RUNTIME.md), [Testing](../TESTING.md).
