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
using Npgsql;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.SupplyChain.Infrastructure.Mutations;

/// <summary>
/// Supply-chain mutation adapter. Simple mutations are expressed semantically and
/// lowered through Foundgine.Core.Semantic.Planning.Mutation + Foundgine.Providers.Storage.Sql.Mutation. The two
/// high-assurance workflows retain explicit transaction orchestration because they
/// combine idempotency, locking, conditional inventory allocation and evidence.
/// </summary>
public sealed class SupplyChainMutationRepository : ISupplyChainMutations
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IMetadataProvider _metadata;

    public SupplyChainMutationRepository(NpgsqlDataSource dataSource, IMetadataProvider metadata)
    {
        _dataSource = dataSource;
        _metadata = metadata;
    }

    public Task<object> UpdateInventory(int warehouseId, int productId, int quantity, CancellationToken ct)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var filter = new SemanticAndFilter([
            GeneratedSemanticModel.InventoryPosition.WarehouseId.Eq(warehouseId),
            GeneratedSemanticModel.InventoryPosition.ProductId.Eq(productId)
        ]);
        var operation = SemanticMutationBuilder.Update(
            GeneratedSemanticModel.InventoryPosition.Entity,
            [GeneratedSemanticModel.InventoryPosition.QuantityOnHand.Set(quantity)], filter,
            GeneratedSemanticModel.InventoryPosition.All);
        return ExecuteSemantic(operation, result => new
        {
            warehouseId, productId, quantity,
            result
        }, ct);
    }

    public Task<object> CreateShipment(int orderId, int carrierId, int warehouseId, string trackingNumber,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber)) throw new ArgumentException("Tracking number is required.");
        var operation = SemanticMutationBuilder.Create(
            GeneratedSemanticModel.Shipment.Entity,
            [
                GeneratedSemanticModel.Shipment.OrderId.Set(orderId),
                GeneratedSemanticModel.Shipment.CarrierId.Set(carrierId),
                GeneratedSemanticModel.Shipment.WarehouseId.Set(warehouseId),
                GeneratedSemanticModel.Shipment.TrackingNumber.Set(trackingNumber),
                GeneratedSemanticModel.Shipment.Status.Set("In Transit")
            ], GeneratedSemanticModel.Shipment.All);
        return ExecuteSemantic(operation, result => new { shipment = result }, ct);
    }

    public Task<object> UpdateShipment(int shipmentId, string status, CancellationToken ct)
    {
        var allowed = new[] { "In Transit", "Out for Delivery", "Delivered", "Delayed" };
        if (!allowed.Contains(status, StringComparer.Ordinal))
            throw new InvalidOperationException("Invalid shipment status.");
        var operation = SemanticMutationBuilder.Update(
            GeneratedSemanticModel.Shipment.Entity,
            [GeneratedSemanticModel.Shipment.Status.Set(status)],
            GeneratedSemanticModel.Shipment.Id.Eq(shipmentId),
            [GeneratedSemanticModel.Shipment.Id.Id, GeneratedSemanticModel.Shipment.Status.Id]);
        return ExecuteSemantic(operation, result => new { shipment = result }, ct);
    }

    private async Task<object> ExecuteSemantic(
        SemanticMutationOperation operation,
        Func<object, object> projection,
        CancellationToken ct)
    {
        var graph = new SemanticMutationOperationGraph([operation]);
        var plan = new MutationPlanner((IMutationSchema)_metadata).Plan(graph);
        var sqlPlan = new SqlMutationCompiler(_metadata).Compile(plan);

        await using var connection = await _dataSource.OpenConnectionAsync(ct);

        var result = new SqlMutationExecutionProvider(
                connection,
                metadata: _metadata)
            .ExecuteBatch(
                sqlPlan,
                new ExecutionContext(),
                ct);

        return projection(result.Results.Single());
    }

    // High-assurance transaction paths deliberately preserve explicit orchestration.
    // Their SQL remains parameterized and transaction-scoped because they require
    // PostgreSQL advisory locks, FOR UPDATE/SKIP LOCKED and multi-table invariants.
    public async Task<object> PlaceOrder(string actor, int customerId, OrderLine[] lines, string key,
        CancellationToken ct)
    {
        if (lines.Length == 0) throw new ArgumentException("At least one line is required.");
        if (lines.Any(x => x.Quantity <= 0)) throw new InvalidOperationException("Quantity must be positive.");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Idempotency key is required.");
        var requested = lines.GroupBy(x => x.ProductId).Select(g => new OrderLine(g.Key, g.Sum(x => x.Quantity)))
            .ToArray();
        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@k));", connection, tx))
        {
            lockCommand.Parameters.AddWithValue("k", key);
            await lockCommand.ExecuteScalarAsync(ct);
        }

        await using (var existing =
                     new NpgsqlCommand(
                         "SELECT order_id FROM supply_chain_idempotency WHERE idempotency_key=@k FOR SHARE", connection,
                         tx))
        {
            existing.Parameters.AddWithValue("k", key);
            var value = await existing.ExecuteScalarAsync(ct);
            if (value is not null)
            {
                await tx.CommitAsync(ct);
                return new { orderId = Convert.ToInt32(value), replay = true };
            }
        }

        await using (var customer =
                     new NpgsqlCommand("SELECT customer_id FROM customers WHERE customer_id=@id", connection, tx))
        {
            customer.Parameters.AddWithValue("id", customerId);
            if (await customer.ExecuteScalarAsync(ct) is null)
                throw new InvalidOperationException("Customer not found.");
        }

        decimal total = 0;
        var resolved = new List<(int product, int qty, decimal price, int warehouse)>();
        foreach (var line in requested)
        {
            await using var product =
                new NpgsqlCommand("SELECT unit_price FROM products WHERE product_id=@p", connection, tx);
            product.Parameters.AddWithValue("p", line.ProductId);
            var priceValue = await product.ExecuteScalarAsync(ct);
            if (priceValue is null) throw new InvalidOperationException($"Product {line.ProductId} not found.");
            await using var stock =
                new NpgsqlCommand(
                    "SELECT warehouse_id FROM inventory WHERE product_id=@p AND quantity_on_hand>=@q ORDER BY quantity_on_hand DESC, warehouse_id FOR UPDATE SKIP LOCKED LIMIT 1",
                    connection, tx);
            stock.Parameters.AddWithValue("p", line.ProductId);
            stock.Parameters.AddWithValue("q", line.Quantity);
            var warehouse = await stock.ExecuteScalarAsync(ct);
            if (warehouse is null)
                throw new InvalidOperationException($"Insufficient inventory for product {line.ProductId}.");
            var price = (decimal)priceValue;
            resolved.Add((line.ProductId, line.Quantity, price, Convert.ToInt32(warehouse)));
            total += price * line.Quantity;
        }

        int orderId;
        await using (var insertOrder =
                     new NpgsqlCommand(
                         "INSERT INTO orders(customer_id,status,total_amount) VALUES(@c,'Pending',@t) RETURNING order_id",
                         connection, tx))
        {
            insertOrder.Parameters.AddWithValue("c", customerId);
            insertOrder.Parameters.AddWithValue("t", total);
            orderId = Convert.ToInt32(await insertOrder.ExecuteScalarAsync(ct));
        }

        foreach (var item in resolved)
        {
            int itemId;
            await using (var insertItem =
                         new NpgsqlCommand(
                             "INSERT INTO order_items(order_id,product_id,quantity,unit_price) VALUES(@o,@p,@q,@u) RETURNING order_item_id",
                             connection, tx))
            {
                insertItem.Parameters.AddWithValue("o", orderId);
                insertItem.Parameters.AddWithValue("p", item.product);
                insertItem.Parameters.AddWithValue("q", item.qty);
                insertItem.Parameters.AddWithValue("u", item.price);
                itemId = Convert.ToInt32(await insertItem.ExecuteScalarAsync(ct));
            }

            await using var allocation = new NpgsqlCommand(
                "INSERT INTO order_allocations(order_item_id,warehouse_id,quantity) VALUES(@i,@w,@q); UPDATE inventory SET quantity_on_hand=quantity_on_hand-@q,last_updated=CURRENT_TIMESTAMP WHERE warehouse_id=@w AND product_id=@p AND quantity_on_hand>=@q",
                connection, tx);
            allocation.Parameters.AddWithValue("i", itemId);
            allocation.Parameters.AddWithValue("w", item.warehouse);
            allocation.Parameters.AddWithValue("q", item.qty);
            allocation.Parameters.AddWithValue("p", item.product);
            if (await allocation.ExecuteNonQueryAsync(ct) < 2)
                throw new InvalidOperationException("Inventory changed before reservation could be committed.");
        }

        await using (var idem = new NpgsqlCommand(
                         "INSERT INTO supply_chain_idempotency(idempotency_key,actor_id,operation,order_id) VALUES(@k,@a,'place_order',@o)",
                         connection, tx))
        {
            idem.Parameters.AddWithValue("k", key);
            idem.Parameters.AddWithValue("a", ActorNumber(actor));
            idem.Parameters.AddWithValue("o", orderId);
            await idem.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new
        {
            orderId, replay = false, total, evidence = Hash($"place_order|{actor}|{customerId}|{orderId}|{key}")
        };
    }

    public async Task<object> CancelOrder(string actor, int customerId, int orderId, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var update =
                     new NpgsqlCommand(
                         "UPDATE orders SET status='Cancelled' WHERE order_id=@o AND customer_id=@c AND status='Pending' RETURNING order_id",
                         connection, tx))
        {
            update.Parameters.AddWithValue("o", orderId);
            update.Parameters.AddWithValue("c", customerId);
            if (await update.ExecuteScalarAsync(ct) is null)
                throw new UnauthorizedAccessException(
                    "Sales order is not owned by the customer or is not cancellable.");
        }

        await using (var restore = new NpgsqlCommand(
                         "UPDATE inventory i SET quantity_on_hand=i.quantity_on_hand+a.quantity,last_updated=CURRENT_TIMESTAMP FROM order_allocations a JOIN order_items oi ON oi.order_item_id=a.order_item_id WHERE oi.order_id=@o AND i.warehouse_id=a.warehouse_id AND i.product_id=oi.product_id",
                         connection, tx))
        {
            restore.Parameters.AddWithValue("o", orderId);
            await restore.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return new { orderId, status = "Cancelled", restoredInventory = true };
    }

    private static int ActorNumber(string actor) => actor switch
    {
        "alice" => 1, "bob" => 2, "carol" => 3, "dave" => 4, "admin" => 5,
        _ when actor.StartsWith("customer", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(actor[8..], out var id) => id,
        _ => 0
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
}