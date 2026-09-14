using Foundgine.SupplyChain.Advanced.Authorization;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

/// <summary>
/// GUIDE.md lists "sensitive-field authorization" as one of the difficult
/// cases the migration must preserve. <see cref="AuthorizationPolicyTests"/>
/// only asserts a single negative case in passing (Analyst cannot read
/// InventoryLot.Quarantined); this fixture is the dedicated, exhaustive
/// coverage for both field-level policies in
/// see SupplyChainAuthorization.CanReadField.
/// </summary>
public sealed class SensitiveFieldAuthorizationTests
{
    [Theory]
    [InlineData(SupplyChainRole.Customer, false)]
    [InlineData(SupplyChainRole.WarehouseOperator, false)]
    [InlineData(SupplyChainRole.Analyst, true)]
    [InlineData(SupplyChainRole.SupplyChainManager, true)]
    public void Supplier_risk_score_is_readable_only_by_analyst_and_manager(SupplyChainRole role, bool expectedAllowed)
    {
        var policy = SupplyChainAuthorization.Create("tenant-a", role);

        Assert.Equal(
            expectedAllowed,
            policy.CanAccessField(SupplyChainSemanticModel.Supplier,
                SupplyChainAuthorization.FieldIds.SupplierRiskScore));
    }

    [Theory]
    [InlineData(SupplyChainRole.Customer, false)]
    [InlineData(SupplyChainRole.Analyst, false)]
    [InlineData(SupplyChainRole.WarehouseOperator, true)]
    [InlineData(SupplyChainRole.SupplyChainManager, true)]
    public void Inventory_quarantined_quantity_is_readable_only_by_warehouse_operator_and_manager(SupplyChainRole role,
        bool expectedAllowed)
    {
        var policy = SupplyChainAuthorization.Create("tenant-a", role);

        Assert.Equal(
            expectedAllowed,
            policy.CanAccessField(SupplyChainSemanticModel.InventoryLot,
                SupplyChainAuthorization.FieldIds.InventoryQuarantined));
    }

    [Theory]
    [InlineData(SupplyChainRole.Customer)]
    [InlineData(SupplyChainRole.Analyst)]
    [InlineData(SupplyChainRole.WarehouseOperator)]
    [InlineData(SupplyChainRole.SupplyChainManager)]
    public void Ordinary_inventory_fields_remain_readable_for_every_role_that_can_read_the_entity(SupplyChainRole role)
    {
        var policy = SupplyChainAuthorization.Create("tenant-a", role);

        // Field-level sensitivity is scoped to the specific gated fields; it
        // must not accidentally shadow every other field on the entity.
        Assert.True(policy.CanAccessField(SupplyChainSemanticModel.InventoryLot,
            SupplyChainAuthorization.FieldIds.InventoryOnHand));
        Assert.True(policy.CanAccessField(SupplyChainSemanticModel.InventoryLot,
            SupplyChainAuthorization.FieldIds.InventoryReserved));
    }
}