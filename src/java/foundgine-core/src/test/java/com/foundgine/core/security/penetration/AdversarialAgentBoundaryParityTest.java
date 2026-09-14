package com.foundgine.core.security.penetration;

import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.foundgine.core.serialization.JsonReadIntentAdapter;
import com.foundgine.core.serialization.JsonReadIntentAdapterOptions;

import org.junit.jupiter.api.Test;

/**
 * Parity coverage for the agent-facing JSON boundary.
 *
 * <p>Structured intent is treated as hostile model output. Execution authority, tenant identity and
 * provider configuration are never accepted as intent data.
 */
class AdversarialAgentBoundaryParityTest {

    // C# InvalidOperationException maps to Java IllegalStateException for
    // transport/security-boundary and resource-limit rejections in the port.

    @Test
    void unknownExecutionControlPropertiesAreRejected() {
        var adapter = new JsonReadIntentAdapter();

        var json =
                """
                {
                  "rootEntity": "Customer",
                  "selections": [{"field": "Id"}],
                  "tenantId": "victim-tenant",
                  "provider": "postgres",
                  "authorization": "allow-all",
                  "connectionString": "Host=evil"
                }
                """;

        var exception = assertThrows(IllegalStateException.class, () -> adapter.parse(json));
        assertTrue(exception.getMessage().toLowerCase().contains("tenantid"));
    }

    @Test
    void selectionDepthIsBoundedBeforeSemanticResolution() {
        var adapter =
                new JsonReadIntentAdapter(
                        new JsonReadIntentAdapterOptions(3, 100, 32, 256, 16, true));

        var json =
                """
                {
                  "rootEntity": "Customer",
                  "selections": [
                    {"relationship":"Accounts","children":[
                      {"relationship":"Transactions","children":[
                        {"relationship":"Customer","children":[
                          {"field":"Id"}
                        ]}
                      ]}
                    ]}
                  ]
                }
                """;

        var exception = assertThrows(IllegalStateException.class, () -> adapter.parse(json));
        assertTrue(exception.getMessage().toLowerCase().contains("depth"));
    }

    @Test
    void selectionFanoutIsBoundedBeforePlanning() {
        var adapter =
                new JsonReadIntentAdapter(
                        new JsonReadIntentAdapterOptions(8, 4, 32, 256, 16, true));

        var json =
                """
                {
                  "rootEntity": "Customer",
                  "selections": [
                    {"field":"Field1"},
                    {"field":"Field2"},
                    {"field":"Field3"},
                    {"field":"Field4"},
                    {"field":"Field5"}
                  ]
                }
                """;

        var exception = assertThrows(IllegalStateException.class, () -> adapter.parse(json));
        assertTrue(exception.getMessage().toLowerCase().contains("selection count"));
    }

    @Test
    void filterDepthAndNodeCountAreBounded() {
        var adapter =
                new JsonReadIntentAdapter(
                        new JsonReadIntentAdapterOptions(32, 256, 3, 4, 16, true));

        var json =
                """
                {
                  "rootEntity": "Customer",
                  "selections": [{"field":"Id"}],
                  "filter": {
                    "kind":"relationship",
                    "relationship":"Accounts",
                    "quantifier":"some",
                    "predicate": {
                      "kind":"relationship",
                      "relationship":"Transactions",
                      "quantifier":"some",
                      "predicate": {
                        "kind":"relationship",
                        "relationship":"Customer",
                        "quantifier":"some",
                        "predicate": {
                          "kind":"field",
                          "field":"Id",
                          "operator":"Eq",
                          "value":1
                        }
                      }
                    }
                  }
                }
                """;

        var exception = assertThrows(IllegalStateException.class, () -> adapter.parse(json));
        assertTrue(exception.getMessage().toLowerCase().contains("filter"));
    }
}
