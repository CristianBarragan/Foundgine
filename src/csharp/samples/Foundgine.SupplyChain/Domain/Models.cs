namespace Foundgine.SupplyChain.Domain.Models;

// Plain domain classes. Each one is BOTH the Foundgine semantic model and the EF Core entity: there is no
// separate storage type and no mapping layer. Tables and columns are configured fluently in
// SupplyChainModel.cs; the types carry no Foundgine or EF attributes. Scalar members are the mapped fields;
// navigation members exist only so configuration can name relationships with lambdas (EF ignores them,
// Foundgine never populates them - related rows are read through the semantic layer).

public sealed class Customer
{
    public int Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";

    public IReadOnlyList<SalesOrder> Orders { get; init; } = [];
}

public sealed class SalesOrder
{
    public int Id { get; init; }
    public int CustomerId { get; init; }
    public string Status { get; init; } = "";
    public decimal TotalAmount { get; init; }

    public IReadOnlyList<SalesOrderLine> Lines { get; init; } = [];
    public IReadOnlyList<Shipment> Shipments { get; init; } = [];
}

public sealed class SalesOrderLine
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public int ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }

    public CatalogProduct Product { get; init; } = null!;
}

public sealed class CatalogProduct
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Sku { get; init; } = "";
    public decimal UnitPrice { get; init; }
    public int SupplierId { get; init; }
    public int CategoryId { get; init; }

    public Supplier Supplier { get; init; } = null!;
    public Category Category { get; init; } = null!;
    public IReadOnlyList<InventoryPosition> InventoryPositions { get; init; } = [];
}

public sealed class Supplier
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Email { get; init; } = "";
}

public sealed class Category
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public sealed class InventoryPosition
{
    public int Id { get; init; }
    public int WarehouseId { get; init; }
    public int ProductId { get; init; }
    public int QuantityOnHand { get; init; }
    public int ReorderLevel { get; init; }

    public Warehouse Warehouse { get; init; } = null!;
}

public sealed class Warehouse
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Location { get; init; } = "";
}

public sealed class Shipment
{
    public int Id { get; init; }
    public int OrderId { get; init; }
    public int CarrierId { get; init; }
    public int WarehouseId { get; init; }
    public string TrackingNumber { get; init; } = "";
    public string Status { get; init; } = "";

    public SalesOrder Order { get; init; } = null!;
    public Carrier Carrier { get; init; } = null!;
    public Warehouse Warehouse { get; init; } = null!;
}

public sealed class Carrier
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}
