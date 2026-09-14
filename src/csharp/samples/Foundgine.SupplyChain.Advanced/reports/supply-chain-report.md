# Supply Chain E2E

- Seed: 20260823
- Steps: 25
- Customers: 5
- Success: 25
- Failures: 0
- Unexpected unauthorized successes: 12
- Average latency: 21.9 ms

## Efficiency estimate

- Measured Foundgine tool calls: 25
- Measured Foundgine estimated context load: 1921 tokens
- Measured Foundgine average context load: 76.8 tokens/call
- Modeled conventional tool calls: 100
- Modeled conventional estimated context load: 7684 tokens
- Estimated tool-call reduction: 75%
- Estimated context-load reduction: 75%

> This run has no live conventional flow to compare against. The conventional side is modeled from the same discover/authorize/execute/verify choreography measured by Run1 and is not re-executed here.

## Security PenTest regression measurement

- Cases measured: 14
- Passed: 14
- Failed: 0
- Skipped: 0
- Suite wall time: 24935.8 ms
- Total case execution time: 28747.4 ms
- Average case time: 2053.4 ms

| Transport | Case | Outcome | Duration |
| --- | --- | --- | ---: |
| MCP | Foundgine.SupplyChain.PenTest.Tests.McpPenetrationTests.No_credentials_are_rejected | Passed | 20.1 ms |
| MCP | Foundgine.SupplyChain.PenTest.Tests.McpPenetrationTests.Injection_payload_in_tracking_number_is_stored_as_literal_text_not_executed | Passed | 147.5 ms |
| GraphQL | Foundgine.SupplyChain.PenTest.Tests.GraphPenetrationTests.Customer_cannot_escalate_to_a_warehouse_only_capability | Passed | 50 ms |
| MCP | Foundgine.SupplyChain.PenTest.Tests.McpPenetrationTests.Customer_can_read_own_order | Passed | 1499.4 ms |
| GraphQL | Foundgine.SupplyChain.PenTest.Tests.GraphPenetrationTests.Customer_cannot_read_another_customers_order_scope_idor | Passed | 54.5 ms |
| GraphQL | Foundgine.SupplyChain.PenTest.Tests.GraphPenetrationTests.Wrong_token_for_a_real_actor_is_rejected | Passed | 13824.5 ms |
| GraphQL | Foundgine.SupplyChain.PenTest.Tests.GraphPenetrationTests.Unknown_actor_and_known_actor_with_wrong_token_give_identical_errors | Passed | 53 ms |
| GraphQL | Foundgine.SupplyChain.PenTest.Tests.GraphPenetrationTests.No_credentials_are_rejected | Passed | 39.6 ms |
| MCP | Foundgine.SupplyChain.PenTest.Tests.McpPenetrationTests.Customer_cannot_read_another_customers_order_scope_idor | Passed | 11551.1 ms |
| MCP | Foundgine.SupplyChain.PenTest.Tests.McpPenetrationTests.Unknown_actor_and_known_actor_with_wrong_token_give_identical_errors | Passed | 826.9 ms |
| MCP | Foundgine.SupplyChain.PenTest.Tests.McpPenetrationTests.Wrong_token_for_a_real_actor_is_rejected | Passed | 15 ms |
| GraphQL | Foundgine.SupplyChain.PenTest.Tests.GraphPenetrationTests.Authorization_failure_does_not_leak_internal_exception_details | Passed | 515.3 ms |
| GraphQL | Foundgine.SupplyChain.PenTest.Tests.GraphPenetrationTests.Injection_payload_in_tracking_number_is_stored_as_literal_text_not_executed | Passed | 135 ms |
| MCP | Foundgine.SupplyChain.PenTest.Tests.McpPenetrationTests.Customer_cannot_escalate_to_a_warehouse_only_capability | Passed | 15.5 ms |

> Security cases are the existing Foundgine.SupplyChain.PenTest xUnit tests executed against the PostgreSQL instance used by this E2E run. Individual durations come directly from the xUnit TRX result.

