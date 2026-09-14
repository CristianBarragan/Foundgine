using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticRelationshipIdentityConsistencyTests
{
    private sealed class Customer
    {
        public int Id { get; set; }
    }

    private sealed class Order
    {
        public int Id { get; set; }
    }

    private sealed class Invoice
    {
        public int Id { get; set; }
    }

    [Fact]
    public void SameRelationshipIdentityMustAgreeOnTargetAndCardinality()
    {
        var customerId = EntityId.Create("Customer");
        var orderId = EntityId.Create("Order");
        var invoiceId = EntityId.Create("Invoice");
        var relationshipId = RelationshipId.Create("Customer", "Orders");

        var builder = new SemanticModelBuilder();
        builder.Entity<Customer>(customerId, "Customer", e =>
            e.Identity(x => x.Id)
                .Field(x => x.Id));
        builder.Entity<Order>(orderId, "Order", e =>
            e.Identity(x => x.Id)
                .Field(x => x.Id));
        builder.Entity<Invoice>(invoiceId, "Invoice", e =>
            e.Identity(x => x.Id)
                .Field(x => x.Id));

        builder.Relationship<Customer, Order>(customerId, relationshipId, "Orders", x => x.Id, orderId, x => x.Id,
            RelationshipCardinality.Many);
        builder.Relationship<Customer, Invoice>(customerId, relationshipId, "Orders", x => x.Id, invoiceId, x => x.Id,
            RelationshipCardinality.Many);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("Relationship identity conflict", exception.Message);
        Assert.Contains("Customer.Orders", exception.Message);
    }

    [Fact]
    public void SameRelationshipIdentityMustAgreeOnCardinality()
    {
        var customerId = EntityId.Create("Customer");
        var orderId = EntityId.Create("Order");
        var relationshipId = RelationshipId.Create("Customer", "Orders");

        var builder = new SemanticModelBuilder();
        builder.Entity<Customer>(customerId, "Customer", e =>
            e.Identity(x => x.Id)
                .Field(x => x.Id));
        builder.Entity<Order>(orderId, "Order", e =>
            e.Identity(x => x.Id)
                .Field(x => x.Id));

        builder.Relationship<Customer, Order>(customerId, relationshipId, "Orders", x => x.Id, orderId, x => x.Id,
            RelationshipCardinality.Many);
        builder.Relationship<Customer, Order>(customerId, relationshipId, "Orders", x => x.Id, orderId, x => x.Id,
            RelationshipCardinality.One);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("Relationship identity conflict", exception.Message);
        Assert.Contains("Many", exception.Message);
        Assert.Contains("One", exception.Message);
    }
}