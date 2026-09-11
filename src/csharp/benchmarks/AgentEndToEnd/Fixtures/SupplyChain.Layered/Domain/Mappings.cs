using Foundgine.Providers.Aot;
using Foundgine.SupplyChain.Domain.Models;
using Foundgine.SupplyChain.Domain.Storage;

namespace Foundgine.SupplyChain.Domain;

/// <summary>
/// Schema-bound model/entity mappings. Keeping these declarations in their own
/// file prevents either the application model or the ERP entity from depending
/// on the other CLR type.
/// </summary>
[FoundgineModelEntityMap(typeof(Customer), typeof(CustomerERP))]
[FoundgineModelEntityMap(typeof(SalesOrder), typeof(SalesOrderERP))]
[FoundgineModelEntityMap(typeof(SalesOrderLine), typeof(SalesOrderLineERP))]
[FoundgineModelEntityMap(typeof(CatalogProduct), typeof(CatalogProductERP))]
[FoundgineModelEntityMap(typeof(Supplier), typeof(SupplierERP))]
[FoundgineModelEntityMap(typeof(Category), typeof(CategoryERP))]
[FoundgineModelEntityMap(typeof(InventoryPosition), typeof(InventoryPositionERP))]
[FoundgineModelEntityMap(typeof(Warehouse), typeof(WarehouseERP))]
[FoundgineModelEntityMap(typeof(Shipment), typeof(ShipmentERP))]
[FoundgineModelEntityMap(typeof(Carrier), typeof(CarrierERP))]
[FoundgineConnectionMap(typeof(Customer), nameof(Customer.Orders), typeof(SalesOrderERP))]
internal static class SupplyChainSchemaMappings
{
}