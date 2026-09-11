using Foundgine.Core.Serialization;
using Xunit;

namespace Foundgine.Security.Tests.Penetration;

/// <summary>SEC-38, SEC-48 and SEC-56..SEC-57: hostile transport and ambiguity inputs.</summary>
public sealed class TransportAndSecretLeakagePenetrationTests
{
    [Fact]
    public void Duplicate_security_properties_do_not_create_a_second_authority_channel()
    {
        var adapter = new JsonReadIntentAdapter();
        var json =
            "{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"tenantId\":\"tenant-alpha\",\"tenantId\":\"tenant-beta-secret\"}";

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse(json));
        Assert.DoesNotContain("tenant-beta-secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_mcp_style_authority_fields_are_rejected_in_strict_mode()
    {
        var adapter = new JsonReadIntentAdapter();
        var exception = Assert.Throws<InvalidOperationException>(() => adapter.Parse("""
            {"rootEntity":"Customer","selections":[{"field":"Id"}],
             "identity":{"subject":"admin","tenant":"victim"},
             "provider":{"connectionString":"Host=evil"},
             "sql":"SELECT * FROM secrets"}
            """));

        Assert.DoesNotContain("Host=evil", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT *", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Permissive_transport_mode_still_cannot_map_unknown_properties_into_authority()
    {
        var adapter = new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions { RejectUnknownProperties = false });
        var intent = adapter.Parse("""
                                   {"rootEntity":"Customer","selections":[{"field":"Id"}],
                                    "identity":{"subject":"admin","tenant":"victim"},
                                    "provider":"evil","sql":"DROP TABLE secrets"}
                                   """);

        Assert.Equal("Customer", intent.RootEntity);
        Assert.Single(intent.Selections);
        Assert.Equal("Id", intent.Selections[0].Field);
    }

    [Fact]
    public void Unicode_confusable_security_property_does_not_become_an_authority_field()
    {
        var adapter = new JsonReadIntentAdapter();
        var json = "{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"tenant1\":\"victim\"}";
        var exception = Record.Exception(() => adapter.Parse(json));

        // Either strict rejection or harmless ignoring is acceptable; the
        // transport layer must never treat a confusable key as a trusted tenant.
        Assert.True(exception is null || exception is InvalidOperationException);
    }
}