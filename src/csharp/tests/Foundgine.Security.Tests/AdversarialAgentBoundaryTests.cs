using Foundgine.Core.Abstractions;
using Foundgine.Core.Serialization;
using Foundgine.Core.Semantic;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Core.Semantic.Capabilities;
using Xunit;

namespace Foundgine.Security.Tests;

/// <summary>
/// M17 adversarial harness for the agent-facing semantic boundary.
/// These tests deliberately treat structured intent as hostile model output.
/// No test grants the model authority over tenant, identity, provider or policy context.
/// </summary>
public sealed class AdversarialAgentBoundaryTests
{
    [Fact]
    public void Unknown_execution_control_properties_are_rejected()
    {
        var adapter = new JsonReadIntentAdapter();

        var json = """
                   {
                     "rootEntity": "Customer",
                     "selections": [{ "field": "Id" }],
                     "tenantId": "victim-tenant",
                     "provider": "postgres",
                     "authorization": "allow-all",
                     "connectionString": "Host=evil"
                   }
                   """;

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));

        Assert.Contains("Invalid JSON read intent", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenantId", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selection_depth_is_bounded_before_semantic_resolution()
    {
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions
        {
            MaxSelectionDepth = 3,
            MaxSelections = 100
        });

        var json = """
                   {
                     "rootEntity": "Customer",
                     "selections": [
                       { "relationship": "Accounts", "children": [
                         { "relationship": "Transactions", "children": [
                           { "relationship": "Customer", "children": [
                             { "field": "Id" }
                           ]}
                         ]}
                       ]}
                     ]
                   }
                   """;

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
        Assert.Contains("depth", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selection_fanout_is_bounded_before_planning()
    {
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions
        {
            MaxSelections = 4,
            MaxSelectionDepth = 8
        });

        var fields = string.Join(",", Enumerable.Range(1, 5).Select(i => $"{{\"field\":\"Field{i}\"}}"));
        var json = $"{{\"rootEntity\":\"Customer\",\"selections\":[{fields}]}}";

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
        Assert.Contains("selection count", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Filter_depth_and_node_count_are_bounded()
    {
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions
        {
            MaxFilterDepth = 3,
            MaxFilterNodes = 4
        });

        var json = """
                   {
                     "rootEntity": "Customer",
                     "selections": [{ "field": "Id" }],
                     "filter": {
                       "kind": "relationship",
                       "relationship": "Accounts",
                       "quantifier": "some",
                       "predicate": {
                         "kind": "relationship",
                         "relationship": "Transactions",
                         "quantifier": "some",
                         "predicate": {
                           "kind": "relationship",
                           "relationship": "Customer",
                           "quantifier": "some",
                           "predicate": {
                             "kind": "field",
                             "field": "Id",
                             "operator": "Eq",
                             "value": 1
                           }
                         }
                       }
                     }
                   }
                   """;

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
        Assert.Contains("filter", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capability_contract_does_not_grant_hidden_fields_or_write_effects_to_read_only_surface()
    {
        var customer = new EntityId(1);
        var model = new SemanticModelBuilder()
            .Entity(customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Field(new FieldId(3), "SecretScore", typeof(decimal)))
            .Build();

        var policy = new ReadOnlySecretPolicy(customer);
        var contract = SemanticCapabilityContractDiscovery.Describe(model, policy);
        var read = contract.Capabilities.Single(x => x.Id == "Customer.read");
        var write = contract.Capabilities.Single(x => x.Id == "Customer.write");

        Assert.DoesNotContain("Id", read.Fields);
        Assert.Contains("Name", read.Fields);
        Assert.DoesNotContain("SecretScore", read.Fields);
        Assert.False(read.HasSideEffects);
        Assert.True(read.IsIdempotent);
        Assert.False(write.Access.IsAllowed);
        Assert.Empty(write.Effects);
    }

    [Fact]
    public void Agent_control_values_are_not_part_of_canonical_intent()
    {
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions
        {
            RejectUnknownProperties = false
        });

        var intent = adapter.Parse("""
                                   {
                                     "rootEntity": "Customer",
                                     "selections": [{ "field": "Id" }],
                                     "tenantId": "attacker",
                                     "userId": "administrator",
                                     "provider": "SqlServer",
                                     "authorization": "allow",
                                     "sql": "DROP TABLE Customer"
                                   }
                                   """);

        Assert.Equal("Customer", intent.RootEntity);
        Assert.Single(intent.Selections);
        Assert.Equal("Id", intent.Selections[0].Field);
        Assert.Null(intent.Selections[0].Relationship);
    }

    private sealed class ReadOnlySecretPolicy(EntityId customer) : AllowAllSemanticAuthorizationPolicy
    {
        public override bool CanAccessField(EntityId entityId, FieldId fieldId) =>
            entityId == customer && fieldId != new FieldId(3);

        public override bool CanWriteEntity(EntityId entityId) => false;
    }
}