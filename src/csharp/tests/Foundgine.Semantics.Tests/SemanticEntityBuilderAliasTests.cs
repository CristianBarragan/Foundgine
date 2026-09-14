using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

/// <summary>
/// Covers the plural alias-declaration surface added to <see cref="SemanticEntityBuilder{TModel}"/>
/// (and its untyped/obsolete counterpart): <c>Aliases(params string[])</c>,
/// <c>FieldAliases(...)</c> and <c>RelationshipAliases(...)</c>. These sit alongside the
/// pre-existing singular <c>Alias</c>/<c>FieldAlias</c>/<c>RelationshipAlias</c> methods and
/// are expected to behave identically to calling the singular form once per name, including
/// duplicate rejection.
/// </summary>
public sealed class SemanticEntityBuilderAliasTests
{
    private sealed record TestSalesOrder(int Id, string Status);

    private sealed record TestSalesOrderLine(int OrderId, int LineNumber);

    [Fact]
    public void Typed_builder_declares_multiple_entity_aliases_in_one_call()
    {
        var order = new EntityId(500);

        var model = new SemanticModelBuilder()
            .Entity<TestSalesOrder>(order, "SalesOrder", e => e
                .Identity(x => x.Id)
                .Aliases("aa", "bb"))
            .Build();

        Assert.Equal(
            ["aa", "bb"],
            model.Get(order).EffectiveAliases.Select(a => a.Name));
    }

    [Fact]
    public void Typed_builder_accepts_collection_expression_syntax_for_aliases()
    {
        var order = new EntityId(501);

        var model = new SemanticModelBuilder()
            .Entity<TestSalesOrder>(order, "SalesOrder", e => e
                .Identity(x => x.Id)
                .Aliases(["aa", "bb"]))
            .Build();

        Assert.Equal(
            ["aa", "bb"],
            model.Get(order).EffectiveAliases.Select(a => a.Name));
    }

    [Fact]
    public void Typed_builder_rejects_duplicate_alias_within_a_single_Aliases_call()
    {
        var order = new EntityId(502);

        Assert.Throws<ArgumentException>(() =>
            new SemanticModelBuilder()
                .Entity<TestSalesOrder>(order, "SalesOrder", e => e
                    .Identity(x => x.Id)
                    .Aliases("aa", "AA")));
    }

    [Fact]
    public void Typed_builder_rejects_alias_duplicated_against_a_prior_Alias_call()
    {
        var order = new EntityId(503);

        Assert.Throws<ArgumentException>(() =>
            new SemanticModelBuilder()
                .Entity<TestSalesOrder>(order, "SalesOrder", e => e
                    .Identity(x => x.Id)
                    .Alias("aa")
                    .Aliases("bb", "aa")));
    }

    [Fact]
    public void Typed_builder_declares_multiple_field_aliases_in_one_call()
    {
        var order = new EntityId(504);

        var model = new SemanticModelBuilder()
            .Entity<TestSalesOrder>(order, "SalesOrder", e => e
                .Identity(x => x.Id)
                .Field(x => x.Status)
                .FieldAliases(x => x.Status, "State", "Stage"))
            .Build();

        Assert.Equal(
            ["State", "Stage"],
            model.Get(order).Fields.Single(f => f.Name == "Status").EffectiveAliases.Select(a => a.Name));
    }

    [Fact]
    public void Typed_builder_declares_multiple_relationship_aliases_in_one_call()
    {
        var order = new EntityId(505);
        var line = new EntityId(506);
        var relationshipId = RelationshipId.Create("SalesOrder", "lines");

        var model = new SemanticModelBuilder()
            .Entity<TestSalesOrder>(order, "SalesOrder", e => e
                .Identity(x => x.Id)
                .Relationship<TestSalesOrderLine>(
                    "lines",
                    x => x.Id,
                    (TestSalesOrderLine x) => x.OrderId,
                    line,
                    RelationshipCardinality.Many)
                .RelationshipAliases(relationshipId, "items", "detail"))
            .Entity<TestSalesOrderLine>(line, "SalesOrderLine", e => e
                .Identity(x => x.OrderId, "Id"))
            .Build();

        Assert.Equal(
            ["items", "detail"],
            model.Get(order).Relationships.Single(r => r.Id == relationshipId).EffectiveAliases.Select(a => a.Name));
    }

    [Fact]
    public void Untyped_builder_declares_multiple_entity_and_field_aliases_in_one_call()
    {
        var order = new EntityId(507);

        var model = new SemanticModelBuilder()
            .Entity(order, "SalesOrder", e => e
                .Identity(new FieldId(1), "Id")
                .Aliases("aa", "bb")
                .Field(new FieldId(2), "Status", typeof(string))
                .FieldAliases(new FieldId(2), "State", "Stage"))
            .Build();

        var entity = model.Get(order);
        Assert.Equal(["aa", "bb"], entity.EffectiveAliases.Select(a => a.Name));
        Assert.Equal(["State", "Stage"],
            entity.Fields.Single(f => f.Name == "Status").EffectiveAliases.Select(a => a.Name));
    }
}