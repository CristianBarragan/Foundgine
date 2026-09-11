package com.foundgine.core.serialization;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/** Mirrors the C# malicious JSON intent boundary tests. */
class JsonReadIntentSecurityParityTest {
    @Test
    void agentCannotManufactureSecurityOrProviderAuthority() {
        var adapter = new JsonReadIntentAdapter();
        String[] forbidden = {
                "tenantId", "subject", "audience", "warrant", "authorization",
                "capabilityId", "provider", "connectionString", "sql"
        };

        for (String property : forbidden) {
            var json = "{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\""
                    + property + "\":\"attacker-controlled\"}";
            var exception = assertThrows(IllegalStateException.class, () -> adapter.parse(json), property);
            assertTrue(exception.getMessage().contains("Unrecognized property"), exception.getMessage());
            assertTrue(exception.getMessage().contains(property), exception.getMessage());
        }
    }

    @Test
    void hostileTenantFilterRemainsUntrustedSemanticInput() {
        var intent = new JsonReadIntentAdapter().parse("""
                {
                  "rootEntity":"Customer",
                  "selections":[{"field":"Id"}],
                  "filter":{
                    "kind":"field",
                    "field":"TenantId",
                    "operator":"EQ",
                    "value":"victim-tenant"
                  }
                }
                """);

        assertNull(intent.security());
        assertEquals("Customer", intent.rootEntity());
    }

    @Test
    void maliciousPredicateStructureIsBoundedBeforePlanning() {
        var adapter = new JsonReadIntentAdapter(
                new JsonReadIntentAdapterOptions(32, 256, 4, 8, 16, true));
        String json = """
                {
                  "rootEntity":"Customer",
                  "selections":[{"field":"Id"}],
                  "filter":{
                    "kind":"or","expressions":[
                      {"kind":"or","expressions":[
                        {"kind":"or","expressions":[
                          {"kind":"or","expressions":[
                            {"kind":"field","field":"Id","operator":"EQ","value":1}
                          ]}
                        ]}
                      ]
                    ]
                  }
                }
                """;

        var exception = assertThrows(IllegalStateException.class, () -> adapter.parse(json));
        assertTrue(exception.getMessage().toLowerCase().contains("filter"), exception.getMessage());
    }
}
