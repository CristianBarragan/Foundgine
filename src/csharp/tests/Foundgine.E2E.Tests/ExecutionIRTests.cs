using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Planning;
using Xunit;

namespace Foundgine.E2E.Tests;

public sealed class ExecutionIRTests
{
    [Fact]
    public void SemanticPlan_Lowers_To_ExecutionIR_WithoutChangingTopology()
    {
        var child = new SemanticPlanNode(
            2,
            ExecutionOperation.Traverse,
            new EntityId(2),
            new[] { new FieldId(21) },
            new RelationshipId(7),
            Array.Empty<SemanticPlanNode>());

        var root = new SemanticPlanNode(
            1,
            ExecutionOperation.Scan,
            new EntityId(1),
            new[] { new FieldId(11), new FieldId(12) },
            null,
            new[] { child });

        var plan = new SemanticPlan(
            root,
            AuthorizationBinding: new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));

        var ir = ExecutionIRCompiler.Compile(plan);

        Assert.Equal(1, ir.Root.Id);
        Assert.Equal(ExecutionOperation.Scan, ir.Root.Operation);
        Assert.Equal(new EntityId(1), ir.Root.EntityId);
        Assert.Equal(new[] { new FieldId(11), new FieldId(12) }, ir.Root.Fields);
        var irChild = Assert.Single(ir.Root.Children);
        Assert.Equal(2, irChild.Id);
        Assert.Equal(new EntityId(2), irChild.EntityId);
        Assert.Equal(new RelationshipId(7), irChild.ViaRelationship);
    }

    [Fact]
    public void ExecutionIR_is_the_only_provider_execution_representation()
    {
        var plan = new SemanticPlan(
            new SemanticPlanNode(
                1,
                ExecutionOperation.Scan,
                new EntityId(1),
                new[] { new FieldId(11) },
                null,
                Array.Empty<SemanticPlanNode>()),
            AuthorizationBinding: new SemanticPlanAuthorizationBinding("test-contract", "test-authorization"));

        var ir = ExecutionIRCompiler.Compile(plan);

        Assert.Equal(plan.Root.Id, ir.Root.Id);
        Assert.Equal(plan.Root.EntityId, ir.Root.EntityId);
        Assert.Equal(plan.Root.Fields, ir.Root.Fields);
    }
}
