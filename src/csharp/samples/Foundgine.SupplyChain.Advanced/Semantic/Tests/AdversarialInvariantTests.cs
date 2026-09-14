using Foundgine.SupplyChain.Advanced.Data;
using Foundgine.SupplyChain.Advanced.Domain;
using Foundgine.SupplyChain.Advanced.Scenarios;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

public sealed class AdversarialInvariantTests
{
    [Fact]
    public void Passes_for_the_documented_seed_fixture_and_correctly_scoped_tenant_a_context()
    {
        var data = SupplyChainData.Seed();
        var auth = new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1), new(2) }, true, true);
        var exception = Record.Exception(() => SupplyChainScenarios.AssertAdversarialInvariants(data, auth));
        Assert.Null(exception);
    }

    [Fact]
    public void Fails_when_authorization_context_leaks_a_cross_tenant_warehouse()
    {
        var data = SupplyChainData.Seed();
        var leakyAuth =
            new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1), new(2), new(3) }, true, true);
        var exception = Record.Exception(() => SupplyChainScenarios.AssertAdversarialInvariants(data, leakyAuth));
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("tenant-b", exception!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fails_when_the_deliberate_BOM_cycle_fixture_is_missing()
    {
        var data = new SupplyChainData();
        data.Suppliers.Add(new Supplier(new SupplierId(1), "Kiwi Components", "NZ", 0.22m, "tenant-a"));
        data.Products.Add(new Product(new ProductId(1), "MOTOR-X", "Industrial Motor", "Motors", 30));
        data.Products.Add(new Product(new ProductId(2), "CTRL-X", "Motor Controller", "Controls", 40));
        data.Components.Add(new ProductComponent(new ProductId(1), new ProductId(2), 1));
        data.Certifications.Add(new SupplierCertification(new CertificationId(1), new SupplierId(1), "ISO9001",
            new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)));
        var auth = new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1) }, true, true);
        var exception = Record.Exception(() => SupplyChainScenarios.AssertAdversarialInvariants(data, auth));
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("cycle", exception!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fails_when_supplier_3_certification_is_not_expired()
    {
        var data = new SupplyChainData();
        data.Suppliers.Add(new Supplier(new SupplierId(1), "Kiwi Components", "NZ", 0.22m, "tenant-a"));
        data.Suppliers.Add(new Supplier(new SupplierId(3), "Global Metals", "US", 0.62m, "tenant-b"));
        data.Products.Add(new Product(new ProductId(1), "MOTOR-X", "Industrial Motor", "Motors", 30));
        data.Products.Add(new Product(new ProductId(2), "CTRL-X", "Motor Controller", "Controls", 40));
        data.Products.Add(new Product(new ProductId(3), "PCB-X", "Controller PCB", "Electronics", 80));
        data.Components.Add(new ProductComponent(new ProductId(1), new ProductId(2), 1));
        data.Components.Add(new ProductComponent(new ProductId(2), new ProductId(3), 1));
        data.Components.Add(new ProductComponent(new ProductId(3), new ProductId(1), 1));
        data.Certifications.Add(new SupplierCertification(new CertificationId(1), new SupplierId(3), "ISO9001",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 12, 31)));
        var auth = new AuthorizationContext("tenant-a", new HashSet<WarehouseId> { new(1) }, true, true);
        var exception = Record.Exception(() => SupplyChainScenarios.AssertAdversarialInvariants(data, auth));
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("expired certification", exception!.Message, StringComparison.OrdinalIgnoreCase);
    }
}