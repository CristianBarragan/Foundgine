using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticModelTests
{
    [Fact]
    public void Banking_domain_can_be_described_as_semantic_model()
    {
        var customer = new EntityId(1);
        var account = new EntityId(2);
        var transaction = new EntityId(3);

        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Relationship(
                    new RelationshipId(1),
                    "Accounts",
                    account,
                    RelationshipCardinality.Many))
            .Entity(account, "Account", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Balance", typeof(decimal))
                .Relationship(
                    new RelationshipId(2),
                    "Transactions",
                    transaction,
                    RelationshipCardinality.Many))
            .Entity(transaction, "Transaction", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Amount", typeof(decimal)))
            .Build();

        Assert.Equal(3, model.Entities.Count);
        Assert.Equal("Customer", model.Get(customer).Name);
        Assert.Equal(account, model.Get(customer).Relationships.Single().Target);
        Assert.Equal(transaction, model.Get(account).Relationships.Single().Target);
    }


    [Fact]
    public void Typed_manual_semantics_derive_fields_from_domain_model_properties()
    {
        var product = new EntityId(100);

        var model = new SemanticModelBuilder()
            .Entity<TestProduct>(product, "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Sku)
                .Field(x => x.Name)
                .Field(x => x.Price))
            .Build();

        var entity = model.Get(product);

        Assert.Equal(FieldId.Create("Product", "Id"), entity.Identity.FieldId);
        Assert.Equal("Id", entity.Identity.Name);
        Assert.Collection(
            entity.Fields,
            field =>
            {
                Assert.Equal(FieldId.Create("Product", "Sku"), field.Id);
                Assert.Equal("Sku", field.Name);
                Assert.Equal(typeof(string), field.ClrType);
            },
            field =>
            {
                Assert.Equal(FieldId.Create("Product", "Name"), field.Id);
                Assert.Equal("Name", field.Name);
                Assert.Equal(typeof(string), field.ClrType);
            },
            field =>
            {
                Assert.Equal(FieldId.Create("Product", "Price"), field.Id);
                Assert.Equal("Price", field.Name);
                Assert.Equal(typeof(decimal), field.ClrType);
            });
    }

    [Fact]
    public void Typed_manual_field_ids_are_stable_when_field_declaration_order_changes()
    {
        var first = new SemanticModelBuilder()
            .Entity<TestProduct>(new EntityId(103), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Sku)
                .Field(x => x.Name)
                .Field(x => x.Price))
            .Build();

        var reordered = new SemanticModelBuilder()
            .Entity<TestProduct>(new EntityId(104), "Product", e => e
                .Identity(x => x.Id)
                .Field(x => x.Price)
                .Field(x => x.Name)
                .Field(x => x.Sku))
            .Build();

        var firstName = first.Get(new EntityId(103)).Fields.Single(x => x.Name == "Name");
        var reorderedName = reordered.Get(new EntityId(104)).Fields.Single(x => x.Name == "Name");

        Assert.Equal(firstName.Id, reorderedName.Id);
        Assert.Equal(FieldId.Create("Product", "Name"), firstName.Id);
    }


    [Fact]
    public void Typed_manual_selectors_use_model_properties_not_storage_entity_properties()
    {
        var product = new EntityId(102);

        var model = new SemanticModelBuilder()
            .Entity<TestProduct>(product, "Product", entity => entity
                .Identity(model => model.Id)
                .Field(model => model.Price))
            .Build();

        Assert.NotEqual(typeof(TestProductEntity), typeof(TestProduct));
        Assert.Equal(typeof(decimal), Assert.Single(model.Get(product).Fields).ClrType);
    }

    [Fact]
    public void Typed_manual_identity_can_preserve_a_semantic_name_when_domain_property_differs()
    {
        var component = new EntityId(101);

        var model = new SemanticModelBuilder()
            .Entity<TestProductComponent>(component, "ProductComponent", e => e
                .Identity(x => x.ParentProductId, "Id")
                .Field(x => x.ParentProductId)
                .Field(x => x.ComponentProductId))
            .Build();

        var entity = model.Get(component);

        Assert.Equal(FieldId.Create("ProductComponent", "Id"), entity.Identity.FieldId);
        Assert.Equal("Id", entity.Identity.Name);
        Assert.Equal("ParentProductId", entity.Fields[0].Name);
        Assert.Equal(typeof(int), entity.Fields[0].ClrType);
    }


    [Fact]
    public void Metadata_discovery_builds_structural_semantics_without_manual_entity_registration()
    {
        var registry = new MetadataRegistry();
        var customer = new EntityId(200);
        var order = new EntityId(201);
        var customerId = new FieldId(1);
        var orderId = new FieldId(2);
        var customerPk = new ColumnId(1);
        var orderPk = new ColumnId(2);
        var relationship = new RelationshipId(200);

        registry.Register(new EntityMetadata(
            customer,
            "Customer",
            [new ColumnMetadata(customerPk, "Id")],
            PrimaryKey: new ColumnReference(customer, customerPk),
            Fields:
            [
                new FieldMetadata(customerId, "Id", typeof(int), new ColumnReference(customer, customerPk)),
                new FieldMetadata(new FieldId(3), "Name", typeof(string))
            ],
            ClrType: typeof(TestCustomer)));

        registry.Register(new EntityMetadata(
            order,
            "Order",
            [new ColumnMetadata(orderPk, "Id")],
            PrimaryKey: new ColumnReference(order, orderPk),
            Fields:
            [
                new FieldMetadata(orderId, "Id", typeof(int), new ColumnReference(order, orderPk))
            ],
            ClrType: typeof(TestOrder)));

        registry.Register(new RelationshipMetadata(
            relationship,
            customer,
            order,
            "Orders",
            new ColumnReference(customer, customerPk),
            new ColumnReference(order, orderPk),
            IsCollection: true));

        var model = registry.Discover();

        Assert.Equal(typeof(TestCustomer), model.Get(customer).ModelType);
        Assert.Equal(customerId, model.Get(customer).Identity.FieldId);
        Assert.Contains(model.Get(customer).Fields, field => field.Name == "Name");
        var discoveredRelationship = Assert.Single(model.Get(customer).Relationships);
        Assert.Equal("Orders", discoveredRelationship.Name);
        Assert.Equal(RelationshipCardinality.Many, discoveredRelationship.Cardinality);
    }

    [Fact]
    public void Metadata_discovery_can_be_enriched_with_logical_traversals()
    {
        var registry = new MetadataRegistry();
        var customer = new EntityId(210);
        var relationshipEntity = new EntityId(211);
        var contract = new EntityId(212);
        var transaction = new EntityId(213);

        RegisterEntity(registry, customer, "Customer", 210);
        RegisterEntity(registry, relationshipEntity, "CustomerRelationship", 211);
        RegisterEntity(registry, contract, "Contract", 212);
        RegisterEntity(registry, transaction, "Transaction", 213);

        registry.Register(new RelationshipMetadata(new RelationshipId(210), customer, relationshipEntity,
            "Relationships",
            new ColumnReference(customer, new ColumnId(210)),
            new ColumnReference(relationshipEntity, new ColumnId(211))));
        registry.Register(new RelationshipMetadata(new RelationshipId(211), relationshipEntity, contract, "Contract",
            new ColumnReference(relationshipEntity, new ColumnId(211)),
            new ColumnReference(contract, new ColumnId(212))));
        registry.Register(new RelationshipMetadata(new RelationshipId(212), contract, transaction, "Transactions",
            new ColumnReference(contract, new ColumnId(212)), new ColumnReference(transaction, new ColumnId(213))));

        var model = registry.FromMetadata()
            .Traversal(customer, "transactions",
                new RelationshipId(210),
                new RelationshipId(211),
                new RelationshipId(212))
            .Build();

        var traversal = model.GetTraversal(customer, "transactions");
        Assert.Equal(transaction, traversal.Target);
        Assert.Equal(3, traversal.Path.Count);
    }

    private sealed record TestCustomer(int Id, string Name);

    private sealed record TestOrder(int Id);

    private static void RegisterEntity(MetadataRegistry registry, EntityId id, string name, ushort columnId)
    {
        var column = new ColumnId(columnId);
        registry.Register(new EntityMetadata(
            id,
            name,
            [new ColumnMetadata(column, "Id")],
            PrimaryKey: new ColumnReference(id, column),
            Fields:
            [
                new FieldMetadata(new FieldId(columnId), "Id", typeof(int), new ColumnReference(id, column))
            ]));
    }

    private sealed record TestProduct(int Id, string Sku, string Name, decimal Price);

    // Deliberately different from TestProduct to prove the manual selector is
    // rooted in the application model, not a persistence/entity metadata type.
    private sealed record TestProductEntity(int Id, string Sku, string Name, string Price);

    private sealed record TestProductComponent(int ParentProductId, int ComponentProductId);

    private sealed record TestMismatchedComponent(Guid ParentProductId);

    [Fact]
    public void Typed_relationship_selectors_bind_both_domain_model_sides()
    {
        var product = new EntityId(110);
        var component = new EntityId(111);

        var model = new SemanticModelBuilder()
            .Entity<TestProduct>(product, "Product", e => e
                .Identity(x => x.Id))
            .Entity<TestProductComponent>(component, "ProductComponent", e => e
                .Identity(x => x.ParentProductId, "Id"))
            .Relationship<TestProduct, TestProductComponent>(
                product,
                new RelationshipId(1),
                "components",
                productModel => productModel.Id,
                component,
                componentModel => componentModel.ParentProductId,
                RelationshipCardinality.Many)
            .Build();

        Assert.Equal(component, Assert.Single(model.Get(product).Relationships).Target);
    }

    [Fact]
    public void Typed_relationship_rejects_mismatched_model_property_types()
    {
        var product = new EntityId(112);
        var component = new EntityId(113);

        Assert.Throws<ArgumentException>(() =>
            new SemanticModelBuilder()
                .Entity<TestProduct>(product, "Product", e => e
                    .Identity(x => x.Id))
                .Entity<TestMismatchedComponent>(component, "MismatchedComponent", e => e
                    .Identity(x => x.ParentProductId))
                .Relationship<TestProduct, TestMismatchedComponent>(
                    product,
                    new RelationshipId(1),
                    "components",
                    productModel => productModel.Id,
                    component,
                    componentModel => componentModel.ParentProductId,
                    RelationshipCardinality.Many)
                .Build());
    }

    [Fact]
    public void Request_graph_is_provider_independent()
    {
        var graph = new SemanticGraph();
        var customer = graph.AddRoot(new EntityId(1));
        var account = graph.Add(
            new EntityId(2),
            new RelationshipId(1),
            customer);
        var transaction = graph.Add(
            new EntityId(3),
            new RelationshipId(2),
            account);

        Assert.Equal(3, graph.Nodes.Count);
        Assert.Null(customer.ParentId);
        Assert.Equal(customer.Id, account.ParentId);
        Assert.Equal(account.Id, transaction.ParentId);
    }

    [Fact]
    public void Entity_ids_are_deterministic_and_order_independent()
    {
        var customer = EntityId.Create("Customer");
        var account = EntityId.Create("Account");

        Assert.Equal(customer, EntityId.Create("Customer"));
        Assert.Equal(account, EntityId.Create("Account"));
        Assert.NotEqual(customer, account);
    }


    [Fact]
    public void Snapshot_requires_an_explicitly_frozen_model()
    {
        var model = new SemanticModelBuilder()
            .Entity<TestCustomer>(new EntityId(301), "Customer", e => e.Identity(x => x.Id))
            .Build();

        Assert.Throws<InvalidOperationException>(() => model.CreateSnapshot());
    }

    [Fact]
    public void Snapshot_preserves_fingerprint_and_is_independent_of_model_state()
    {
        var customer = new EntityId(302);
        var order = new EntityId(303);
        var model = new SemanticModelBuilder()
            .Entity<TestCustomer>(customer, "Customer", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name))
            .Entity<TestOrder>(order, "Order", e => e.Identity(x => x.Id))
            .Relationship<TestCustomer, TestOrder>(
                customer,
                new RelationshipId(302),
                "ordersRelationship",
                x => x.Id,
                order,
                x => x.Id,
                RelationshipCardinality.Many)
            .Traversal(customer, "orders", new RelationshipId(302))
            .Build()
            .Freeze();

        var snapshot = model.CreateSnapshot();

        Assert.Equal(model.ContractFingerprint, snapshot.ContractFingerprint);
        Assert.Equal("Customer", snapshot.Get(customer).Name);
        Assert.Equal("Name", Assert.Single(snapshot.Get(customer).Fields, x => x.Name == "Name").Name);
        Assert.Equal(order, snapshot.GetTraversal(customer, "orders").Target);
        Assert.Throws<NotSupportedException>(() => ((IList<SemanticTraversal>)snapshot.Traversals)[0] = null!);
    }

    [Fact]
    public void Snapshot_defensively_copies_nested_collections()
    {
        var customer = new EntityId(304);
        var model = new SemanticModelBuilder()
            .Entity<TestCustomer>(customer, "Customer", e => e
                .Identity(x => x.Id)
                .Field(x => x.Name)
                .FieldAlias(x => x.Name, "display")
                .Constraint(x => x.Name, SemanticConstraint.Pattern(".+")))
            .Build()
            .Freeze();

        var snapshot = model.CreateSnapshot();
        var entity = snapshot.Get(customer);
        var field = Assert.Single(entity.Fields);

        Assert.Throws<NotSupportedException>(() => ((IList<SemanticField>)entity.Fields).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SemanticAlias>)field.Aliases!)[0] = new SemanticAlias("changed"));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SemanticConstraint>)field.Constraints!)[0] = SemanticConstraint.Pattern("changed"));
    }
}