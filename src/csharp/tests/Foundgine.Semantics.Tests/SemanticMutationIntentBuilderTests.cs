using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Mutation;
using Foundgine.Core.Semantic.Query;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticMutationIntentBuilderTests
{
    private static readonly EntityId Customer = new(1);
    private static readonly EntityId Account = new(2);
    private static readonly FieldId Id = new(1);
    private static readonly FieldId Name = new(2);
    private static readonly FieldId CustomerId = new(3);
    private static readonly FieldId Status = new(4);

    [Fact]
    public void Open_mutation_builder_supports_generated_value_dependencies_without_provider_concepts()
    {
        var model = BuildModel();

        var graph = new SemanticMutationIntentBuilder(model)
            .Create("Customer", "customer")
            .Set("Name", "Alice")
            .Return("Id")
            .Create("Account", "account")
            .SetFrom("CustomerId", "customer", "Id")
            .Set("Status", "Open")
            .Return("Id", "CustomerId")
            .Build();

        var plan = new SemanticMutationPlanner().Plan(graph);

        Assert.Equal(2, plan.Operations.Count);
        Assert.Single(plan.Dependencies);
        Assert.Equal(new FieldId(1), plan.Dependencies[0].SourceField);
        Assert.Equal(CustomerId, plan.Dependencies[0].TargetField);
    }

    [Fact]
    public void Open_mutation_builder_preserves_update_filters_and_conflict_semantics()
    {
        var model = BuildModel();

        var graph = new SemanticMutationIntentBuilder(model)
            .Upsert("Account")
            .Set("CustomerId", 42)
            .Set("Status", "Open")
            .Conflict("CustomerId")
            .Return("Id", "Status")
            .Update("Customer")
            .Set("Name", "Verified")
            .Where("Id", SemanticFilterOperator.Eq, 42)
            .Return("Id")
            .Build();

        var plan = new SemanticMutationPlanner().Plan(graph);

        Assert.Equal([CustomerId], plan.Operations[0].ConflictFields);
        Assert.IsType<SemanticFieldFilter>(plan.Operations[1].Filter);
        Assert.Equal([Id], plan.Operations[1].ReturnFields);
    }

    private static SemanticModel BuildModel() => new SemanticModelBuilder()
        .Entity(Customer, "Customer", e => e
            .Identity(Id, "Id")
            .Field(Name, "Name", typeof(string)))
        .Entity(Account, "Account", e => e
            .Identity(new FieldId(5), "Id")
            .Field(CustomerId, "CustomerId", typeof(long))
            .Field(Status, "Status", typeof(string)))
        .Build();
}