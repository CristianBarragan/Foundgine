# Planning and execution

## Purpose

Planning turns an authorized semantic request into an explicit description of work. Execution translates that description into a provider operation and returns results plus evidence. The separation allows Foundgine to validate semantics before touching physical storage.

## Read path

A typical read path is:

1. Accept a structured or compiled intent.
2. Resolve semantic identities and validate fields, filters, sort order, pagination, and relationship paths.
3. Obtain an authorization decision for the caller, operation, and scope.
4. Build a semantic query/plan.
5. Lower the plan into an execution representation supported by the selected provider.
6. Run provider conformance checks and parameterize values.
7. Execute and map the provider result into semantic result types.
8. Emit diagnostics/audit evidence as configured.

## Mutation path

Mutations use a separate plan and lifecycle; see [Mutations](mutations). The plan captures operation kind, fields, dependencies, and effects. It should be validated before execution and bound to the authorization and approval context that permits it.

## SQL compilation

The SQL compiler is a provider concern. It receives a validated semantic plan and maps it to SQL plus parameter bindings. It must not accept caller-supplied SQL fragments as a substitute for semantic filters. Values should be parameterized; identifiers should come from trusted compiled mappings; authorization predicates must be included in the execution boundary.

## Provider conformance gate

A provider must demonstrate that it can preserve the semantics required by the plan. Examples include supported data types, aggregate behavior, null/empty semantics, pagination, transaction boundaries, and authorization enforcement. A capability flag is a declaration to validate, not a reason to skip request-level checks.

## Cursor and pagination behavior

Opaque cursors should be integrity-protected and bound to the query/scope that created them. A cursor from one tenant, principal, filter, or semantic contract must not be reusable in another context. Pagination must have explicit limits and stable ordering requirements.

## Cost and resource limits

Planning and execution should enforce bounded candidate counts, path exploration, execution time, result sizes, and mutation batch sizes. Limits are security and reliability controls, not merely performance tuning.

## Result semantics

Results should preserve semantic field identities and types rather than leak raw provider-specific row shapes. Errors should distinguish invalid intent, unresolved references, denied operations, unsupported provider semantics, and execution failures without exposing secrets or internal SQL unnecessarily.

## Where to look in source

- C#: `src/csharp/Foundgine.Core/Semantic/Query`, `Semantic/Expressions`, `Semantic/Aggregates`, `src/csharp/Foundgine.Runtime/Execution`, `src/csharp/Foundgine.Providers/Storage/Sql`.
- Java: `foundgine-core` semantic query/planning packages, `foundgine-runtime` execution, and `foundgine-providers` storage implementations.
- Existing guides: [Runtime](../RUNTIME), [Architecture](../ARCHITECTURE), [Standards](../STANDARDS).
