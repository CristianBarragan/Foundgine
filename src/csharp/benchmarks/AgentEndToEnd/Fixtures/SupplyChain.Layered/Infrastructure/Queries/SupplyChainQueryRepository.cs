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
using Foundgine.Generated;
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
        var operation = Read(GeneratedSemanticModel.SalesOrder.Entity, GeneratedSemanticModel.SalesOrder.All,
            GeneratedSemanticModel.SalesOrder.CustomerId.Eq(customerId),
            [GeneratedSemanticModel.SalesOrder.Id.Asc()]);
        var result = await _sql.ExecuteAsync(operation, ct);
        return new { customerId, orders = result.Rows.Select(x => x.Values).ToArray(), plan = result.Fingerprint };
    }

    public async Task<object> GetOrder(int customerId, int orderId, CancellationToken ct)
    {
        var line = new SemanticReadNode(2, GeneratedSemanticModel.SalesOrderLine.Entity,
            GeneratedSemanticModel.SalesOrderLine.All,
            SupplyChainSemanticConfiguration.OrderLines, null, []);
        var filter = new SemanticAndFilter([
            GeneratedSemanticModel.SalesOrder.Id.Eq(orderId),
            GeneratedSemanticModel.SalesOrder.CustomerId.Eq(customerId)
        ]);
        var operation = new SemanticOperation(new SemanticReadNode(1, GeneratedSemanticModel.SalesOrder.Entity,
            GeneratedSemanticModel.SalesOrder.All, null, null, [line],
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
            GeneratedSemanticModel.Shipment.Id.Eq(shipmentId),
            new SemanticRelationshipFilter(SupplyChainSemanticConfiguration.ShipmentOrder,
                SemanticRelationshipQuantifier.Some,
                GeneratedSemanticModel.SalesOrder.CustomerId.Eq(customerId))
        ]);
        var result =
            await _sql.ExecuteAsync(
                Read(GeneratedSemanticModel.Shipment.Entity, GeneratedSemanticModel.Shipment.All, filter), ct);
        var row = result.Rows.FirstOrDefault() ?? throw new KeyNotFoundException("Shipment not found.");
        return new { shipment = row.Values, plan = result.Fingerprint };
    }

    public Task<object> ListProducts(CancellationToken ct) => Query(GeneratedSemanticModel.CatalogProduct.Entity,
        GeneratedSemanticModel.CatalogProduct.All, "products", ct);

    public Task<object> ListCustomers(CancellationToken ct) => Query(GeneratedSemanticModel.Customer.Entity,
        GeneratedSemanticModel.Customer.All, "customers", ct);

    public Task<object> ListSuppliers(CancellationToken ct) => Query(GeneratedSemanticModel.Supplier.Entity,
        GeneratedSemanticModel.Supplier.All, "suppliers", ct);

    public Task<object> GetProduct(int productId, CancellationToken ct) => ExecuteNamed(
        Read(GeneratedSemanticModel.CatalogProduct.Entity, GeneratedSemanticModel.CatalogProduct.All,
            GeneratedSemanticModel.CatalogProduct.Id.Eq(productId)), "product", ct);

    public Task<object> GetInventory(int productId, CancellationToken ct) => ExecuteNamed(
        Read(GeneratedSemanticModel.InventoryPosition.Entity, GeneratedSemanticModel.InventoryPosition.All,
            GeneratedSemanticModel.InventoryPosition.ProductId.Eq(productId),
            [GeneratedSemanticModel.InventoryPosition.WarehouseId.Asc()]), "inventory", ct, productId);

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