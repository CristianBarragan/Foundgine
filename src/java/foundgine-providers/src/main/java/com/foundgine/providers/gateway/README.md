# Foundgine Java Gateway — Increment 1

This increment adds the Java 27 gateway/provider foundation in `foundgine-providers`.

## Included

- Immutable gateway discovery contracts and facade (`IFoundgineGateway`, `FoundgineGateway`).
- Capability catalog with case-insensitive duplicate detection.
- Provider identity and allocation-light `long` capability flags.
- Provider registry with health reporting, deterministic snapshots and lifecycle publication.
- Rollback of registration, health and unregistration mutations when lifecycle publication fails.
- Provider capability negotiation. A single provider must satisfy each complete requirement; capabilities are never combined across providers.
- JUnit parity tests for discovery, registration, rollback and negotiation.

## Example

```java
var gatewayOptions = new FoundgineGatewayOptions()
    .name("Supply Chain Gateway")
    .contractVersion("1.0")
    .addCapability(new FoundgineGatewayCapability(
        "orders.read", "Order read", "1.0", "query",
        "Read order data", List.of("get", "list"), Map.of()));

IFoundgineGateway gateway = new FoundgineGateway(gatewayOptions);
var discovery = gateway.describe();

IFoundgineGatewayEventPublisher events = (type, subject, correlationId, providerId, attributes) -> {
    // Connect to the application's event pipeline. This increment defines the stable contract.
};
var registry = new FoundgineGatewayProviderRegistry(events);
registry.register(new FoundgineGatewayProviderDescriptor(
    "foundgine.postgres.eventstore", "PostgreSQL Event Store", "1.0",
    List.of("DURABLE_STORAGE", "ORDERED_EVENTS")));
registry.reportHealth("foundgine.postgres.eventstore",
    FoundgineGatewayProviderHealth.HEALTHY, "Ready");
```

Provider negotiation is explicit and does not grant execution authority:

```java
var requirement = new FoundgineProviderRequirement(
    FoundgineProviderKind.EVENT_STORE,
    FoundgineProviderCapability.mask(
        FoundgineProviderCapability.DURABLE_STORAGE,
        FoundgineProviderCapability.ORDERED_EVENTS),
    "The configured journal must be durable and ordered");

FoundgineProviderNegotiator.negotiate(providers, List.of(requirement))
    .throwIfUnsatisfied();
```

## Deliberate boundaries

This increment does **not** add persistence, replay, HTTP endpoints, approvals, execution, or authorization policy. The event publisher is an integration contract; durable event storage and streaming belong to the next increment. The registry is discovery/lifecycle metadata only and must not be treated as an authorization decision or a substitute for tenant-scoped execution checks.

## Verification

Production sources compile with `javac --release 27`. Maven was not available in the implementation environment, so the Maven/JUnit suite could not be executed here.
