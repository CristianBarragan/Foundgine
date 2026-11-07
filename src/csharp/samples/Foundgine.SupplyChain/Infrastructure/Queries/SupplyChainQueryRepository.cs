using System.Security.Cryptography;
using System.Text;
using Foundgine.Core.Abstractions;
using Foundgine.Providers.Aot;
using Foundgine.Core.Execution;
using Foundgine.Core.Execution.Mutation;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;
using Foundgine.Providers.Storage.Sql.Mutation;
using Foundgine.SupplyChain.Application;
using Foundgine.SupplyChain.Domain;
using Foundgine.Core.Semantic.IR;
using Npgsql;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.SupplyChain.Infrastructure.Queries;

public sealed class SupplyChainQueryRepository : ISupplyChainQueries
{
    private readonly SemanticSqlQueryExecutor _sql;
    public SupplyChainQueryRepository(SemanticSqlQueryExecutor sql) => _sql = sql;

    public async Task<object> GetOrders(int customerId, CancellationToken ct)
    {
        var operation = Read(SupplyChainSemantics.SalesOrder.Entity, SupplyChainSemantics.SalesOrder.All,
            SupplyChainSemantics.SalesOrder.CustomerId.Eq(customerId),
            [SupplyChainSemantics.SalesOrder.Id.Asc()]);
        var result = await _sql.ExecuteAsync(operation, ct);
        return new { customerId, orders = result.Rows.Select(x => x.Values).ToArray(), plan = result.Fingerprint };
    }

    public async Task<object> GetOrder(int customerId, int orderId, CancellationToken ct)
    {
        var line = new SemanticReadNode(2, SupplyChainSemantics.SalesOrderLine.Entity,
            SupplyChainSemantics.SalesOrderLine.All,
            SupplyChainSemantics.SalesOrder.Relationships.Lines, null, []);
        var filter = new SemanticAndFilter([
            SupplyChainSemantics.SalesOrder.Id.Eq(orderId),
            SupplyChainSemantics.SalesOrder.CustomerId.Eq(customerId)
        ]);
        var operation = new SemanticOperation(new SemanticReadNode(1, SupplyChainSemantics.SalesOrder.Entity,
            SupplyChainSemantics.SalesOrder.All, null, null, [line],
            new SemanticQueryOptions(filter)));
        var result = await _sql.ExecuteAsync(operation, ct);
        var row = result.Rows.FirstOrDefault() ?? throw new KeyNotFoundException("Sales order not found.");
        return new
        {
            order = row.Values, lines = result.Rows.Select(x => x.Values).ToArray(), plan = result.Fingerprint
        };
    }

    public async Task<object> GetShipment(int customerId, int shipmentId, CancellationToken ct)
    {
        var filter = new SemanticAndFilter([
            SupplyChainSemantics.Shipment.Id.Eq(shipmentId),
            new SemanticRelationshipFilter(SupplyChainSemantics.Shipment.Relationships.Order,
                SemanticRelationshipQuantifier.Some,
                SupplyChainSemantics.SalesOrder.CustomerId.Eq(customerId))
        ]);
        var result =
            await _sql.ExecuteAsync(
                Read(SupplyChainSemantics.Shipment.Entity, SupplyChainSemantics.Shipment.All, filter), ct);
        var row = result.Rows.FirstOrDefault() ?? throw new KeyNotFoundException("Shipment not found.");
        return new { shipment = row.Values, plan = result.Fingerprint };
    }

    public Task<object> ListProducts(CancellationToken ct) => Query(SupplyChainSemantics.CatalogProduct.Entity,
        SupplyChainSemantics.CatalogProduct.All, "products", ct);

    public Task<object> ListCustomers(CancellationToken ct) => Query(SupplyChainSemantics.Customer.Entity,
        SupplyChainSemantics.Customer.All, "customers", ct);

    public Task<object> ListSuppliers(CancellationToken ct) => Query(SupplyChainSemantics.Supplier.Entity,
        SupplyChainSemantics.Supplier.All, "suppliers", ct);

    public Task<object> GetProduct(int productId, CancellationToken ct) => ExecuteNamed(
        Read(SupplyChainSemantics.CatalogProduct.Entity, SupplyChainSemantics.CatalogProduct.All,
            SupplyChainSemantics.CatalogProduct.Id.Eq(productId)), "product", ct);

    public Task<object> GetInventory(int productId, CancellationToken ct) => ExecuteNamed(
        Read(SupplyChainSemantics.InventoryPosition.Entity, SupplyChainSemantics.InventoryPosition.All,
            SupplyChainSemantics.InventoryPosition.ProductId.Eq(productId),
            [SupplyChainSemantics.InventoryPosition.WarehouseId.Asc()]), "inventory", ct, productId);

    private static SemanticOperation Read(EntityId entity, IReadOnlyList<FieldId> fields,
        SemanticFilterExpression? filter = null, IReadOnlyList<SemanticOrderTerm>? order = null) =>
        new(new SemanticReadNode(1, entity, fields, null, null, [], new SemanticQueryOptions(filter, order ?? [])));

    private async Task<object> Query(EntityId entity, IReadOnlyList<FieldId> fields, string name, CancellationToken ct)
    {
        var result = await _sql.ExecuteAsync(Read(entity, fields), ct);
        return new Dictionary<string, object?>
            { [name] = result.Rows.Select(x => x.Values).ToArray(), ["plan"] = result.Fingerprint };
    }

    private async Task<object> ExecuteNamed(SemanticOperation operation, string name, CancellationToken ct,
        object? extra = null)
    {
        var result = await _sql.ExecuteAsync(operation, ct);
        var dict = new Dictionary<string, object?>
            { [name] = result.Rows.FirstOrDefault()?.Values, ["plan"] = result.Fingerprint };
        if (extra is not null) dict["productId"] = extra;
        return dict;
    }
}
