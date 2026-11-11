using Foundgine.SupplyChain.Advanced.OpenIntent.Services;

namespace Foundgine.SupplyChain.Advanced.OpenIntent.Repositories;

/// <summary>Persistence + query operations the service layer builds on. Implemented by <see cref="SupplyChainExecutionService"/>.</summary>
public interface ISupplyChainRepository
{
    Task<object> GetOrders(int customerId, CancellationToken ct);
    Task<object> GetOrder(int customerId, int orderId, CancellationToken ct);
    Task<object> GetShipment(int customerId, int shipmentId, CancellationToken ct);
    Task<object> ListProducts(CancellationToken ct);
    Task<object> ListCustomers(CancellationToken ct);
    Task<object> GetProduct(int productId, CancellationToken ct);
    Task<object> GetInventory(int productId, CancellationToken ct);
    Task<object> ListSuppliers(CancellationToken ct);
    Task<object> FindTopSupplierOverdueOrders(string actor, string state, string? supplierName, CancellationToken ct);
    Task<object> UpdateInventory(int warehouseId, int productId, int quantity, CancellationToken ct);
    Task<object> CreateShipment(int orderId, int carrierId, int warehouseId, string trackingNumber, CancellationToken ct);
    Task<object> UpdateShipment(int shipmentId, string status, CancellationToken ct);
    Task<object> PlaceOrder(string actor, int customerId, OrderLine[] lines, string key, CancellationToken ct);
    Task<object> CancelOrder(string actor, int customerId, int orderId, CancellationToken ct);
}
