namespace Foundgine.SupplyChain.Application;

public sealed record OrderLine(int ProductId, int Quantity);

public interface ISupplyChainQueries
{
    Task<object> GetOrders(int customerId, CancellationToken ct);
    Task<object> GetOrder(int customerId, int orderId, CancellationToken ct);
    Task<object> GetShipment(int customerId, int shipmentId, CancellationToken ct);
    Task<object> ListProducts(CancellationToken ct);
    Task<object> ListCustomers(CancellationToken ct);
    Task<object> GetProduct(int productId, CancellationToken ct);
    Task<object> GetInventory(int productId, CancellationToken ct);
    Task<object> ListSuppliers(CancellationToken ct);
}

public interface ISupplyChainMutations
{
    Task<object> UpdateInventory(int warehouseId, int productId, int quantity, CancellationToken ct);

    Task<object> CreateShipment(int orderId, int carrierId, int warehouseId, string trackingNumber,
        CancellationToken ct);

    Task<object> UpdateShipment(int shipmentId, string status, CancellationToken ct);
    Task<object> PlaceOrder(string actor, int customerId, OrderLine[] lines, string key, CancellationToken ct);
    Task<object> CancelOrder(string actor, int customerId, int orderId, CancellationToken ct);
}