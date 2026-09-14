using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Xunit;

namespace Foundgine.Core.Semantic.Tests;

public sealed class SemanticIdentityTests
{
    [Fact]
    public void Identity_is_deterministic_for_each_namespace()
    {
        Assert.Equal(EntityId.Create("Customer"), EntityId.Create(" Customer "));
        Assert.Equal(FieldId.Create("Customer", "Name"), FieldId.Create("Customer", "Name"));
        Assert.Equal(RelationshipId.Create("Customer", "Accounts"), RelationshipId.Create("Customer", "Accounts"));
        Assert.Equal(ColumnId.Create("public.customers", "name"), ColumnId.Create("public.customers", "name"));
        Assert.Equal(StorageEntityId.Create("public.customers"), StorageEntityId.Create("public.customers"));
        Assert.Equal(ModelId.Create("CustomerView"), ModelId.Create("CustomerView"));
        Assert.Equal(ConnectionId.Create("CustomerView", "Customer"), ConnectionId.Create("CustomerView", "Customer"));
        Assert.Equal(AuthorizationId.Create("CustomerPolicy", "CanRead"),
            AuthorizationId.Create("CustomerPolicy", "CanRead"));
    }

    [Fact]
    public void Identity_namespaces_do_not_share_canonical_keys()
    {
        Assert.NotEqual(SemanticIdentity.EntityKey("Customer"), SemanticIdentity.TableKey("Customer"));
        Assert.NotEqual(SemanticIdentity.FieldKey("Customer", "Name"), SemanticIdentity.ColumnKey("Customer", "Name"));
    }

    [Fact]
    public void Zero_is_never_returned_by_stable_hash()
    {
        Assert.NotEqual(0UL, SemanticIdentity.Hash("test:anything"));
    }
}

public sealed class SemanticIdentityJsonTests
{
    [Theory]
    [InlineData(1UL)]
    [InlineData(4294967297UL)]
    public void All_identity_types_serialize_as_numeric_values_and_round_trip(ulong value)
    {
        var values = new object[]
        {
            new EntityId(value),
            new FieldId(value),
            new RelationshipId(value),
            new ColumnId(value),
            new StorageEntityId(value),
            new ModelId(value),
            new ConnectionId(value),
            new AuthorizationId(value)
        };

        foreach (var identity in values)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(identity, identity.GetType());
            Assert.Equal(value.ToString(System.Globalization.CultureInfo.InvariantCulture), json);
        }
    }

    [Fact]
    public void FieldId_reads_legacy_object_wire_format()
    {
        var result = System.Text.Json.JsonSerializer.Deserialize<FieldId>("{\"Value\":4294967297}");

        Assert.Equal(new FieldId(4294967297UL), result);
    }

    [Fact]
    public void EntityId_reads_legacy_object_wire_format()
    {
        var result = System.Text.Json.JsonSerializer.Deserialize<EntityId>("{\"Value\":4294967297}");

        Assert.Equal(new EntityId(4294967297UL), result);
    }

    [Fact]
    public void Dictionary_keyed_by_relationship_id_supports_64_bit_values()
    {
        var value = 4294967297UL;
        var source = new Dictionary<RelationshipId, string>
        {
            [new RelationshipId(value)] = "ok"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(source);
        var result = System.Text.Json.JsonSerializer.Deserialize<Dictionary<RelationshipId, string>>(json);

        Assert.Equal("ok", result![new RelationshipId(value)]);
    }

    [Fact]
    public void Explicit_zero_identity_is_rejected()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            SemanticIdentity.ValidateExplicitId(0, "field"));

        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extended_identity_namespaces_are_distinct()
    {
        Assert.NotEqual(
            SemanticIdentity.ModelKey("Orders"),
            SemanticIdentity.ConnectionKey("Orders", "Primary"));
        Assert.NotEqual(
            SemanticIdentity.ConnectionKey("Orders", "Primary"),
            SemanticIdentity.AuthorizationKey("Orders", "Primary"));
    }
}