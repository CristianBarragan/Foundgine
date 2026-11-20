# Foundgine vs Hot Chocolate + EF Core: one small real app, two implementations

Same Postgres schema, same GraphQL-only API, same plain JWT bearer authentication, same authorization rules.

> **Not compiled or run.** The session that produced this had no .NET SDK and no NuGet access. Treat both apps as
> unbuilt source. `tools/parity.py` is the acceptance test: run it once both apps start.

## The app
Customers, Orders, OrderLines, Products in a legacy-style schema (`tbl_order.ord_total`, `qty`, `cust_id` ...).
The API models use different names (`Order.Total`, `OrderLine.Quantity`, `Product.Name`). Read-only GraphQL; roots
`customer`, `order`, `product`, `orderLine`, with `where` / `order` arguments and nested selections between models.

Authorization (identical in both): JWT HS256, issuer `https://auth.local`, audience `shop-api`, claims `role`,
`tenant_id` (required), `customer_id` (customers).
- `manager`: reads everything.
- `customer`: only their own Customer row and Orders (from the `customer_id` claim, never from GraphQL input); may read
  Products and OrderLines through their own orders; may not start a query at `orderLine`.
- no/invalid/expired token: 401; missing `tenant_id`: denied.

## Run
```bash
docker compose up -d                                  # postgres on :5433, schema + seed from db/init.sql
dotnet run --project Foundgine.Shop    --urls http://localhost:5001
dotnet run --project HotChocolate.Shop --urls http://localhost:5002
python3 tools/parity.py http://localhost:5001 http://localhost:5002
python3 tools/mint_token.py manager       # tokens for curl: Authorization: Bearer <token>
```

## How "entities differ from models" is handled (read this)
| | Foundgine.Shop | HotChocolate.Shop |
|---|---|---|
| Model classes | `Domain/Models.cs` | `Models.cs` |
| Storage/EF entities | **same classes**; DB shape declared in `Domain/ShopModel.cs` (tables, columns, FKs) | separate `*Row` classes in `Data/Entities.cs` |
| Model<->entity mapping | declarative column naming, no mapping code | hand-written `Select` projections in `Mappings.cs` |

Foundgine's metadata supports a separate storage class (`FoundgineEntityMap<Model, TEntity>`), but its **EF provider
registers the model class itself as the EF entity** (`FluentEfModel` uses `EntityMetadata.ClrType` = model type), and no
test or sample executes through a separate storage type. So the Foundgine side uses the supported path. You chose
"dynamic querying between models" as what matters, which is what the comparison below stresses.

## Findings
| | Foundgine.Shop | HotChocolate.Shop |
|---|---|---|
| Non-blank, non-comment C# lines in the sample app | **234** | **113** |
| Dynamic nested selection | GraphQL adapter + reusable HTTP endpoint and result assembler from `Foundgine.Extensions.GraphQL` | built in |
| Filtering | `where` with eq/neq/in, and/or, **collection** filters via count/min/max | `UseFiltering`: richer operators, to-one and `some/all/none` filters |
| Sorting | `order` (also across relationships) | `UseSorting` |
| Cyclic selections (customer->orders->customer) | fine | EF cannot translate recursive projections: each root needs its own acyclic mapping (`Mappings.cs`) |
| JWT bearer | needs a bridge: claims -> signed warrant (`Auth.cs`, uses the Keycloak extension's issuer only) | native |
| Row-level security | one predicate per entity, enforced at **every** level of the query plan, including nested | resolver `Where` on the root only; safe here only because nested data hangs off already-scoped roots |
| Pagination (`first`) | supported | omitted so both accept the same query text |

The LOC count above measures only the two sample applications: all their non-blank, non-comment `.cs` lines. It
does not allocate shared library code to the Foundgine sample. The HTTP endpoint and response assembler now live in
`Foundgine.Extensions.GraphQL/Http/GraphQLHttpEndpoint.cs` (268 non-blank, non-comment C# lines in this revision);
the sample configures and maps that endpoint through `MapFoundgineGraphQL(ShopGraphQL.Configure)`. Those 268 lines
are reusable extension code, not application code, and are therefore excluded from the 234-line sample count.

Honest reading: on this small app Foundgine still has more application code than the Hot Chocolate implementation
(234 vs 113 lines), but the old ~235-line, sample-owned HTTP endpoint/result-assembly cost has moved into a reusable
extension. Its advantages are structural, not simply LOC: one authorization boundary enforced inside the query plan
(so new relationships can't leak rows), model-driven relationship filters and ordering without writing resolvers, and
one declaration shared by other interfaces (MCP etc.). Its remaining costs here are the JWT-claims-to-warrant bridge
and the model==entity limitation above. The shared endpoint does not make the sample's LOC directly comparable to
the total implementation cost of the two frameworks; it makes the ownership boundary explicit.

## Caveats
- Hot Chocolate `UseProjection` is not used: composing it over already-projected DTOs is unverified. Without it every mapped
  column and nested collection is fetched regardless of the selection set, so this is **not** a performance comparison.
- Warrant signing uses an ephemeral in-process key plus in-memory replay/revocation stores: single instance only.
- The Foundgine HTTP endpoint and nested response assembly are supplied by the reusable `Foundgine.Extensions.GraphQL`
  project; the sample owns its GraphQL root-role and claim-authorization configuration, not the HTTP handler.
- The cross-model filter dialects differ (`lines:{quantity:{max:{gt:1}}}` vs `lines:{some:{quantity:{gt:1}}}`); `parity.py` handles this.
