using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Npgsql;

internal static class Program
{
    public static async Task Main()
    {
        var steps = GetInt("SUPPLY_CHAIN_STEPS", 25);
        var customers = Math.Clamp(
            GetInt("SUPPLY_CHAIN_CUSTOMERS", 5),
            1,
            5);

        var seed = GetInt("SUPPLY_CHAIN_SEED", 20260823);

        var mcpUrl =
            Environment.GetEnvironmentVariable("SUPPLY_CHAIN_MCP_URL")
            ?? "http://localhost:4422/mcp";

        var connectionString =
            Environment.GetEnvironmentVariable("SupplyChainConnectionString")
            ?? "Host=localhost;Port=4429;Database=foundgine_supply_chain;Username=benchmark;Password=benchmark";

        var reportDirectory =
            Environment.GetEnvironmentVariable("SUPPLY_CHAIN_REPORT_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "reports");

        Directory.CreateDirectory(reportDirectory);

        Console.WriteLine("Foundgine Supply Chain E2E");
        Console.WriteLine($"Seed:      {seed}");
        Console.WriteLine($"Steps:     {steps}");
        Console.WriteLine($"Customers: {customers}");
        Console.WriteLine($"MCP:       {mcpUrl}");
        Console.WriteLine();

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        await WaitForServiceAsync(
            http,
            mcpUrl.Replace("/mcp", "/health/ready"));

        await using var db = NpgsqlDataSource.Create(connectionString);

        var rng = new Random(seed);

        var actors = Enumerable
            .Range(1, customers)
            .Select(i =>
                new Actor(
                    i == 1 ? "alice" : $"customer{i}",
                    i))
            .ToList();

        actors.Add(new Actor("bob", Math.Min(2, customers)));
        actors.Add(new Actor("carol", 1));
        actors.Add(new Actor("dave", 1));
        actors.Add(new Actor("admin", 1));

        var records = new List<Record>(steps);

        for (var i = 0; i < steps; i++)
        {
            var actor = actors[rng.Next(actors.Count)];
            var choice = rng.Next(100);

            var stopwatch = Stopwatch.StartNew();

            string capability;
            object toolArguments;
            bool expectedAllowed;

            if (choice < 12)
            {
                capability = "get_my_orders";

                toolArguments = new
                {
                    actor = actor.Name,
                    customerId = actor.CustomerId
                };

                expectedAllowed =
                    actor.Name is "alice"
                        or "bob"
                        or "admin"
                    || actor.Name.StartsWith("customer");
            }
            else if (choice < 22)
            {
                capability = "get_order";

                var targetCustomer =
                    actor.Name is "alice" or "bob" or "admin"
                        ? rng.Next(1, Math.Max(2, i + 1))
                        : actor.CustomerId;

                toolArguments = new
                {
                    actor = actor.Name,
                    customerId = targetCustomer,
                    orderId = rng.Next(1, Math.Max(2, i + 1))
                };

                expectedAllowed =
                    actor.Name is "alice"
                        or "bob"
                        or "admin"
                    || actor.Name.StartsWith("customer");
            }
            else if (choice < 32)
            {
                capability = "get_product";

                toolArguments = new
                {
                    actor = actor.Name,
                    productId = rng.Next(1, 13)
                };

                expectedAllowed =
                    actor.Name is "alice"
                        or "bob"
                        or "carol"
                        or "dave"
                        or "admin"
                    || actor.Name.StartsWith("customer");
            }
            else if (choice < 42)
            {
                capability = "get_inventory";

                toolArguments = new
                {
                    actor = actor.Name,
                    productId = rng.Next(1, 13)
                };

                expectedAllowed =
                    actor.Name is "carol"
                        or "dave"
                        or "admin";
            }
            else if (choice < 50)
            {
                capability = "list_suppliers";

                toolArguments = new
                {
                    actor = actor.Name
                };

                expectedAllowed =
                    actor.Name is "dave" or "admin";
            }
            else if (choice < 58)
            {
                capability = "list_products";

                toolArguments = new
                {
                    actor = actor.Name
                };

                expectedAllowed =
                    actor.Name is "dave" or "admin";
            }
            else if (choice < 64)
            {
                capability = "list_customers";

                toolArguments = new
                {
                    actor = actor.Name
                };

                expectedAllowed =
                    actor.Name is "bob" or "admin";
            }
            else if (choice < 70)
            {
                // Ambiguity-resolution cases from the Foundgine walkthrough
                // (docs-site/walkthrough/index.html): "top supplier in
                // <state>" is not a database key. In the seeded fixture:
                //   TX -> unambiguous (Acme > Globex): calculated evidence,
                //         goes all the way to execution.
                //   CA -> tie (Northstar == Southline): stops before
                //         authorization/execution and asks the agent to
                //         choose or give a more specific intent.
                //   NY -> no suppliers at all: not_found, nothing to
                //         resolve or execute.
                //   CA + an explicit supplierName -> closes the loop: the
                //         agent already knows which of the tied candidates
                //         it wants, so this resolves and executes instead
                //         of asking again.
                capability = "find_top_supplier_overdue_orders";

                var pick = rng.Next(4);
                var state = pick switch
                {
                    0 => "TX",
                    1 => "CA",
                    2 => "NY",
                    _ => "CA"
                };
                var supplierName = pick == 3
                    ? (rng.Next(2) == 0 ? "Northstar Supply" : "Southline Parts")
                    : null;

                toolArguments = supplierName is null
                    ? new { actor = actor.Name, state }
                    : (object)new { actor = actor.Name, state, supplierName };

                expectedAllowed =
                    actor.Name is "bob" or "admin";
            }
            else if (choice < 80)
            {
                capability = "place_order";

                var customer =
                    actor.Name is "alice"
                    || actor.Name.StartsWith("customer")
                        ? rng.Next(100) < 75
                            ? actor.CustomerId
                            : rng.Next(1, customers + 1)
                        : rng.Next(1, customers + 1);

                toolArguments = new
                {
                    actor = actor.Name,
                    customerId = customer,
                    lines = new[]
                    {
                        new
                        {
                            productId = rng.Next(1, 13),
                            quantity = rng.Next(1, 4)
                        }
                    },
                    idempotencyKey =
                        $"agent-{seed}-{i}-{Guid.NewGuid():N}"
                };

                expectedAllowed =
                    actor.Name is "bob" or "admin"
                    || (
                        actor.Name is "alice"
                        || actor.Name.StartsWith("customer")
                    )
                    && customer == actor.CustomerId;
            }
            else if (choice < 88)
            {
                capability = "cancel_order";

                var customer =
                    actor.Name is "alice"
                    || actor.Name.StartsWith("customer")
                        ? actor.CustomerId
                        : Math.Min(2, customers);

                toolArguments = new
                {
                    actor = actor.Name,
                    customerId = customer,
                    orderId = rng.Next(1, Math.Max(2, i + 1))
                };

                expectedAllowed =
                    actor.Name is "bob"
                        or "admin"
                        or "alice"
                    || actor.Name.StartsWith("customer");
            }
            else if (choice < 93)
            {
                capability = "update_inventory";

                toolArguments = new
                {
                    actor = actor.Name,
                    warehouseId = rng.Next(1, 4),
                    productId = rng.Next(1, 13),
                    quantity = rng.Next(0, 100)
                };

                expectedAllowed =
                    actor.Name is "carol"
                        or "dave"
                        or "admin";
            }
            else if (choice < 97)
            {
                capability = "create_shipment";

                toolArguments = new
                {
                    actor = actor.Name,
                    orderId = rng.Next(1, Math.Max(2, i + 1)),
                    carrierId = rng.Next(1, 3),
                    warehouseId = rng.Next(1, 4),
                    trackingNumber = $"TRK-{seed}-{i}"
                };

                expectedAllowed =
                    actor.Name is "carol" or "admin";
            }
            else
            {
                capability = "update_shipment";

                toolArguments = new
                {
                    actor = actor.Name,
                    shipmentId = rng.Next(1, Math.Max(2, i + 1)),
                    status = new[]
                    {
                        "In Transit",
                        "Out for Delivery",
                        "Delivered",
                        "Delayed"
                    }[rng.Next(4)]
                };

                expectedAllowed =
                    actor.Name is "carol" or "admin";
            }

            var requestText = JsonSerializer.Serialize(toolArguments);

            try
            {
                var ordersBefore =
                    await CountOrdersAsync(db, actor.CustomerId);

                var responseBody =
                    await CallMcpAsync(
                        http,
                        mcpUrl,
                        capability,
                        toolArguments);

                stopwatch.Stop();

                var toolError = IsToolError(responseBody);

                var ordersAfter =
                    await CountOrdersAsync(db, actor.CustomerId);

                records.Add(
                    new Record(
                        Step: i + 1,
                        Actor: actor.Name,
                        Capability: capability,
                        ExpectedAllowed: expectedAllowed,
                        Success: !toolError,
                        LatencyMs: stopwatch.Elapsed.TotalMilliseconds,
                        OrdersBefore: ordersBefore,
                        OrdersAfter: ordersAfter,
                        ResponseBytes: Encoding.UTF8.GetByteCount(responseBody),
                        Error: toolError ? responseBody : null,
                        RequestText: requestText,
                        ResponseText: responseBody));
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                records.Add(
                    new Record(
                        Step: i + 1,
                        Actor: actor.Name,
                        Capability: capability,
                        ExpectedAllowed: expectedAllowed,
                        Success: false,
                        LatencyMs: stopwatch.Elapsed.TotalMilliseconds,
                        OrdersBefore: -1,
                        OrdersAfter: -1,
                        ResponseBytes: 0,
                        Error: ex.Message,
                        RequestText: requestText,
                        ResponseText: null));
            }
        }

        //
        // IMPORTANT:
        //
        // This workload executes only the Foundgine flow.
        // There is NO live conventional baseline here.
        //
        // Therefore the conventional numbers below are explicitly MODELED,
        // not measured.
        //

        const int ModeledConventionalStepsPerCapability = 4;

        var succeeded =
            records
                .Where(x => x.Success)
                .ToList();

        var perCallContextTokens =
            succeeded
                .Select(x =>
                    TokenEstimator.Estimate(x.RequestText)
                    + TokenEstimator.Estimate(x.ResponseText))
                .ToList();

        var totalFoundgineContextTokens =
            perCallContextTokens.Sum();

        var averageFoundgineContextTokensPerCall =
            perCallContextTokens.Count > 0
                ? perCallContextTokens.Average()
                : 0;

        var modeledConventionalContextTokens =
            (long)Math.Round(
                (double)(totalFoundgineContextTokens
                         * ModeledConventionalStepsPerCapability));

        var modeledConventionalToolCalls =
            succeeded.Count
            * ModeledConventionalStepsPerCapability;

        var efficiencyEstimate = new
        {
            method =
                "Standalone heuristic (chars/4 vs words*1.3, same as Run1/Program.cs TokenEstimator) applied to this run's actual MCP request/response payloads. No conventional flow was executed for comparison, so the conventional side is MODELED, not measured.",

            measuredFoundgine = new
            {
                toolCalls = succeeded.Count,
                // One MCP round trip per successful capability call: the
                // agent sends one request and gets back a completed result.
                roundTrips = succeeded.Count,
                totalEstimatedContextLoadTokens =
                    totalFoundgineContextTokens,
                avgEstimatedContextLoadTokensPerCall =
                    Math.Round(
                        averageFoundgineContextTokensPerCall,
                        1)
            },

            modeledConventional = new
            {
                stepsPerCapabilityMultiplier =
                    ModeledConventionalStepsPerCapability,

                estimatedToolCalls =
                    modeledConventionalToolCalls,

                // Same figure as estimatedToolCalls, named for the
                // "how many round trips would this take without Foundgine"
                // question specifically: discover -> authorize -> execute
                // -> verify, one round trip each, per successful capability.
                estimatedRoundTrips =
                    modeledConventionalToolCalls,

                roundTripsPerCapability =
                    ModeledConventionalStepsPerCapability,

                estimatedContextLoadTokens =
                    modeledConventionalContextTokens
            },

            modeledToolCallReductionPercent =
                modeledConventionalToolCalls > 0
                    ? Math.Round(
                        (1 -
                         (double)succeeded.Count
                         / modeledConventionalToolCalls)
                        * 100,
                        1)
                    : 0,

            modeledContextLoadReductionPercent =
                modeledConventionalContextTokens > 0
                    ? Math.Round(
                        (1 -
                         (double)totalFoundgineContextTokens
                         / modeledConventionalContextTokens)
                        * 100,
                        1)
                    : 0,

            caveats = new[]
            {
                "MODELED, not measured: no conventional REST/GraphQL flow was actually executed for this workload.",

                "The 4x step multiplier (discover/authorize/execute/verify) mirrors the choreography measured directly in Run1; it is not re-derived from this workload.",

                "Token counts are a payload-size heuristic, not a real tokenizer or provider-reported count.",

                "For a measured comparison, see the AgentEndToEnd Run1-5 reports, which execute both flows against the same fixture."
            }
        };

        var summary = new
        {
            success = records.Count(x => x.Success),
            failures = records.Count(x => !x.Success),

            expectedAllowed =
                records.Count(x => x.ExpectedAllowed),

            unexpectedUnauthorizedSuccesses =
                records.Count(x => !x.ExpectedAllowed && x.Success),

            avgLatencyMs =
                records
                    .Where(x => x.Success)
                    .Select(x => x.LatencyMs)
                    .DefaultIfEmpty()
                    .Average()
        };

        var report = new
        {
            schemaVersion = 2,
            utc = DateTimeOffset.UtcNow,
            seed,
            steps,
            customers,
            mcp = mcpUrl,
            summary,
            efficiencyEstimate,
            records
        };

        var jsonPath =
            Path.Combine(
                reportDirectory,
                "supply-chain-report.json");

        var markdownPath =
            Path.Combine(
                reportDirectory,
                "supply-chain-report.md");

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        await File.WriteAllTextAsync(
            markdownPath,
            Markdown(report));

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("Supply Chain E2E");
        Console.WriteLine("========================================");
        Console.WriteLine(
            $"Successful operations: {summary.success}/{steps}");
        Console.WriteLine(
            $"Failures:               {summary.failures}");
        Console.WriteLine(
            $"Expected allowed:       {summary.expectedAllowed}");
        Console.WriteLine(
            $"Unexpected unauthorized successes: " +
            $"{summary.unexpectedUnauthorizedSuccesses}");
        Console.WriteLine(
            $"Average latency:        {summary.avgLatencyMs:F1} ms");

        Console.WriteLine();
        Console.WriteLine(
            "Efficiency estimate (MODELED, not measured):");

        Console.WriteLine(
            $"  Tool-call reduction:   " +
            $"{efficiencyEstimate.modeledToolCallReductionPercent:F1}%");

        Console.WriteLine(
            $"  Context-load reduction: " +
            $"{efficiencyEstimate.modeledContextLoadReductionPercent:F1}%");

        Console.WriteLine();
        Console.WriteLine($"JSON report: {jsonPath}");
        Console.WriteLine($"Markdown report: {markdownPath}");
    }

    private static async Task<string> CallMcpAsync(
        HttpClient http,
        string url,
        string name,
        object arguments)
    {
        var requestJson = JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id = Guid.NewGuid().ToString("N"),
                method = "tools/call",
                @params = new
                {
                    name,
                    arguments
                }
            });

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                url)
            {
                Content =
                    new StringContent(
                        requestJson,
                        Encoding.UTF8,
                        "application/json")
            };

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "text/event-stream"));

        request.Headers.TryAddWithoutValidation(
            "MCP-Protocol-Version",
            "2025-06-18");

        using var response =
            await http.SendAsync(request);

        var body =
            await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return
                $"HTTP {(int)response.StatusCode}: {body}";
        }

        return body;
    }

    private static bool IsToolError(string body)
    {
        try
        {
            using var document =
                JsonDocument.Parse(body);

            var root = document.RootElement;

            if (root.TryGetProperty(
                    "error",
                    out _))
            {
                return true;
            }

            if (root.TryGetProperty(
                    "result",
                    out var result)
                && result.TryGetProperty(
                    "isError",
                    out var isError)
                && isError.ValueKind ==
                JsonValueKind.True)
            {
                return true;
            }
        }
        catch
        {
            // Invalid JSON is treated as a non-tool-error here.
            // Protocol validation can be made stricter if desired.
        }

        return false;
    }

    private static async Task<int> CountOrdersAsync(
        NpgsqlDataSource dataSource,
        int customerId)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync();

        await using var command =
            new NpgsqlCommand(
                """
                SELECT count(*)
                FROM orders
                WHERE customer_id = @customerId
                """,
                connection);

        command.Parameters.AddWithValue(
            "customerId",
            customerId);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync());
    }

    private static async Task WaitForServiceAsync(
        HttpClient http,
        string url)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                var response =
                    await http.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Service may not be ready yet.
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException(
            $"MCP service not ready: {url}");
    }

    private static int GetInt(
        string name,
        int defaultValue)
    {
        return int.TryParse(
            Environment.GetEnvironmentVariable(name),
            out var value)
            ? value
            : defaultValue;
    }

    private static string Markdown(dynamic report)
    {
        return $"""
                # Supply Chain E2E

                - Seed: {report.seed}
                - Steps: {report.steps}
                - Customers: {report.customers}
                - Success: {report.summary.success}
                - Failures: {report.summary.failures}
                - Unexpected unauthorized successes: {report.summary.unexpectedUnauthorizedSuccesses}
                - Average latency: {report.summary.avgLatencyMs:F1} ms

                ## Efficiency estimate (MODELED, not measured)

                - Measured Foundgine tool calls: {report.efficiencyEstimate.measuredFoundgine.toolCalls}
                - Measured Foundgine estimated context load: {report.efficiencyEstimate.measuredFoundgine.totalEstimatedContextLoadTokens} tokens ({report.efficiencyEstimate.measuredFoundgine.avgEstimatedContextLoadTokensPerCall} avg/call)
                - Modeled conventional tool calls ({report.efficiencyEstimate.modeledConventional.stepsPerCapabilityMultiplier}x/capability): {report.efficiencyEstimate.modeledConventional.estimatedToolCalls}
                - Modeled conventional estimated context load: {report.efficiencyEstimate.modeledConventional.estimatedContextLoadTokens} tokens
                - **Modeled tool-call reduction: {report.efficiencyEstimate.modeledToolCallReductionPercent}%**
                - **Modeled context-load reduction: {report.efficiencyEstimate.modeledContextLoadReductionPercent}%**

                > This run has no live conventional flow to compare against, so the conventional side above is modeled from the discover/authorize/execute/verify choreography used by Run1 — not re-executed here.

                > For a measured comparison, see the AgentEndToEnd Run1-5 reports.
                """;
    }

    private static class TokenEstimator
    {
        // Kept identical to Run1/Program.cs's TokenEstimator.Estimate
        // so benchmark reports use the same estimation scale.
        public static int Estimate(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var characters = text.Length;

            var words =
                text.Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries)
                    .Length;

            return (int)Math.Round(
                Math.Max(
                    characters / 4.0,
                    words * 1.3));
        }
    }

    private sealed record Actor(
        string Name,
        int CustomerId);

    private sealed record Record(
        int Step,
        string Actor,
        string Capability,
        bool ExpectedAllowed,
        bool Success,
        double LatencyMs,
        int OrdersBefore,
        int OrdersAfter,
        int ResponseBytes,
        string? Error,
        string RequestText,
        string? ResponseText);
}