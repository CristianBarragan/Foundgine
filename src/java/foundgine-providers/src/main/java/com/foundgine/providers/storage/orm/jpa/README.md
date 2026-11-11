# Foundgine JPA provider

The JPA adapter is an execution target for Foundgine's provider-neutral semantic
runtime. Authorization, semantic planning, tenant isolation, cursor validation,
and mutation capability checks remain outside the JPA adapter.

## Typed repository

`JpaOrmRepository<T>` supplies mechanical CRUD for a single JPA entity. It is
intentionally not an authorization boundary. Use it only after the application
has authorized the requested capability. For semantic reads, use Foundgine's
`IFoundgine` execution pipeline and register the `orm-jpa` execution provider;
do not use repository predicates as a substitute for semantic authorization.

```java
try (EntityManager em = emf.createEntityManager()) {
    var customers = new JpaOrmRepository<>(em, Customer.class);
    Customer customer = customers.get(42L);
    long count = customers.count();
}
```

The class-based repository constructor is a compatibility path. For Native Image
and strict AOT use, construct it with the generated `JpaEntityModel<T>` produced by
`FoundgineAotProcessor`. That model provides direct construction and attribute
writers without reflective member access. The caller owns the `EntityManager`;
repository instances are not thread-safe. Writes join active resource-local
transactions and never commit a transaction they did not start.

JPA has no portable no-tracking query mode. `get` and `list` detach returned
entities; `query()` exposes the provider's normal managed query semantics so that
callers can deliberately compose provider-specific query operations.

## Semantic queries and generated metadata

`FoundgineQuery.typedQuery(...)` and `TypedQuery<T>` are provider-neutral semantic
query builders. They execute through the configured `IFoundgine` engine; JPA is
selected by the engine's `orm-jpa` provider registration. Java lambdas do not
expose C#-style expression trees, so Java's typed query uses semantic field and
relationship names. The semantic model validates them during planning.

`FoundgineAotProcessor` can emit `JpaEntityModel` direct-access models for
`@FoundgineOrmEntity` types. `OrmRegistry.create(...)` binds semantic metadata to
persistent Java classes and JPA attribute names. Hibernate Native Image
reachability metadata and native executable test evidence are not yet certified;
see `ORM-JPA-PARITY-PLAN.md` for the explicit remaining gates.
