using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Semantic.Security.Execution;
using Foundgine.SupplyChain.Advanced.Authorization;
using Foundgine.SupplyChain.Advanced.Semantics;
using Xunit;

namespace Foundgine.SupplyChain.Advanced.Tests;

/// <summary>
/// Supply-Chain-flavored coverage for two of the recent semantic security
/// boundaries, exercised against the real generated domain model rather than
/// a synthetic one:
/// 
/// - Step 32, Semantic Traversal Safety &amp; Resource Bounds: a caller-supplied
///   <see cref="ReadIntent"/> is bounded by <see cref="SecurityResourceLimits"/>
///   before it ever reaches planning or execution, using the domain's own
///   recursive bill-of-materials relationship (<c>Product.components</c> -&gt;
///   <c>ProductComponent.componentProduct</c> -&gt; ...) as the deep traversal.
/// - Step 33, Graph-Level Authorization: <see cref="SemanticAuthorizer"/>
///   removes an entire denied relationship subtree - here, the
///   <c>Supplier.incidents</c> edge that <see>
///     <cref>SupplyChainAuthorization</cref>
/// </see>
/// denies to every role except Analyst and SupplyChainManager - while
///   still returning the rest of the graph.
/// </summary>
public sealed class GraphSecurityBoundaryTests
{
    [Fact]
    public void Deep_bill_of_materials_traversal_is_rejected_once_it_exceeds_the_configured_depth()
    {
        var intent = new ReadIntent(
            "Product",
            [
                new ReadSelection(Field: "Name"),
                new ReadSelection(Relationship: "components", Children:
                [
                    new ReadSelection(Relationship: "componentProduct", Children:
                    [
                        new ReadSelection(Relationship: "components", Children:
                        [
                            new ReadSelection(Field: "QuantityPerParent")
                        ])
                    ])
                ])
            ]);

        // Product (depth 1) -> components (2) -> componentProduct (3) -> components (4).
        var tightLimits = new SecurityResourceLimits { MaxOperationGraphDepth = 3 };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ReadIntentCompiler(SupplyChainSemanticModel.Model).CompileOperationGraph(intent, tightLimits));

        Assert.Contains("depth exceeds", ex.Message);

        // The same intent succeeds once the bound is generous enough,
        // proving the limit - not the traversal shape - caused the rejection.
        var graph = new ReadIntentCompiler(SupplyChainSemanticModel.Model)
            .CompileOperationGraph(intent, new SecurityResourceLimits { MaxOperationGraphDepth = 4 });
        Assert.Equal(4, graph.Nodes.Count);
    }

    [Fact]
    public void Denied_supplier_incidents_relationship_is_pruned_from_the_authorized_graph()
    {
        var intent = new ReadIntent(
            "Supplier",
            [
                new ReadSelection(Field: "Name"),
                new ReadSelection(Relationship: "incidents", Children:
                [
                    new ReadSelection(Field: "Severity")
                ])
            ]);

        var model = SupplyChainSemanticModel.Model;
        var graph = new ReadIntentCompiler(model).CompileOperationGraph(intent);
        var contract = model.Freeze().CreateSnapshot();

        // WarehouseOperator can read Supplier, but SupplyChainAuthorization
        // denies the "incidents" relationship to everyone except Analyst and
        // SupplyChainManager.
        var operatorPolicy = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.WarehouseOperator);
        var operatorResult = new SemanticAuthorizer(operatorPolicy).AuthorizeGraphWithEvidence(contract, graph);

        Assert.Single(operatorResult.Graph.Nodes);
        Assert.Equal(SupplyChainSemanticModel.Supplier, operatorResult.Graph.Root.EntityId);
        Assert.DoesNotContain(operatorResult.Graph.Nodes,
            n => n.EntityId == SupplyChainSemanticModel.ComplianceIncident);
        operatorResult.EnsureMatches(contract);

        // The same graph, authorized for a role the policy does grant the
        // relationship to, keeps both nodes - proving the pruning above is
        // the relationship-level denial and not, e.g., a malformed graph.
        var analystPolicy = SupplyChainAuthorization.Create("tenant-a", SupplyChainRole.Analyst);
        var analystResult = new SemanticAuthorizer(analystPolicy).AuthorizeGraphWithEvidence(contract, graph);

        Assert.Equal(2, analystResult.Graph.Nodes.Count);
        Assert.Contains(analystResult.Graph.Nodes, n => n.EntityId == SupplyChainSemanticModel.ComplianceIncident);
    }
}