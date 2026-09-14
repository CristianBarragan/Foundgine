using Foundgine.Core.Serialization;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>Transport-boundary attacks: authority, provider and SQL syntax are not agent-controlled fields.</summary>
public sealed class JsonAndMcpBoundaryPenetrationTests
{
    [Fact]
    public void Agent_cannot_supply_tenant_authority_or_connection_string()
    {
        var adapter = new JsonReadIntentAdapter();
        var json = """
                   {
                     "rootEntity": "Customer",
                     "selections": [{ "field": "Id" }],
                     "tenantId": "victim",
                     "userId": "administrator",
                     "provider": "postgres",
                     "authorization": "allow-all",
                     "connectionString": "Host=evil",
                     "sql": "DROP TABLE Customer"
                   }
                   """;

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
        Assert.Contains("tenantId", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Relaxed_unknown_property_mode_still_discards_security_controls()
    {
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions { RejectUnknownProperties = false });
        var intent = adapter.Parse("""
                                   {
                                     "rootEntity": "Customer",
                                     "selections": [{ "field": "Id" }],
                                     "tenantId": "attacker",
                                     "userId": "admin",
                                     "authorization": "allow",
                                     "provider": "sqlserver",
                                     "sql": "DROP TABLE Customer"
                                   }
                                   """);

        Assert.Equal("Customer", intent.RootEntity);
        Assert.Single(intent.Selections);
        Assert.Equal("Id", intent.Selections[0].Field);
    }

    [Fact]
    public void Oversized_selection_fanout_is_rejected_at_transport_boundary()
    {
        var fields = string.Join(",", Enumerable.Range(1, 101).Select(i => $"{{\"field\":\"F{i}\"}}"));
        var json = $"{{\"rootEntity\":\"Customer\",\"selections\":[{fields}]}}";
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions { MaxSelections = 100 });

        Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
    }
}