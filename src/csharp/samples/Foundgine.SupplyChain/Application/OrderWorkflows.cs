using System.Security.Cryptography;
using System.Text;
using Foundgine.Providers.EntityFrameworkCore;
using Foundgine.SupplyChain.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Foundgine.SupplyChain.Application;

/// <summary>
/// The two high-assurance workflows. Entity writes go through the generic repository; the few statements EF Core
/// cannot express stay native PostgreSQL SQL on the SAME transaction, parameterized and labelled [native]:
/// advisory lock, idempotency table, FOR UPDATE SKIP LOCKED allocation, allocation rows and conditional updates.
/// </summary>
public sealed class OrderWorkflows(
    FoundgineDbContext db,
    IFoundgineRepository<Customer> customers,
    IFoundgineRepository<CatalogProduct> products,
    IFoundgineRepository<SalesOrder> orders,
    IFoundgineRepository<SalesOrderLine> lines)
{
    public async Task<object> PlaceOrder(string actor, int customerId, OrderLine[] request, string key,
        CancellationToken ct)
    {
        if (request.Length == 0) throw new ArgumentException("At least one line is required.");
        if (request.Any(x => x.Quantity <= 0)) throw new InvalidOperationException("Quantity must be positive.");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Idempotency key is required.");
        var requested = request.GroupBy(x => x.ProductId).Select(g => new OrderLine(g.Key, g.Sum(x => x.Quantity)))
            .ToArray();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // [native] serialize concurrent attempts with the same idempotency key
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({key}))", ct);

            // [native] idempotency replay
            var existing = await db.Database
                .SqlQuery<int>($"SELECT order_id AS \"Value\" FROM supply_chain_idempotency WHERE idempotency_key = {key} FOR SHARE")
                .ToListAsync(ct);
            if (existing.Count > 0)
            {
                await tx.CommitAsync(ct);
                return new { orderId = existing[0], replay = true };
            }

            if (!await customers.ExistsAsync(c => c.Id == customerId, ct))
                throw new InvalidOperationException("Customer not found.");

            decimal total = 0;
            var resolved = new List<(int product, int qty, decimal price, int warehouse)>();
            foreach (var line in requested)
            {
                var product = await products.GetAsync(line.ProductId, ct)
                              ?? throw new InvalidOperationException($"Product {line.ProductId} not found.");
                var (productId, quantity) = (line.ProductId, line.Quantity);
                // [native] row-lock a warehouse with enough stock, skipping rows other orders hold
                var warehouses = await db.Database.SqlQuery<int>(
                        $"SELECT warehouse_id AS \"Value\" FROM inventory WHERE product_id = {productId} AND quantity_on_hand >= {quantity} ORDER BY quantity_on_hand DESC, warehouse_id FOR UPDATE SKIP LOCKED LIMIT 1")
                    .ToListAsync(ct);
                if (warehouses.Count == 0)
                    throw new InvalidOperationException($"Insufficient inventory for product {line.ProductId}.");
                resolved.Add((line.ProductId, line.Quantity, product.UnitPrice, warehouses[0]));
                total += product.UnitPrice * line.Quantity;
            }

            var order = await orders.AddAsync(new SalesOrder
                { CustomerId = customerId, Status = "Pending", TotalAmount = total }, ct);

            foreach (var item in resolved)
            {
                var orderLine = await lines.AddAsync(new SalesOrderLine
                {
                    OrderId = order.Id, ProductId = item.product, Quantity = item.qty, UnitPrice = item.price
                }, ct);

                var (itemId, warehouse, qty, product) = (orderLine.Id, item.warehouse, item.qty, item.product);
                // [native] allocation record + guarded decrement
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO order_allocations(order_item_id, warehouse_id, quantity) VALUES({itemId}, {warehouse}, {qty})", ct);
                var updated = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE inventory SET quantity_on_hand = quantity_on_hand - {qty}, last_updated = CURRENT_TIMESTAMP WHERE warehouse_id = {warehouse} AND product_id = {product} AND quantity_on_hand >= {qty}", ct);
                if (updated < 1)
                    throw new InvalidOperationException("Inventory changed before reservation could be committed.");
            }

            var actorId = ActorNumber(actor);
            var orderId = order.Id;
            // [native] idempotency record
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO supply_chain_idempotency(idempotency_key, actor_id, operation, order_id) VALUES({key}, {actorId}, 'place_order', {orderId})", ct);

            await tx.CommitAsync(ct);
            return new { orderId, replay = false, total, evidence = Hash($"place_order|{actor}|{customerId}|{orderId}|{key}") };
        }
        finally
        {
            db.ChangeTracker.Clear(); // a failed attempt must not leave tracked rows behind
        }
    }

    public async Task<object> CancelOrder(int customerId, int orderId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // [native] ownership + state guard in one conditional statement
        var rows = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE orders SET status = 'Cancelled' WHERE order_id = {orderId} AND customer_id = {customerId} AND status = 'Pending'", ct);
        if (rows == 0)
            throw new UnauthorizedAccessException("Sales order is not owned by the customer or is not cancellable.");

        // [native] restore allocated stock
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE inventory i SET quantity_on_hand = i.quantity_on_hand + a.quantity, last_updated = CURRENT_TIMESTAMP FROM order_allocations a JOIN order_items oi ON oi.order_item_id = a.order_item_id WHERE oi.order_id = {orderId} AND i.warehouse_id = a.warehouse_id AND i.product_id = oi.product_id", ct);

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
