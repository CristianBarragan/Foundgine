using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Providers.Storage.InMemory;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Providers.Storage.InMemory.Tests;

/// <summary>
/// Step 37 escape-boundary regression tests.
///
/// Before the fix, <see cref="InMemoryExecutionProvider"/>'s
/// <c>ToExecutionRow</c> built <see cref="ExecutionRow.Values"/> from the
/// full unauthorized backing <see cref="InMemoryRow.Values"/> dictionary,
/// while <see cref="ExecutionRow.Cells"/>/<see cref="ExecutionRow.EffectiveCells"/>
/// were correctly restricted to the authorized <c>ExecutionIRNode.Fields</c>
/// set. Any backing-store column that a data source carries for internal
/// purposes (foreign keys, risk scores, join keys, etc.) but that is not part
/// of the semantic model's selectable field surface would leak into
/// <c>Values</c> even though it was never authorized, never selected, and
/// never discoverable through the capability contract.
///
/// These tests pin the fix directly against the provider, independent of
/// planner/authorizer wiring, by constructing <see cref="ExecutionIR"/> nodes
/// whose <c>Fields</c> deliberately omit a backing-only field that the
/// in-memory rows nonetheless carry (exactly the shape of
/// <c>Foundgine.E2E.Tests.Banking.BankingRelationalMetadata</c>'s
/// <c>Account.CustomerId</c>, which exists only for relationship traversal
/// and has no corresponding semantic field).
/// </summary>
public sealed class ExecutionRowCapabilityBoundaryTests
{
    private static readonly EntityId Account = new(9);
    private static readonly FieldId AccountId = new(1);
    private static readonly FieldId Balance = new(2);

    // Present in the backing store, but deliberately NOT modeled as a
    // selectable semantic field anywhere - the in-memory analogue of
    // Account.CustomerId in BankingRelationalMetadata.
    private static readonly FieldId InternalRiskScore = new(3);

    private static readonly EntityId Customer = new(10);
    private static readonly FieldId CustomerId = new(1);
    private static readonly FieldId CustomerName = new(2);
    private static readonly RelationshipId CustomerAccounts = new(1);

    [Fact]
    public async Task Leaf_projection_exposes_only_the_authorized_field_not_the_full_backing_row()
    {
        var metadata = new MetadataRegistry();
        metadata.Register(new EntityMetadata(
            Account,
            "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "Balance"),
                new ColumnMetadata(new ColumnId(3), "InternalRiskScore")
            ],
            Fields:
            [
                new FieldMetadata(AccountId, "Id", typeof(int), new ColumnReference(Account, new ColumnId(1))),
                new FieldMetadata(Balance, "Balance", typeof(decimal), new ColumnReference(Account, new ColumnId(2)))
                // InternalRiskScore intentionally has no FieldMetadata entry:
                // it is never selectable through the semantic surface.
            ],
            PrimaryKey: new ColumnReference(Account, new ColumnId(1))));

        var data = new InMemoryDataSet().Add(new InMemoryRow(
            Account,
            new Dictionary<FieldId, object?>
            {
                [AccountId] = 1,
                [Balance] = 500m,
                [InternalRiskScore] = 987.65m // backing-store-only; must never surface
            }));

        // The plan only ever authorizes/selects Balance.
        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(0, ExecutionOperation.Scan, Account, [Balance], null, null, []),
            []);

        var plan = new InMemoryCompiler(metadata, data).Compile(ir);
        var result = await new InMemoryExecutionProvider(metadata, data)
            .ExecuteAsync(plan, new ExecutionContext());

        var row = Assert.Single(result.Rows);

        // Pin: Values must expose exactly the authorized field set, matching
        // Cells one-for-one, the same invariant the Step 37 fix restores.
        Assert.Equal(row.EffectiveCells.Count, row.Values.Count);
        Assert.Single(row.Values);
        Assert.Equal(500m, Assert.Single(row.EffectiveCells).Value);

        // Escape-boundary check: the unauthorized backing values never reach
        // the provider-facing Values dictionary, however it is keyed.
        Assert.DoesNotContain(987.65m, row.Values.Values);
    }

    [Fact]
    public async Task Nested_traversal_merge_does_not_leak_the_relationship_join_key_used_to_reach_it()
    {
        // Account.CustomerId (FieldId 3) exists only so the provider can
        // resolve the CustomerAccounts join. It is never part of any
        // ExecutionIRNode.Fields set and must never appear in the merged
        // result, even though every Account backing row carries it.
        var customerFk = new FieldId(3);

        var metadata = new MetadataRegistry();
        metadata.Register(new EntityMetadata(
            Customer,
            "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(CustomerId, "Id", typeof(int), new ColumnReference(Customer, new ColumnId(1))),
                new FieldMetadata(CustomerName, "Name", typeof(string), new ColumnReference(Customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(Customer, new ColumnId(1))));

        metadata.Register(new EntityMetadata(
            Account,
            "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Balance")
            ],
            Fields:
            [
                new FieldMetadata(Balance, "Balance", typeof(decimal), new ColumnReference(Account, new ColumnId(3))),
                // Internal foreign-key mapping only, same shape as
                // BankingRelationalMetadata.Account.CustomerId: no semantic
                // field ever exposes it, it exists solely to resolve joins.
                new FieldMetadata(customerFk, "CustomerId", typeof(int), new ColumnReference(Account, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(Account, new ColumnId(1))));

        metadata.Register(new RelationshipMetadata(
            CustomerAccounts,
            Customer,
            Account,
            "Accounts",
            new ColumnReference(Customer, new ColumnId(1)),
            new ColumnReference(Account, new ColumnId(2))));

        var data = new InMemoryDataSet()
            .Add(new InMemoryRow(Customer,
                new Dictionary<FieldId, object?> { [CustomerId] = 1, [CustomerName] = "Alice" }))
            .Add(new InMemoryRow(Account, new Dictionary<FieldId, object?>
            {
                [AccountId] = 100,
                [Balance] = 250.75m,
                [customerFk] = 1 // join key; never authorized as a selectable field
            }));

        var ir = Foundgine.Testing.ExecutionIRTestFactory.Create(
            new ExecutionIRNode(
                0, ExecutionOperation.Scan, Customer, [CustomerName], null, null,
                [
                    new ExecutionIRNode(1, ExecutionOperation.Traverse, Account, [Balance], CustomerAccounts, null, [])
                ]),
            []);

        var plan = new InMemoryCompiler(metadata, data).Compile(ir);
        var result = await new InMemoryExecutionProvider(metadata, data)
            .ExecuteAsync(plan, new ExecutionContext());

        var row = Assert.Single(result.Rows);

        // The traversal is optional at the provider boundary; regardless of
        // whether a related row is materialized, only explicitly selected
        // fields may cross the boundary. Balance IS explicitly selected on
        // the Traverse node, so it must cross; only the unselected join key
        // (customerFk) must not.
        Assert.Equal(row.EffectiveCells.Count, row.Values.Count);
        Assert.Contains("Alice", row.Values.Values);
        Assert.Contains(row.EffectiveCells, cell => Equals(cell.Value, "Alice"));
        Assert.Contains(250.75m, row.Values.Values);
        Assert.Contains(row.EffectiveCells, cell => Equals(cell.Value, 250.75m));

        // The join key that made the traversal possible must not leak into
        // the provider-facing result, despite living on the backing row that
        // participated in the merge.
        Assert.DoesNotContain(row.EffectiveCells.Keys, key => key.FieldId == customerFk);
    }
}