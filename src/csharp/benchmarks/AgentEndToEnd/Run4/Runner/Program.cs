using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var runs = GetInt("RUN4_RUNS", 30);
var warmups = GetInt("RUN4_WARMUPS", 5);
var mode = Environment.GetEnvironmentVariable("RUN4_MODE") ?? (args.Length > 0 ? args[0] : "both");
var gql = Environment.GetEnvironmentVariable("RUN4_GRAPHQL_URL") ?? "http://localhost:4401/graphql";
var mcp = Environment.GetEnvironmentVariable("RUN4_MCP_URL") ?? "http://localhost:4402/mcp";
var customerCount = GetInt("RUN4_CUSTOMER_COUNT", 10);
var concurrency = GetInt("RUN4_CONCURRENCY", 8);

if (runs < 1 || warmups < 0 || customerCount < 1 || concurrency < 1)
    throw new ArgumentOutOfRangeException("Run 4 parameters must be positive (warmups may be zero).");

var handler = new SocketsHttpHandler
{
    MaxConnectionsPerServer = Math.Max(128, concurrency * 2),
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    ConnectTimeout = TimeSpan.FromSeconds(10)
};
using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };

Console.WriteLine("Foundgine Run 4 — MCP + Foundgine vs Hot Chocolate + EF Core");
Console.WriteLine($"Mode={mode} Warmups={warmups} Runs={runs} Customers={customerCount} Concurrency={concurrency}");
Console.WriteLine("Load profile: same as Run 2 (customers=10,100,1000,10000; concurrency=8,16,32,64)");

await WaitFor(http, gql.Replace("/graphql", "/health"));
await WaitFor(http, mcp.Replace("/mcp", "/health"));

var reportStats = new List<ConcurrentStats>();

if (mode is "agent" or "both")
{
    var gqlAgent = await MeasureConcurrent(
        "GraphQL+HotChocolate+EF agent",
        runs,
        warmups,
        concurrency,
        customerCount,
        async customerId =>
        {
            var calls = BuildGraphQlCalls(customerId);
            var input = 0;
            var output = 0;
            foreach (var q in calls)
            {
                input += q.Length;
                output += (await PostGraphql(http, gql, q)).Length;
            }

            return (input, output);
        });

    var mcpAgent = await MeasureConcurrent(
        "MCP+Foundgine agent",
        runs,
        warmups,
        concurrency,
        customerCount,
        async customerId =>
        {
            var intent = BuildIntent(customerId);
            var output = await PostMcpTool(http, mcp, "foundgine_query", new { intentJson = intent });
            return (intent.Length, output.Length);
        });

    reportStats.Add(gqlAgent);
    reportStats.Add(mcpAgent);
    PrintComparison(gqlAgent, mcpAgent, 6, 1);
}

if (mode is "protocol" or "both")
{
    var gqlOne = await MeasureConcurrent(
        "GraphQL+HotChocolate+EF single request",
        runs,
        warmups,
        concurrency,
        customerCount,
        async customerId =>
        {
            var query = BuildFullGraphQl(customerId);
            var output = await PostGraphql(http, gql, query);
            return (query.Length, output.Length);
        });

    var mcpOne = await MeasureConcurrent(
        "MCP+Foundgine single tool",
        runs,
        warmups,
        concurrency,
        customerCount,
        async customerId =>
        {
            var intent = BuildIntent(customerId);
            var output = await PostMcpTool(http, mcp, "foundgine_query", new { intentJson = intent });
            return (intent.Length, output.Length);
        });

    reportStats.Add(gqlOne);
    reportStats.Add(mcpOne);
    PrintComparison(gqlOne, mcpOne, 1, 1);
}

var reportDirectory = Environment.GetEnvironmentVariable("RUN4_REPORT_DIRECTORY");
if (!string.IsNullOrWhiteSpace(reportDirectory))
{
    Directory.CreateDirectory(reportDirectory);
    var report = new
    {
        schemaVersion = 2,
        utc = DateTimeOffset.UtcNow,
        mode,
        runs,
        warmups,
        customerCount,
        concurrency,
        customerFixture = "4 relationships / 12 contracts / 48 transactions per customer",
        conventional = "Hot Chocolate + EF Core GraphQL",
        foundgine = "MCP + Foundgine",
        tokenEstimate = "chars / 4 heuristic; replay mode is not provider billing data",
        samples = reportStats.SelectMany(x => x.RunSummaries.Select(r => new
        {
            implementation = x.Name.Contains("GraphQL", StringComparison.OrdinalIgnoreCase)
                ? "Conventional"
                : "Foundgine",
            option = x.Name,
            run = r.Run,
            concurrency,
            customerCount,
            rps = r.Rps,
            avgWallMs = r.AvgWallMs,
            p50Ms = r.P50Ms,
            p95Ms = r.P95Ms,
            p99Ms = r.P99Ms,
            maxWallMs = r.MaxWallMs,
            success = r.Success,
            failed = r.Failed,
            toolCalls = r.ToolCalls,
            estimatedInputTokens = r.EstimatedInputTokens,
            estimatedOutputTokens = r.EstimatedOutputTokens,
            estimatedContextTokens = r.EstimatedContextTokens
        })).ToArray()
    };
    await File.WriteAllTextAsync(Path.Combine(reportDirectory, "run4-metadata.json"),
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task<ConcurrentStats> MeasureConcurrent(
    string name,
    int runs,
    int warmups,
    int concurrency,
    int customerCount,
    Func<int, Task<(int inputChars, int outputChars)>> action)
{
    for (var i = 0; i < warmups; i++)
    {
        var warmup = await RunBatch(concurrency, customerCount, action);
        if (warmup.Failed > 0)
        {
            foreach (var error in warmup.Samples.Where(x => !x.Success).GroupBy(x => x.ErrorType ?? "Unknown")
                         .OrderByDescending(x => x.Count()))
                Console.WriteLine(
                    $"  warmup-error={error.Key} count={error.Count()} sample=\"{error.First().ErrorMessage}\"");
            throw new InvalidOperationException(
                $"Warmup failed for {name}: {warmup.Failed}/{warmup.Count} workers failed.");
        }
    }

    var all = new List<Sample>(runs * concurrency);
    var runSummaries = new List<RunSummary>(runs);
    for (var run = 1; run <= runs; run++)
    {
        var batch = await RunBatch(concurrency, customerCount, action);
        all.AddRange(batch.Samples);

        var successful = batch.Samples.Where(x => x.Success).ToArray();
        var latencies = successful.Select(x => x.WallMs).ToArray();
        var maxWall = latencies.Length == 0 ? 0 : latencies.Max();
        var avgWall = latencies.Length == 0 ? 0 : latencies.Average();
        var p50 = Percentile(latencies, .50);
        var p95 = Percentile(latencies, .95);
        var p99 = Percentile(latencies, .99);
        var rps = maxWall > 0 ? successful.Length / (maxWall / 1000.0) : 0;
        var avgContext = successful.Length == 0
            ? 0
            : successful.Average(x => EstimateTokens(x.InputChars + x.OutputChars));
        var avgInput = successful.Length == 0 ? 0 : successful.Average(x => x.InputChars / 4.0);
        var avgOutput = successful.Length == 0 ? 0 : successful.Average(x => x.OutputChars / 4.0);

        runSummaries.Add(new RunSummary(
            run, rps, avgWall, p50, p95, p99, maxWall, successful.Length, batch.Failed,
            name.Contains("agent", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("GraphQL", StringComparison.OrdinalIgnoreCase)
                ? 6
                : 1,
            avgInput, avgOutput, avgContext));

        Console.WriteLine(
            $"{name} run={run}: concurrency={concurrency} workers={batch.Count} success={successful.Length} failed={batch.Failed} " +
            $"rps={rps:F1} avgWall={avgWall:F1}ms p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1}ms maxWall={maxWall:F1}ms " +
            $"toolCalls/flow={(name.Contains("agent", StringComparison.OrdinalIgnoreCase) && name.Contains("GraphQL", StringComparison.OrdinalIgnoreCase) ? 6 : 1)} " +
            $"estInputTokens={avgInput:F0} estOutputTokens={avgOutput:F0} estContextLoadTokens={avgContext:F0}");

        foreach (var error in batch.Samples.Where(x => !x.Success).GroupBy(x => x.ErrorType ?? "Unknown")
                     .OrderByDescending(x => x.Count()))
            Console.WriteLine($"  error={error.Key} count={error.Count()} sample=\"{error.First().ErrorMessage}\"");
    }

    return new ConcurrentStats(name, all, runSummaries);
}

static async Task<BatchResult> RunBatch(
    int concurrency,
    int customerCount,
    Func<int, Task<(int inputChars, int outputChars)>> action)
{
    var tasks = Enumerable.Range(0, concurrency)
        .Select(worker => RunOne(customerCount == 0 ? 1 : (worker % customerCount) + 1, action))
        .ToArray();
    return new BatchResult(await Task.WhenAll(tasks));
}

static async Task<Sample> RunOne(int customerId, Func<int, Task<(int inputChars, int outputChars)>> action)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var result = await action(customerId);
        sw.Stop();
        return new Sample(true, sw.Elapsed.TotalMilliseconds, result.inputChars, result.outputChars, null, null);
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new Sample(false, sw.Elapsed.TotalMilliseconds, 0, 0, Classify(ex), ex.Message);
    }
}

static void PrintComparison(ConcurrentStats a, ConcurrentStats b, int aCalls, int bCalls)
{
    Console.WriteLine();
    Print(a);
    Print(b);
    Console.WriteLine($"Latency delta ({b.Name} vs {a.Name}): {(b.Average / a.Average - 1) * 100:F1}%");
    Console.WriteLine($"Tool calls/flow: {a.Name}={aCalls}, {b.Name}={bCalls}");
    var aContext = EstimateTokens(a.AvgInputChars + a.AvgOutputChars);
    var bContext = EstimateTokens(b.AvgInputChars + b.AvgOutputChars);
    Console.WriteLine($"Estimated context-load reduction: {(1.0 - bContext / aContext) * 100:F1}%");
    Console.WriteLine();
}

static void Print(ConcurrentStats x) => Console.WriteLine(
    $"{x.Name}: avg={x.Average:F1}ms p50={Percentile(x.SuccessfulLatencies, .50):F1}ms " +
    $"p95={Percentile(x.SuccessfulLatencies, .95):F1}ms p99={Percentile(x.SuccessfulLatencies, .99):F1}ms " +
    $"success={x.SuccessCount}/{x.Samples.Count} estInput={x.AvgInputChars / 4.0:F0} " +
    $"estOutput={x.AvgOutputChars / 4.0:F0} estContext={EstimateTokens(x.AvgInputChars + x.AvgOutputChars):F0}");

static string[] BuildGraphQlCalls(int customerId) => new[]
{
    $"query {{ customer(id: {customerId}) {{ id customerKey fullName }} }}",
    $"query {{ relationships(customerId: {customerId}) {{ id relationshipKey }} }}",
    $"query {{ contracts(customerId: {customerId}) {{ id contractKey amount }} }}",
    $"query {{ transactions(customerId: {customerId}) {{ id transactionKey amount balance }} }}",
    $"query {{ exposure(customerId: {customerId}) {{ contractCount contractAmount transactionBalance }} }}",
    $"query {{ customerVerify(id: {customerId}) {{ id customerKey fullName }} }}"
};

static string BuildFullGraphQl(int customerId) =>
    $"query {{ customer(id: {customerId}) {{ id customerKey fullName }} relationships(customerId: {customerId}) {{ id relationshipKey }} contracts(customerId: {customerId}) {{ id contractKey amount }} transactions(customerId: {customerId}) {{ id transactionKey amount balance }} exposure(customerId: {customerId}) {{ contractCount contractAmount transactionBalance }} customerVerify(id: {customerId}) {{ id customerKey fullName }} }}";

static string BuildIntent(int customerId) => """
                                             {"rootEntity":"Customer","selections":[{"field":"Id"},{"field":"CustomerKey"},{"field":"FullName"},{"relationship":"CustomerBankingRelationship","children":[{"field":"Id"},{"field":"CustomerBankingRelationshipKey"},{"relationship":"Contract","children":[{"field":"Id"},{"field":"ContractKey"},{"field":"Amount"},{"relationship":"Transaction","children":[{"field":"Id"},{"field":"TransactionKey"},{"field":"Amount"},{"field":"Balance"}]}]}]}],"filter":{"kind":"field","field":"Id","operator":"Eq","value":__CUSTOMER_ID__}}
                                             """.Replace("__CUSTOMER_ID__", customerId.ToString(),
    StringComparison.Ordinal);

static async Task<string> PostGraphql(HttpClient http, string url, string query)
{
    using var response = await SendWithRetry(http, () =>
    {
        var content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");
        return http.PostAsync(url, content);
    });
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new HttpRequestException($"GraphQL {(int)response.StatusCode}: {body}");
    if (body.Contains("\"errors\"", StringComparison.Ordinal))
        throw new InvalidOperationException($"GraphQL errors: {body}");
    return body;
}

static async Task<string> PostMcpTool(HttpClient http, string url, string tool, object arguments)
{
    using var response = await SendWithRetry(http, () =>
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("N"),
            method = "tools/call",
            @params = new
            {
                name = tool,
                arguments,
                _meta = new Dictionary<string, object?>
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientInfo"] = new { name = "Foundgine-Run4-Runner", version = "1.1.0" },
                    ["io.modelcontextprotocol/clientCapabilities"] = new Dictionary<string, object?>()
                }
            }
        };
        var json = JsonSerializer.Serialize(request);
        var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        message.Headers.Accept.ParseAdd("application/json, text/event-stream");
        message.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
        message.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
        message.Headers.TryAddWithoutValidation("Mcp-Name", tool);
        return http.SendAsync(message);
    });
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new HttpRequestException($"MCP {(int)response.StatusCode}: {body}");
    if (body.Contains("\"error\"", StringComparison.Ordinal)) throw new InvalidOperationException($"MCP error: {body}");
    return body;
}

static async Task<HttpResponseMessage> SendWithRetry(HttpClient http, Func<Task<HttpResponseMessage>> send)
{
    Exception? last = null;
    for (var attempt = 1; attempt <= 4; attempt++)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException ex) when (attempt < 4)
        {
            last = ex;
            await Task.Delay(100 * (int)Math.Pow(2, attempt - 1));
        }
        catch (TaskCanceledException ex) when (attempt < 4)
        {
            last = ex;
            await Task.Delay(100 * (int)Math.Pow(2, attempt - 1));
        }
    }

    throw last ?? new HttpRequestException("HTTP request failed after retries.");
}

static async Task WaitFor(HttpClient http, string url)
{
    for (var i = 0; i < 180; i++)
    {
        try
        {
            using var r = await http.GetAsync(url);
            if (r.IsSuccessStatusCode) return;
        }
        catch
        {
        }

        await Task.Delay(500);
    }

    throw new InvalidOperationException($"Endpoint did not become ready: {url}");
}

static string Classify(Exception ex) => ex switch
{
    HttpRequestException => "HTTP",
    TaskCanceledException => "Timeout",
    _ when ex.Message.Contains("MCP", StringComparison.OrdinalIgnoreCase) => "MCP",
    _ when ex.Message.Contains("GraphQL", StringComparison.OrdinalIgnoreCase) => "GraphQL",
    _ => "Application"
};

static double Percentile(IReadOnlyList<double> values, double p)
{
    var x = values.OrderBy(v => v).ToArray();
    if (x.Length == 0) return 0;
    var i = (x.Length - 1) * p;
    var lo = (int)Math.Floor(i);
    var hi = (int)Math.Ceiling(i);
    return lo == hi ? x[lo] : x[lo] + (x[hi] - x[lo]) * (i - lo);
}

static double EstimateTokens(double chars) => Math.Max(1, chars / 4.0);

static int GetInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;

record Sample(bool Success, double WallMs, int InputChars, int OutputChars, string? ErrorType, string? ErrorMessage);

record BatchResult(Sample[] Samples)
{
    public int Count => Samples.Length;
    public int Failed => Samples.Count(x => !x.Success);
}

record RunSummary(
    int Run,
    double Rps,
    double AvgWallMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxWallMs,
    int Success,
    int Failed,
    int ToolCalls,
    double EstimatedInputTokens,
    double EstimatedOutputTokens,
    double EstimatedContextTokens);

record ConcurrentStats(string Name, List<Sample> Samples, List<RunSummary> RunSummaries)
{
    public int SuccessCount => Samples.Count(x => x.Success);
    public double Average => SuccessCount == 0 ? 0 : Samples.Where(x => x.Success).Average(x => x.WallMs);
    public double[] SuccessfulLatencies => Samples.Where(x => x.Success).Select(x => x.WallMs).ToArray();
    public double AvgInputChars => SuccessCount == 0 ? 0 : Samples.Where(x => x.Success).Average(x => x.InputChars);
    public double AvgOutputChars => SuccessCount == 0 ? 0 : Samples.Where(x => x.Success).Average(x => x.OutputChars);
}