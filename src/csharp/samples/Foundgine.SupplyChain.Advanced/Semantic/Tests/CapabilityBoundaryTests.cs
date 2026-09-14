using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Providers.Storage.InMemory;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.SupplyChain.Advanced.Tests;

/// <summary>
/// Supply-Chain-flavored regression coverage for the Step 37 escape-boundary
/// fix: a provider's <see cref="ExecutionRow.Values"/> and
/// <see cref="ExecutionRow.EffectiveCells"/> must expose exactly the fields an
/// <see cref="ExecutionIRNode"/> explicitly selects - never the full backing
/// row a data source happens to carry.
///
/// The generated Supply Chain domain model (<c>Domain.cs</c>) does not itself
/// have a hidden, backing-only column - every CLR property becomes a
/// selectable semantic field. To pin the boundary invariant against this
/// project's own domain shape, these tests build a small hand-authored
/// <see cref="MetadataRegistry"/> mirroring the real Supplier/PurchaseOrder
/// relationship, but with the join key modeled the way a legacy ERP
/// integration column often is in practice: present on every backing row,
/// required to resolve the relationship, and never exposed as a semantic
/// field. See cref="Foundgine.Providers.Storage.InMemory.Tests.ExecutionRowCapabilityBoundaryTests"
/// for the provider-neutral synthetic version of the same fix.
/// </summary>
public sealed class CapabilityBoundaryTests
{
    private static readonly EntityId Supplier = new(9001);
    private static readonly FieldId SupplierId = new(1);
    private static readonly FieldId SupplierName = new(2);

    private static readonly EntityId PurchaseOrder = new(9002);
    private static readonly FieldId PurchaseOrderId = new(1);
    private static readonly FieldId PurchaseOrderStatus = new(2);

    // Present on every backing PurchaseOrder row and required to resolve the
    // SupplierPurchaseOrders relationship, but deliberately never modeled as
    // a selectable semantic field - the in-memory analogue of a legacy ERP
    // correlation column that a real relational schema would carry.
    private static readonly FieldId PurchaseOrderSupplierFk = new(3);

    private static readonly RelationshipId SupplierPurchaseOrders = new(1);

    [Fact]
    public async Task Leaf_projection_of_supplier_name_never_leaks_an_unauthorized_backing_only_field()
    {
        // A backing-only field with no FieldMetadata entry at all, the same
        // shape as the join key below but simpler: nothing ever authorizes or
        // selects it, at any node.
        var internalCreditModelVersion = new FieldId(3);

        var metadata = new MetadataRegistry();
        metadata.Register(new EntityMetadata(
            Supplier,
            "Supplier",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "Name"),
                new ColumnMetadata(new ColumnId(3), "InternalCreditModelVersion")
            ],
            Fields:
            [
                new FieldMetadata(SupplierId, "Id", typeof(int), new ColumnReference(Supplier, new ColumnId(1))),
                new FieldMetadata(SupplierName, "Name", typeof(string), new ColumnReference(Supplier, new ColumnId(2)))
                // InternalCreditModelVersion intentionally has no FieldMetadata
                // entry: it is never selectable through the semantic surface.
            ],
            PrimaryKey: new ColumnReference(Supplier, new ColumnId(1))));

        var data = new InMemoryDataSet().Add(new InMemoryRow(
            Supplier,
            new Dictionary<FieldId, object?>
            {
                [SupplierId] = 1,
                [SupplierName] = "Kiwi Components",
                [internalCreditModelVersion] = 7 // backing-store-only; must never surface
            }));

        var ir = CreateIR(
            new ExecutionIRNode(0, ExecutionOperation.Scan, Supplier, [SupplierName], null, null, []),
            []);

        var plan = new InMemoryCompiler(metadata, data).Compile(ir);
        var result = await new InMemoryExecutionProvider(metadata, data)
            .ExecuteAsync(plan, new ExecutionContext());

        var row = Assert.Single(result.Rows);

        Assert.Equal(row.EffectiveCells.Count, row.Values.Count);
        Assert.Single(row.Values);
        Assert.Contains("Kiwi Components", row.Values.Values);
        Assert.DoesNotContain(7, row.Values.Values);
        Assert.DoesNotContain(row.EffectiveCells.Keys, key => key.FieldId == internalCreditModelVersion);
    }

    [Fact]
    public async Task Traversal_from_supplier_to_purchase_orders_never_leaks_the_relationship_join_key()
    {
        var metadata = new MetadataRegistry();
        metadata.Register(new EntityMetadata(
            Supplier,
            "Supplier",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(SupplierId, "Id", typeof(int), new ColumnReference(Supplier, new ColumnId(1))),
                new FieldMetadata(SupplierName, "Name", typeof(string), new ColumnReference(Supplier, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(Supplier, new ColumnId(1))));

        metadata.Register(new EntityMetadata(
            PurchaseOrder,
            "PurchaseOrder",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "SupplierId"),
                new ColumnMetadata(new ColumnId(3), "Status")
            ],
            Fields:
            [
                new FieldMetadata(PurchaseOrderStatus, "Status", typeof(string),
                    new ColumnReference(PurchaseOrder, new ColumnId(3))),
                // Internal foreign-key mapping only, same shape as
                // BankingRelationalMetadata.Account.CustomerId: no semantic
                // field ever exposes it, it exists solely to resolve the
                // SupplierPurchaseOrders relationship.
                new FieldMetadata(PurchaseOrderSupplierFk, "SupplierId", typeof(int),
                    new ColumnReference(PurchaseOrder, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(PurchaseOrder, new ColumnId(1))));

        metadata.Register(new RelationshipMetadata(
            SupplierPurchaseOrders,
            Supplier,
            PurchaseOrder,
            "PurchaseOrders",
            new ColumnReference(Supplier, new ColumnId(1)),
            new ColumnReference(PurchaseOrder, new ColumnId(2))));

        var data = new InMemoryDataSet()
            .Add(new InMemoryRow(Supplier,
                new Dictionary<FieldId, object?> { [SupplierId] = 1, [SupplierName] = "Kiwi Components" }))
            .Add(new InMemoryRow(PurchaseOrder, new Dictionary<FieldId, object?>
            {
                [PurchaseOrderId] = 500,
                [PurchaseOrderStatus] = "Open",
                [PurchaseOrderSupplierFk] = 1 // join key; never authorized as a selectable field
            }));

        var ir = CreateIR(
            new ExecutionIRNode(
                0, ExecutionOperation.Scan, Supplier, [SupplierName], null, null,
                [
                    new ExecutionIRNode(1, ExecutionOperation.Traverse, PurchaseOrder, [PurchaseOrderStatus],
                        SupplierPurchaseOrders, null, [])
                ]),
            []);

        var plan = new InMemoryCompiler(metadata, data).Compile(ir);
        var result = await new InMemoryExecutionProvider(metadata, data)
            .ExecuteAsync(plan, new ExecutionContext());

        var row = Assert.Single(result.Rows);

        // Both explicitly selected fields cross the boundary - the root
        // Supplier.Name and the traversed PurchaseOrder.Status - matching
        // one-for-one between Values and EffectiveCells.
        Assert.Equal(row.EffectiveCells.Count, row.Values.Count);
        Assert.Contains("Kiwi Components", row.Values.Values);
        Assert.Contains("Open", row.Values.Values);
        Assert.Contains(row.EffectiveCells, cell => Equals(cell.Value, "Kiwi Components"));
        Assert.Contains(row.EffectiveCells, cell => Equals(cell.Value, "Open"));

        // The join key that made the traversal possible must not leak into
        // the provider-facing result, despite living on the backing row that
        // participated in the merge.
        Assert.DoesNotContain(row.EffectiveCells.Keys, key => key.FieldId == PurchaseOrderSupplierFk);
    }

    private static ExecutionIR CreateIR(ExecutionIRNode root, IReadOnlyList<string> requiredSecurityInvariants) =>
        new(root, requiredSecurityInvariants,
            new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));
}