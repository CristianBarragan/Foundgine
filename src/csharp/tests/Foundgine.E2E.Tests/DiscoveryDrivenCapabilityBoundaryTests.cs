using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Providers.Storage.InMemory;
using Foundgine.Core.Semantic.Planning;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;
using Foundgine.Core.Semantic.IR;
using Xunit;
using Foundgine.E2E.Tests.Banking;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

/// <summary>
/// Step 37: a discovery-driven escape-boundary harness.
///
/// Rather than hard-coding "the result must not contain field X", this test
/// asks the same <see cref="SemanticCapabilityContractDiscovery"/> surface an
/// MCP agent would call during capability discovery what fields it is
/// authorized to see for each entity in the result, and then asserts the
/// actual executed result never exceeds that self-declared boundary. This
/// pins the class of bug fixed in <c>InMemoryExecutionProvider.ToExecutionRow</c>
/// (leaking the full unauthorized backing row into <c>ExecutionRow.Values</c>)
/// without needing to know in advance which internal/backing-only fields a
/// given provider happens to carry.
///
/// The scenario runs the real end-to-end pipeline - resolve, authorize with
/// evidence, plan, lower to Execution IR, execute in-memory - against the
/// Banking fixture, whose <c>Account.CustomerId</c> is exactly this shape: a
/// real backing/relational column needed to resolve the CustomerAccounts
/// join, but never a selectable semantic field and never discoverable
/// through the capability contract.
/// </summary>
public sealed class DiscoveryDrivenCapabilityBoundaryTests
{
    [Fact]
    public async Task Discovered_capability_fields_are_the_ceiling_the_executed_result_never_exceeds()
    {
        var model = BankingSemanticModel.Build();
        var metadata = BankingRelationalMetadata.Build();
        var contract = model.Freeze().CreateSnapshot();
        var policy = new AllowAllSemanticAuthorizationPolicy();

        // What an agent would learn from capability discovery, up front.
        var capabilityContract = SemanticCapabilityContractDiscovery.Describe(model, policy);

        // Sanity-check the discovery surface itself: the backing-only FK
        // column must never be advertised as a readable Account field.
        var accountRead = Assert.Single(capabilityContract.Capabilities,
            c => c.TargetEntityId == BankingSemanticModel.Account && c.Operation == "read");
        Assert.DoesNotContain("CustomerId", accountRead.Fields);

        // Customer -> Accounts { Balance }, i.e. exactly the nested traversal
        // shape that routes through the CustomerAccounts join.
        var graph = new SemanticGraph();
        var root = graph.AddRoot(BankingSemanticModel.Customer, [new FieldId(2)]);
        graph.Add(BankingSemanticModel.Account, BankingSemanticModel.CustomerAccounts, root, [new FieldId(3)]);

        var operation = SemanticOperationCompiler.Compile(graph);
        var authorized = new SemanticAuthorizer(policy).AuthorizeWithEvidence(contract, operation);
        var plan = new Planner().Plan(contract, authorized);

        var ir = ExecutionIRCompiler.Compile(plan);
        var providerPlan = new InMemoryCompiler().Compile(ir);

        var data = new InMemoryDataSet()
            .Add(new InMemoryRow(BankingSemanticModel.Customer, new Dictionary<FieldId, object?>
            {
                [new FieldId(1)] = 1,
                [new FieldId(2)] = "Alice",
                [new FieldId(5)] = 7
            }))
            .Add(new InMemoryRow(BankingSemanticModel.Account, new Dictionary<FieldId, object?>
            {
                [new FieldId(1)] = 100,
                [new FieldId(3)] = 250.75m, // Balance
                [new FieldId(4)] = 1 // CustomerId FK - backing-only, join key
            }));

        var result = await new InMemoryExecutionProvider(metadata, data)
            .ExecuteAsync(providerPlan, new ExecutionContext());

        var row = Assert.Single(result.Rows);

        AssertResultRespectsDiscoveredFieldBoundary(capabilityContract, model, row);

        // Concretely, the join key value must never surface in the result,
        // and exactly the two selected/authorized fields must be present.
        Assert.Equal(2, row.EffectiveCells.Count);
        Assert.Contains("Alice", row.Values.Values);
        Assert.Contains(250.75m, row.Values.Values);
    }

    /// <summary>
    /// Reusable discovery-driven escape-boundary assertion: every field a
    /// provider actually returns for an entity must have been advertised as
    /// readable by that entity's discovered read capability, and Values must
    /// never carry more entries than Cells/EffectiveCells - the exact
    /// invariant restored by the Step 37 <c>ToExecutionRow</c> fix.
    /// </summary>
    private static void AssertResultRespectsDiscoveredFieldBoundary(
        SemanticCapabilityContract capabilityContract,
        SemanticModel model,
        ExecutionRow row)
    {
        Assert.Equal(row.EffectiveCells.Count, row.Values.Count);

        foreach (var cellKey in row.EffectiveCells.Keys)
        {
            var capability = capabilityContract.Capabilities.SingleOrDefault(c =>
                c.TargetEntityId == cellKey.EntityId && c.Operation == "read");
            Assert.NotNull(capability);

            var fieldName = model.Get(cellKey.EntityId).Fields.Single(f => f.Id == cellKey.FieldId).Name;
            Assert.Contains(fieldName, capability!.Fields);
        }
    }
}