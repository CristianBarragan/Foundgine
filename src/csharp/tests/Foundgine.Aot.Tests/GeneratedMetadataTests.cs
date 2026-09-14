using System.Linq;
using System.Linq.Expressions;
using Foundgine.Providers.Aot;
using Foundgine.Generated;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Xunit;

namespace Foundgine.Providers.Aot.Tests;

[FoundgineEntity(Id = 1, StorageName = "customers")]
[FoundgineAlias("Client")]
public sealed class Customer
{
    [FoundgineField(Id = 1, StorageName = "id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField(Id = 2, StorageName = "name")]
    [FoundgineAlias("DisplayName")]
    public string Name { get; init; } = string.Empty;

    [FoundgineRelationship(typeof(Account), "CustomerId", "Id", Id = 1, Name = "Accounts")]
    [FoundgineAlias("CustomerAccounts")]
    public IReadOnlyList<Account> Accounts { get; init; } = [];
}

[FoundgineEntity(Id = 2, StorageName = "accounts")]
public sealed class Account
{
    [FoundgineField(Id = 1, StorageName = "id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField(Id = 2, StorageName = "customer_id")]
    public int CustomerId { get; init; }

    [FoundgineRelationship(typeof(Customer), "CustomerId", "Id", Id = 2, Name = "Customer")]
    public Customer Customer { get; init; } = null!;
}

public enum ProductType
{
    CreditCard,
    Mortgage,
    PersonalLoan
}

public enum ContractType
{
    CreditCard,
    Mortgage,
    PersonalLoan
}

public static class ProductConversions
{
    [FoundgineConversion(typeof(ProductType), typeof(ContractType))]
    public static ContractType ToContractType(ProductType value) => value switch
    {
        ProductType.CreditCard => ContractType.CreditCard,
        ProductType.Mortgage => ContractType.Mortgage,
        ProductType.PersonalLoan => ContractType.PersonalLoan,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

[FoundgineModel(Id = 10)]
public sealed class Product
{
    public int Id { get; init; }
    public ProductType ProductType { get; init; }

    [FoundgineConnection(typeof(Contract), Id = 10, Name = "Contract")]
    public static Expression<Func<Product, object>> ContractProjection =>
        product => new
        {
            product.Id,
            ContractType = ProductConversions.ToContractType(product.ProductType)
        };
}

public sealed class UserContext
{
    public int TenantId { get; init; }
}

public static class ProductAuthorization
{
    [FoundgineAuthorization(10, Id = 10, Name = "CanVisitContract")]
    public static Expression<Func<UserContext, Contract, bool>> CanVisitContract =>
        (user, contract) => user.TenantId == contract.TenantId;
}

[FoundgineEntity(Id = 3, StorageName = "contracts")]
public sealed class Contract
{
    [FoundgineField(Id = 1, StorageName = "id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField(Id = 2, StorageName = "contract_type")]
    public ContractType ContractType { get; init; }

    [FoundgineField(Id = 3, StorageName = "tenant_id")]
    public int TenantId { get; init; }
}

[FoundgineModel(Id = 20)]
public sealed class DecoupledCustomer
{
    public int Id { get; init; }

    [FoundgineConnection(Id = 20, Name = "Orders")]
    public object Orders => throw new NotSupportedException();
}

[FoundgineEntity("DecoupledCustomerERP", StorageName = "decoupled_customers", Id = 20)]
public sealed class DecoupledCustomerERP
{
    [FoundgineField(Id = 1, StorageName = "customer_id", IsPrimaryKey = true)]
    public int Id { get; init; }
}

[FoundgineEntity("DecoupledOrderERP", StorageName = "decoupled_orders", Id = 21)]
public sealed class DecoupledOrderERP
{
    [FoundgineField(Id = 1, StorageName = "order_id", IsPrimaryKey = true)]
    public int Id { get; init; }
}

[FoundgineModelEntityMap(typeof(DecoupledCustomer), typeof(DecoupledCustomerERP))]
[FoundgineConnectionMap(typeof(DecoupledCustomer), nameof(DecoupledCustomer.Orders), typeof(DecoupledOrderERP))]
internal static class DecoupledSchemaMap
{
}

[FoundgineEntity(Id = 30, StorageName = "implicit_columns")]
public sealed class ImplicitColumnIdentityEntity
{
    [FoundgineField(Id = 301, ColumnId = 401, StorageName = "id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField(Id = 302, ColumnId = 402, StorageName = "value")]
    public string Value { get; init; } = string.Empty;
}

// Distinct from ImplicitColumnIdentityEntity above: that entity declares
// explicit ColumnId values (to test FieldId/ColumnId independence). This
// entity declares no explicit ColumnId at all, so its column ids must be
// derived from the stable content hash of (storage name, column name),
// independent of declaration order.
[FoundgineEntity(Id = 31, StorageName = "implicit_columns")]
public sealed class StableColumnIdEntity
{
    [FoundgineField(StorageName = "id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField(StorageName = "value")]
    public string Value { get; init; } = string.Empty;
}

[FoundgineEntity(StorageName = "identity_regression_parents")]
public sealed class IdentityRegressionParent
{
    [FoundgineField(StorageName = "id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineRelationship(typeof(IdentityRegressionChild), "ParentId", "Id")]
    public IReadOnlyList<IdentityRegressionChild> Children { get; init; } = [];
}

[FoundgineEntity(StorageName = "identity_regression_children")]
public sealed class IdentityRegressionChild
{
    [FoundgineField(StorageName = "id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField(StorageName = "parent_id")]
    public int ParentId { get; init; }
}

// Exercises FoundgineAliasAttribute's params/collection-expression form
// (multiple names in a single application) alongside the classic stacked
// form, both on the class and on a member, including a duplicate across
// the two forms to confirm the generator dedupes case-insensitively.
[FoundgineEntity(Id = 40, StorageName = "sales_orders")]
[FoundgineAlias(["SalesOrder", "Order"])]
[FoundgineAlias("order")]
public sealed class SalesOrderFixture
{
    [FoundgineField(Id = 1, StorageName = "id", IsPrimaryKey = true)]
    public int Id { get; init; }

    [FoundgineField(Id = 2, StorageName = "status")]
    [FoundgineAlias("State", "Stage")]
    public string Status { get; init; } = string.Empty;
}

[FoundgineModel(Id = 50, MinimumWeight = 80)]
public sealed class WeightedAliasModel
{
    public int Id { get; init; }
}

[FoundgineModelEntityMap(typeof(WeightedAliasModel), typeof(Customer))]
internal static class WeightedAliasSchemaMap
{
}

public sealed class GeneratedMetadataTests
{
    [Fact]
    public void Generated_semantic_contract_fingerprint_matches_runtime_discovery()
    {
        var model = GeneratedMetadata.Registry.Discover();

        Assert.Equal(model.ContractFingerprint, GeneratedSemanticModel.ContractFingerprint);
        Assert.True(Foundgine.Core.Semantic.SemanticContractAttestation.Matches(
            model, GeneratedSemanticModel.ContractFingerprint));
    }

    [Fact]
    public void Aot_aliases_reach_semantic_model_discovery_and_preserve_ids()
    {
        var model = GeneratedMetadata.Registry.Discover();
        var customer = model.ResolveEntity("Client");

        Assert.Equal(new EntityId(1), customer.Id);
        Assert.Equal("Customer", customer.Name);
        Assert.Equal(["Client"], customer.EffectiveAliases.Select(x => x.Name));
        Assert.Equal(["DisplayName"],
            customer.Fields.Single(x => x.Id == new FieldId(2)).EffectiveAliases.Select(x => x.Name));
        Assert.Equal(["CustomerAccounts"],
            customer.Relationships.Single(x => x.Id == new RelationshipId(1)).EffectiveAliases.Select(x => x.Name));
    }

    [Fact]
    public void Explicit_field_id_and_column_id_remain_independent()
    {
        var entity = GeneratedMetadata.Registry.GetEntity(new EntityId(30));
        Assert.Equal(new FieldId(301), entity.EffectiveFields.Single(x => x.Name == "Id").Id);
        Assert.Equal(new ColumnId(401), entity.EffectiveFields.Single(x => x.Name == "Id").Column!.ColumnId);
        Assert.Equal(new FieldId(302), entity.EffectiveFields.Single(x => x.Name == "Value").Id);
        Assert.Equal(new ColumnId(402), entity.EffectiveFields.Single(x => x.Name == "Value").Column!.ColumnId);
    }

    [Fact]
    public void Generator_emits_entities_and_storage_mappings()
    {
        var customer = GeneratedMetadata.Registry.GetEntity(new EntityId(1));
        Assert.Equal("Customer", customer.Name);
        Assert.Equal("customers", customer.EffectiveStorageName);
        Assert.Equal("id", customer.Columns.Single(x => x.EffectiveStorageName == "id").EffectiveStorageName);
        Assert.Equal("name", customer.Columns.Single(x => x.EffectiveStorageName == "name").EffectiveStorageName);
        Assert.Equal(customer.Columns.Single(x => x.EffectiveStorageName == "id").Id,
            customer.EffectiveFields.Single(x => x.Name == "Id").Column!.ColumnId);
        Assert.Equal(customer.Columns.Single(x => x.EffectiveStorageName == "id").Id, customer.PrimaryKey!.ColumnId);
    }

    [Fact]
    public void Generator_propagates_aot_aliases_without_changing_identity()
    {
        var customer = GeneratedMetadata.Registry.GetEntity(new EntityId(1));
        Assert.Equal(["Client"], customer.Aliases?.Select(a => a.Name));
        Assert.Equal(["DisplayName"],
            customer.EffectiveFields.Single(x => x.Id == new FieldId(2)).Aliases?.Select(a => a.Name));

        var relationship = GeneratedMetadata.Registry.GetRelationship(new RelationshipId(1));
        Assert.Equal(["CustomerAccounts"], relationship.Aliases?.Select(a => a.Name));
    }

    [Fact]
    public void Generator_propagates_model_minimum_alias_weight_and_entity_mapping()
    {
        var model = GeneratedMetadata.Registry.GetModel(new ModelId(50));

        Assert.Equal("WeightedAliasModel", model.Name);
        Assert.Equal(new EntityId(1), model.Entity);
        Assert.Equal(80, model.MinimumWeight);
    }

    [Fact]
    public void FoundgineAlias_accepts_multiple_names_in_a_single_application()
    {
        var entity = GeneratedMetadata.Registry.GetEntity(new EntityId(40));

        // ["SalesOrder", "Order"] declared together, plus a separately
        // stacked [FoundgineAlias("order")] that only differs by case -
        // the generator must merge both forms and dedupe case-insensitively,
        // keeping first-seen order.
        Assert.Equal(["SalesOrder", "Order"], entity.Aliases?.Select(a => a.Name));

        var status = entity.EffectiveFields.Single(x => x.Name == "Status");
        Assert.Equal(["State", "Stage"], status.Aliases?.Select(a => a.Name));

        // Multi-name aliases resolve through SemanticModelDiscovery exactly
        // like the classic single-name form.
        var model = GeneratedMetadata.Registry.Discover();
        var resolved = model.ResolveEntity("Order");
        Assert.Equal(entity.EntityId, resolved.Id);
        Assert.Contains(resolved.EffectiveAliases, a => a.Name == "SalesOrder");
        Assert.Contains(resolved.Fields.Single(x => x.Name == "Status").EffectiveAliases, a => a.Name == "Stage");
    }

    [Fact]
    public void Generator_emits_relationship_join_mapping()
    {
        var relationship = GeneratedMetadata.Registry.GetRelationship(new RelationshipId(1));
        Assert.Equal(new EntityId(1), relationship.Source);
        Assert.Equal(new EntityId(2), relationship.Target);
        Assert.Equal(new EntityId(1), relationship.SourceKey.EntityId);
        Assert.Equal(
            GeneratedMetadata.Registry.GetEntity(new EntityId(1)).Columns.Single(x => x.EffectiveStorageName == "id")
                .Id, relationship.SourceKey.ColumnId);
        Assert.Equal(new EntityId(2), relationship.TargetKey.EntityId);
        Assert.Equal(
            GeneratedMetadata.Registry.GetEntity(new EntityId(2)).Columns
                .Single(x => x.EffectiveStorageName == "customer_id").Id, relationship.TargetKey.ColumnId);
    }

    [Fact]
    public void Generator_emits_correct_keys_when_source_owns_foreign_key()
    {
        var relationship = GeneratedMetadata.Registry.GetRelationship(new RelationshipId(2));
        Assert.Equal(new EntityId(2), relationship.Source);
        Assert.Equal(new EntityId(1), relationship.Target);
        Assert.Equal(new EntityId(2), relationship.SourceKey.EntityId);
        Assert.Equal(
            GeneratedMetadata.Registry.GetEntity(new EntityId(2)).Columns
                .Single(x => x.EffectiveStorageName == "customer_id").Id, relationship.SourceKey.ColumnId);
        Assert.Equal(new EntityId(1), relationship.TargetKey.EntityId);
        Assert.Equal(
            GeneratedMetadata.Registry.GetEntity(new EntityId(1)).Columns.Single(x => x.EffectiveStorageName == "id")
                .Id, relationship.TargetKey.ColumnId);
    }

    [Fact]
    public void Generated_provider_implements_runtime_contract()
    {
        IMetadataProvider provider = new GeneratedMetadataProvider();
        Assert.Equal("Account", provider.GetEntity(new EntityId(2)).Name);
        Assert.Equal("Product", provider.GetModel(new ModelId(10)).Name);
        Assert.Equal(new EntityId(3), provider.GetConnection(new ConnectionId(10)).Target);
    }

    [Fact]
    public void Generator_emits_model_connections_without_materialization_contracts()
    {
        var model = GeneratedMetadata.Registry.Get(new ModelId(10));
        Assert.Equal("Product", model.Name);

        var connection = GeneratedMetadata.Registry.Get(new ConnectionId(10));
        Assert.Equal(new ModelId(10), connection.Source);
        Assert.Equal(new EntityId(3), connection.Target);
        Assert.Equal("Contract", connection.Name);
        Assert.Equal(nameof(Product.ContractProjection), connection.SourceMember);
    }


    [Fact]
    public void Connection_uses_convention_fields_and_aot_enum_conversion()
    {
        var connection = GeneratedMetadata.Registry.GetConnection(new ConnectionId(10));

        Assert.NotNull(connection.Fields);
        Assert.Contains(connection.Fields!, x =>
            x.SourceMember == nameof(Product.Id) &&
            x.TargetMember == nameof(Contract.Id) &&
            x.Converter is null);

        var enumField = Assert.Single(connection.Fields!, x =>
            x.SourceMember == nameof(Product.ProductType) &&
            x.TargetMember == nameof(Contract.ContractType));

        Assert.Equal(typeof(ProductType), enumField.SourceType);
        Assert.Equal(typeof(ContractType), enumField.TargetType);
        Assert.Contains(nameof(ProductConversions.ToContractType), enumField.Converter);
    }

    [Fact]
    public void Connection_expression_is_anonymous_projection_not_entity_construction()
    {
        var connection = GeneratedMetadata.Registry.GetConnection(new ConnectionId(10));

        Assert.Equal(nameof(Product.ContractProjection), connection.SourceMember);
        Assert.Equal(2, connection.Fields!.Count);
        Assert.Contains(connection.Fields!, x =>
            x.SourceMember == nameof(Product.Id) &&
            x.TargetMember == nameof(Contract.Id) &&
            x.Converter is null);
        Assert.Contains(connection.Fields!, x =>
            x.SourceMember == nameof(Product.ProductType) &&
            x.TargetMember == nameof(Contract.ContractType) &&
            x.Converter is not null);
    }

    [Fact]
    public void Authorization_expression_is_emitted_as_aot_metadata()
    {
        var authorization = GeneratedMetadata.Registry.GetAuthorization(new AuthorizationId(10));

        Assert.Equal(new ConnectionId(10), authorization.ConnectionId);
        Assert.Equal(typeof(UserContext), authorization.ContextType);
        Assert.Equal(typeof(Contract), authorization.ResourceType);
        Assert.Equal(nameof(ProductAuthorization.CanVisitContract), authorization.SourceMember);
        Assert.Contains("user.TenantId == contract.TenantId", authorization.Expression);
        Assert.NotNull(authorization.Predicate);
        Assert.Equal(AuthorizationPredicateKind.Equal, authorization.Predicate!.Kind);
        Assert.Equal(AuthorizationPredicateKind.MemberAccess, authorization.Predicate.Left!.Kind);
        Assert.Equal("TenantId", authorization.Predicate.Left.Name);
        Assert.Equal(AuthorizationPredicateKind.ContextParameter, authorization.Predicate.Left.Left!.Kind);
        Assert.Equal("user", authorization.Predicate.Left.Left.Name);
        Assert.Equal(AuthorizationPredicateKind.MemberAccess, authorization.Predicate.Right!.Kind);
        Assert.Equal("TenantId", authorization.Predicate.Right.Name);
        Assert.Equal(AuthorizationPredicateKind.ResourceParameter, authorization.Predicate.Right.Left!.Kind);
        Assert.Equal("contract", authorization.Predicate.Right.Left.Name);
    }

    [Fact]
    public void Explicit_model_entity_mapping_keeps_model_and_erp_types_distinct()
    {
        var model = GeneratedMetadata.Registry.GetModel(new ModelId(20));
        Assert.Equal("DecoupledCustomer", model.Name);
        Assert.Equal(new EntityId(20), model.Entity);

        var entity = GeneratedMetadata.Registry.GetEntity(new EntityId(20));
        Assert.Equal("DecoupledCustomerERP", entity.Name);
    }

    [Fact]
    public void Explicit_connection_mapping_resolves_target_without_model_storage_reference()
    {
        var connection = GeneratedMetadata.Registry.GetConnection(new ConnectionId(20));
        Assert.Equal(new ModelId(20), connection.Source);
        Assert.Equal(new EntityId(21), connection.Target);
        Assert.Equal("Orders", connection.Name);
    }

    [Fact]
    public void Conversion_is_metadata_not_runtime_mapping()
    {
        var conversion = GeneratedMetadata.Registry.FindConversion(
            typeof(ProductType),
            typeof(ContractType));

        Assert.NotNull(conversion);
        Assert.Contains(nameof(ProductConversions.ToContractType), conversion!.Method);
    }

    [Fact]
    public void Generator_derives_stable_physical_column_ids_without_declaration_order()
    {
        var entity = GeneratedMetadata.Registry.GetEntity(new EntityId(31));

        Assert.Equal(SemanticIdentity.Hash(SemanticIdentity.ColumnKey("implicit_columns", "id")),
            entity.Columns.Single(x => x.EffectiveStorageName == "id").Id.Value);
        Assert.Equal(SemanticIdentity.Hash(SemanticIdentity.ColumnKey("implicit_columns", "value")),
            entity.Columns.Single(x => x.EffectiveStorageName == "value").Id.Value);
    }


    [Fact]
    public void Aot_generated_automatic_ids_match_runtime_canonical_identity()
    {
        var parent = GeneratedMetadata.Registry.GetEntity(
            EntityId.Create("IdentityRegressionParent"));

        Assert.Equal(
            SemanticIdentity.Hash(SemanticIdentity.EntityKey("IdentityRegressionParent")),
            parent.EntityId.Value);

        Assert.Equal(
            SemanticIdentity.Hash(SemanticIdentity.FieldKey("IdentityRegressionParent", "Id")),
            parent.EffectiveFields.Single(x => x.Name == "Id").Id.Value);

        Assert.Equal(
            SemanticIdentity.Hash(SemanticIdentity.ColumnKey("identity_regression_parents", "id")),
            parent.Columns.Single(x => x.EffectiveStorageName == "id").Id.Value);

        var relationship = GeneratedMetadata.Registry.GetRelationship(
            RelationshipId.Create("IdentityRegressionParent", "Children"));

        Assert.Equal(
            SemanticIdentity.Hash(SemanticIdentity.RelationshipKey("IdentityRegressionParent", "Children")),
            relationship.Id.Value);
    }
}