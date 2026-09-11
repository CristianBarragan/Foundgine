using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Intent;
using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Serialization.Tests;

public sealed class SemanticIntentDocumentTests
{
    [Fact]
    public void Document_binds_dynamic_intent_to_frozen_contract()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer",
                e => e.Identity(new FieldId(1), "Id").Field(new FieldId(2), "Name", typeof(string)))
            .Build();
        var contract = model.Freeze().CreateSnapshot();
        var compiler = new ReadIntentCompiler(contract);
        var intent = new ReadIntent("Customer", [new ReadSelection("Name")]);

        var document = compiler.CreateDocument(intent);
        var resolution = compiler.ResolveDocument(document);

        Assert.Equal(contract.ContractFingerprint, document.ContractFingerprint);
        Assert.Equal(contract.ContractFingerprint, resolution.ContractFingerprint);
        Assert.Equal(customer, resolution.Request.Root);
    }

    [Fact]
    public void Stale_document_is_rejected_before_resolution()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e.Identity(new FieldId(1), "Id"))
            .Build();
        var compiler = new ReadIntentCompiler(model.Freeze().CreateSnapshot());
        var document =
            new SemanticIntentDocument("sha256:stale", new ReadIntent("Customer", [new ReadSelection("Id")]));

        var ex = Assert.Throws<InvalidOperationException>(() => compiler.ResolveDocument(document));
        Assert.Contains("bound to contract", ex.Message);
    }

    [Fact]
    public void Document_and_direct_intent_produce_the_same_operation_graph()
    {
        var customer = new EntityId(1);
        var order = new EntityId(2);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer",
                e => e.Identity(new FieldId(1), "Id")
                    .Relationship(new RelationshipId(1), "Orders", order, RelationshipCardinality.Many))
            .Entity(order, "Order",
                e => e.Identity(new FieldId(1), "Id").Field(new FieldId(2), "OrderDate", typeof(DateTime)))
            .Build();
        var compiler = new ReadIntentCompiler(model.Freeze().CreateSnapshot());
        var intent = new ReadIntent("Customer", [
            new ReadSelection("Id"),
            new ReadSelection(null, "Orders", [new ReadSelection("OrderDate")])
        ]);

        var direct = compiler.CompileOperationGraph(intent);
        var document = compiler.CreateDocument(intent);
        var fromDocument = compiler.ResolveDocumentGraph(document);

        Assert.Equal(direct.Fingerprint(), fromDocument.Fingerprint());
    }
}