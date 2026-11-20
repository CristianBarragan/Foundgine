# Foundgine vs Hot Chocolate + EF Core

One small shop application, one legacy PostgreSQL schema, two implementations.

The purpose of this comparison is not to claim that Foundgine is a drop-in replacement for EF Core or that it wins every line-count or feature comparison. It demonstrates a different architecture for describing, authorizing and executing application data capabilities.

## What is held constant

- PostgreSQL schema and seed data (`db/init.sql`).
- GraphQL-only API surface and equivalent shop concepts.
- JWT HS256 issuer, audience and claims.
- Manager/customer access rules.
- Query parity cases in `tools/parity.py`.

Customers, Orders, OrderLines and Products live in a legacy-style schema (`tbl_order.ord_total`, `qty`, `cust_id`, etc.). API model names differ from several storage column names.

Authorization rules:

- `manager`: may read all rows.
- `customer`: may read only their own Customer row and Orders scoped from the `customer_id` claim; may reach Products and OrderLines through their own Orders; may not start at `orderLine`.
- Missing, invalid or expired JWT: HTTP 401.
- Missing `tenant_id`: denied.

## Run

```bash
docker compose up -d
dotnet run --project Foundgine.Shop --urls http://localhost:5001
dotnet run --project HotChocolate.Shop --urls http://localhost:5002
python3 tools/parity.py http://localhost:5001 http://localhost:5002
python3 tools/mint_token.py manager
```

## The Foundgine configuration model

Foundgine's model is configured fluently and in separate, composable declarations. The sample's `Domain/ShopModel.cs` registers four concerns:

| Configuration | What it describes | Example in this sample |
|---|---|---|
| Semantic | Application entities and navigable relationships | `Customer.Orders -> Order`, `Order.Lines -> OrderLine` |
| Persistence | Tables, keys and legacy column names | `Order -> tbl_order`, `Total -> ord_total` |
| Mapping | Model-member to storage-member mapping | `IdentityEntityMap<T>` where the sample uses the same CLR type |
| Connections | Foreign-key direction and relationship joins | `Order.CustomerId -> Customer.Id` |

The configuration is not GraphQL resolver code. It describes the model and its storage relationships independently of the protocol. The sample uses EF Core as its persistence implementation; Foundgine does not require replacing the existing database.

Example:

```csharp
internal sealed class OrderTable : FoundginePersistenceConfiguration<Order>
{
    public override void Configure(FoundginePersistenceBuilder<Order> m)
    {
        m.ToTable("tbl_order").HasKey(x => x.Id);
        m.Property(x => x.Id).HasColumnName("ord_id");
        m.Property(x => x.CustomerId).HasColumnName("cust_id");
        m.Property(x => x.Total).HasColumnName("ord_total");
    }
}
```

This is the central configuration story: application meaning, persistence mapping and relationships are explicit rather than being scattered across protocol resolvers.

## What the comparison shows

| Capability | Foundgine.Shop | HotChocolate.Shop |
|---|---|---|
| Nested selections | GraphQL adapter and reusable endpoint/result assembly | Built-in GraphQL execution |
| Filtering | Model-derived `where`, including collection filters | `UseFiltering`; richer built-in operators including `some/all/none` |
| Sorting | Model-derived `order`, including relationship traversal | `UseSorting` |
| Cyclic model selections | Supported by the Foundgine query path in this sample | EF projection mapping is kept acyclic per root |
| JWT bearer | Claims are bridged to a signed Foundgine warrant | Native ASP.NET authentication integration |
| Row-level policy | One entity predicate is enforced throughout the query plan, including nested traversal | Root resolver scoping; nested fields rely on already-scoped roots in this sample |
| Pagination | `first` supported | Omitted so the comparison uses shared query text |

The baseline is not inherently insecure. Its resolver scoping is valid for the demonstrated relationship graph. The architectural distinction is that Foundgine applies policy as part of plan execution at each relevant entity boundary, reducing the risk that a future relationship or alternate caller bypasses a resolver-level convention.

## Honest cost and limitations

The measured sample application code in this checkpoint is:

| Measure | Foundgine.Shop | HotChocolate.Shop |
|---|---:|---:|
| Non-blank, non-comment C# lines | 234 | 113 |

Foundgine has more sample application code in this small example. Do not present this as a LOC reduction. The HTTP endpoint and response assembler are reusable code in `Foundgine.Extensions.GraphQL`, not sample-owned code; excluding shared library code makes the ownership boundary visible but does not make it free.

Other caveats:

- Hot Chocolate's built-in filtering surface is richer than the operators used here.
- The sample's Foundgine EF provider uses the model CLR types as EF entities. This sample does not prove a separate storage CLR type for the EF provider.
- Hot Chocolate projection is not enabled; this is not a query-performance comparison.
- The warrant signing key and replay store are in-memory/ephemeral and intended for this local sample only. Use durable shared key and replay infrastructure for multi-instance production deployments.
- The filter dialects differ for collection predicates; `tools/parity.py` accounts for the equivalent cases.

## The point of the comparison

Foundgine's proposition is not "fewer lines" or "replace your ORM". It is a reusable semantic and execution boundary:

1. Configure what application entities mean and how they relate.
2. Configure how that model maps to existing storage.
3. Apply authorization as part of execution, not as an unrelated convention in each resolver.
4. Let different interfaces consume the same model and policy instead of reimplementing data access rules for each protocol.

The current sample demonstrates GraphQL. MCP and additional providers are natural next demonstrations, but should only be claimed as proven by this comparison once wired to the same model and covered by parity tests.
