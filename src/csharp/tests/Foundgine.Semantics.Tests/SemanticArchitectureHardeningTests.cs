using Xunit;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Resolution;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticArchitectureHardeningTests
{
    [SemanticEntity]
    private sealed class Customer
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public decimal CreditLimit { get; set; }
    }

    [SemanticEntity]
    private sealed class Order
    {
        public long Id { get; set; }
        public long CustomerId { get; set; }
    }

    [Fact]
    public void Relationship_overload_derives_stable_identity()
    {
        var customer = new EntityId(1);
        var order = new EntityId(2);
        var model = new SemanticModelBuilder()
            .Entity<Customer>(customer, "Customer", e => e.Identity(x => x.Id).Field(x => x.Name))
            .Entity<Order>(order, "Order", e => e.Identity(x => x.Id).Field(x => x.CustomerId))
            .Relationship<Customer, Order>(customer, "Orders", x => x.Id, order, x => x.CustomerId,
                RelationshipCardinality.Many)
            .Build();

        var relationship = Assert.Single(model.Get(customer).Relationships);
        Assert.Equal(RelationshipId.Create("Customer", "Orders"), relationship.Id);
    }

    [Fact]
    public void Relationship_identity_collision_across_entities_fails_closed()
    {
        var customer = new EntityId(1);
        var order = new EntityId(2);
        var sharedId = new RelationshipId(999);

        var builder = new SemanticModelBuilder()
            .Entity<Customer>(customer, "Customer", e => e.Identity(x => x.Id))
            .Entity<Order>(order, "Order", e => e.Identity(x => x.Id))
            .Relationship<Customer, Order>(customer, sharedId, "Orders", x => x.Id, order, x => x.CustomerId,
                RelationshipCardinality.Many)
            .Relationship<Order, Customer>(order, sharedId, "Customer", x => x.CustomerId, customer, x => x.Id,
                RelationshipCardinality.One);

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("Relationship identity collision", error.Message);
        Assert.Contains("Customer.Orders", error.Message);
        Assert.Contains("Order.Customer", error.Message);
    }

    [Fact]
    public void Independently_composed_modules_reject_relationship_identity_collisions()
    {
        var sharedId = new RelationshipId(777);
        var left = new SemanticModelBuilder()
            .Entity<Customer>(new EntityId(10), "Customer", e => e.Identity(x => x.Id))
            .Entity<Order>(new EntityId(11), "Order", e => e.Identity(x => x.Id))
            .Relationship<Customer, Order>(new EntityId(10), sharedId, "Orders", x => x.Id, new EntityId(11),
                x => x.CustomerId, RelationshipCardinality.Many)
            .Build();

        var right = new SemanticModelBuilder()
            .Entity<Customer>(new EntityId(20), "Customer", e => e.Identity(x => x.Id))
            .Entity<Order>(new EntityId(21), "Order", e => e.Identity(x => x.Id))
            .Relationship<Customer, Order>(new EntityId(20), sharedId, "Purchases", x => x.Id, new EntityId(21),
                x => x.CustomerId, RelationshipCardinality.Many)
            .Build();

        var builder = new SemanticModelBuilder().Import(left);
        builder.Import(right);

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("Relationship identity collision", error.Message);
    }

    [Fact]
    public void Field_constraints_are_retained_and_validated()
    {
        var model = new SemanticModelBuilder()
            .Entity<Customer>(new EntityId(1), "Customer", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name)
                .Field(x => x.CreditLimit)
                .Constraint(x => x.Name, SemanticConstraint.Pattern("^[A-Z]"))
                .Constraint(x => x.CreditLimit, SemanticConstraint.Range(0, 100000)))
            .Build();

        var fields = model.Get(new EntityId(1)).Fields;
        Assert.Contains(fields.Single(x => x.Name == "Name").EffectiveConstraints,
            x => x.Kind == SemanticConstraintKind.Pattern);
        Assert.Contains(fields.Single(x => x.Name == "CreditLimit").EffectiveConstraints,
            x => x.Kind == SemanticConstraintKind.Range);
    }

    [Fact]
    public void Loose_validation_allows_multiple_roots()
    {
        var model = new SemanticModelBuilder()
            .Entity<Customer>(new EntityId(1), "Customer", e => e.Identity(x => x.Id))
            .Entity<Order>(new EntityId(2), "Order", e => e.Identity(x => x.Id))
            .Build();

        var graph = new SemanticGraph();
        graph.AddRoot(new EntityId(1), [FieldId.Create("Customer", "Id")]);
        graph.AddRoot(new EntityId(2), [FieldId.Create("Order", "Id")]);

        SemanticGraphValidator.Validate(graph, model, SemanticGraphValidationMode.Loose);
        Assert.Throws<InvalidOperationException>(() => SemanticGraphValidator.Validate(graph, model));
    }

    [Fact]
    public void Traversal_resolution_expands_declared_path_without_inventing_identity()
    {
        var customer = new EntityId(1);
        var order = new EntityId(2);
        var customerOrders = RelationshipId.Create("Customer", "Orders");
        var model = new SemanticModelBuilder()
            .Entity<Customer>(customer, "Customer", e => e.Identity(x => x.Id))
            .Entity<Order>(order, "Order", e => e.Identity(x => x.Id))
            .Relationship<Customer, Order>(customer, customerOrders, "Orders", x => x.Id, order, x => x.CustomerId,
                RelationshipCardinality.Many)
            .Traversal("Customer", "OrdersPath", "Orders")
            .Build();

        var source = new FakeCandidates();
        source.IdentityMatches.Add((customer, "1", new IdentityCandidate("1", "Customer 1")));
        source.RelationshipMatches.Add((customerOrders, "1", new IdentityCandidate("9", "Order 9")));
        var resolver = new EntityResolver(model, source);
        var resolved = resolver.ResolveByTraversal(new ResolvedReference(customer, "1", 1, "test", []), "OrdersPath");

        Assert.Equal(ResolutionOutcome.Resolved, resolved.Outcome);
        Assert.Equal("9", resolved.Resolved!.IdentityValue);
    }

    private sealed class FakeCandidates : ICandidateSource
    {
        public List<(EntityId, string, IdentityCandidate)> IdentityMatches { get; } = [];
        public List<(RelationshipId, string, IdentityCandidate)> RelationshipMatches { get; } = [];

        public IReadOnlyList<IdentityCandidate> FindByIdentity(EntityId entityType, string identityValue) =>
            IdentityMatches.Where(x => x.Item1 == entityType && x.Item2 == identityValue).Select(x => x.Item3)
                .ToArray();

        public IReadOnlyList<IdentityCandidate>
            FindByRelationship(RelationshipId relationshipId, string sourceIdentityValue) => RelationshipMatches
            .Where(x => x.Item1 == relationshipId && x.Item2 == sourceIdentityValue).Select(x => x.Item3).ToArray();
    }
}