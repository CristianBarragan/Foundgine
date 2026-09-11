using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

const string ScenarioName = "customer-exposure-review-query-mutation-query-mutation";

var mode = (Environment.GetEnvironmentVariable("AGENT_BENCHMARK_MODE") ?? "replay").Trim().ToLowerInvariant();
var runs = GetInt("AGENT_BENCHMARK_RUNS", 5);
var warmups = GetInt("AGENT_BENCHMARK_WARMUPS", 1);
var concurrency = GetInt("AGENT_BENCHMARK_CONCURRENCY", 1);
var customerCount = GetInt("AGENT_BENCHMARK_CUSTOMER_COUNT", 1);
var customerId = GetInt("AGENT_BENCHMARK_CUSTOMER_ID", 1);
var reportDirectory = Environment.GetEnvironmentVariable("AGENT_BENCHMARK_REPORT_DIRECTORY") ??
                      "./artifacts/agent-benchmark";
var connectionString = Environment.GetEnvironmentVariable("BankingConnectionString")
                       ?? Environment.GetEnvironmentVariable("COFFEEBEANERY_CONNECTION")
                       ?? throw new InvalidOperationException(
                           "Set BankingConnectionString or COFFEEBEANERY_CONNECTION.");
var foundgineUrl = Environment.GetEnvironmentVariable("FOUNDGINE_GRAPHQL_URL") ?? "http://localhost:8080/graphql/warm";

if (concurrency < 1) throw new ArgumentOutOfRangeException(nameof(concurrency));
if (customerCount < 1) throw new ArgumentOutOfRangeException(nameof(customerCount));

Directory.CreateDirectory(reportDirectory);

await using var db = new NpgsqlConnection(connectionString);
await db.OpenAsync();
await DatabaseHelpers.SetSearchPathAsync(db);

var customerIds = await DatabaseHelpers.ReadCustomerIdsAsync(db, Math.Max(customerCount, concurrency));
if (customerIds.Count == 0) throw new InvalidOperationException("No benchmark customers were found.");
if (concurrency > customerIds.Count)
    Console.WriteLine(
        $"WARNING: concurrency={concurrency} exceeds available customers={customerIds.Count}; customer IDs will be reused across concurrent flows.");

await ResetCustomersAsync(connectionString, customerIds);
var expectedStates = await ExpectedStateBuilder.BuildAsync(connectionString, customerIds);
var expectedStatePath = Path.Combine(reportDirectory, "expected-state.json");
await File.WriteAllTextAsync(expectedStatePath,
    JsonSerializer.Serialize(expectedStates, new JsonSerializerOptions { WriteIndented = true }));

var fixture = expectedStates.First(x => x.CustomerId == customerId).Baseline;
Console.WriteLine($"Scenario: {ScenarioName}");
Console.WriteLine(
    $"Mode: {mode}; concurrency={concurrency}; warmups={warmups}; runs={runs}; fixtureCustomer={customerId}");
Console.WriteLine(
    $"Fixture: relationships={fixture.RelationshipCount}, contracts={fixture.ContractCount}, transactions={fixture.TransactionCount}, exposure={fixture.Exposure:N2}");

var results = new List<RunResult>();

foreach (var flow in Enum.GetValues<FlowKind>())
{
    for (var i = 0; i < warmups; i++)
    {
        await ResetCustomersAsync(connectionString, customerIds);
        await RunConcurrentBatchAsync(flow, mode, connectionString, foundgineUrl, customerIds, concurrency,
            recordTrace: false);
    }

    for (var i = 1; i <= runs; i++)
    {
        await ResetCustomersAsync(connectionString, customerIds);
        var batch = await RunConcurrentBatchAsync(flow, mode, connectionString, foundgineUrl, customerIds, concurrency,
            recordTrace: true);
        foreach (var result in batch)
        {
            results.Add(result with { Run = i });
        }

        var avgWall = batch.Average(x => x.WallClockMs);
        var maxWall = batch.Max(x => x.WallClockMs);
        var avgToolCalls = batch.Average(x => x.ToolCalls);
        var avgPayloadBytes = batch.Average(x => x.AgentToolPayloadBytes);
        var avgRoundTrips = batch.Average(x => x.AgentToolRoundTrips);
        var avgEstimatedInput = batch.Average(x => x.EstimatedToolInputTokens);
        var avgEstimatedOutput = batch.Average(x => x.EstimatedToolOutputTokens);
        var avgEstimatedContext = batch.Average(x => x.EstimatedContextLoadTokens);
        var avgProviderInput = batch.Average(x => x.ProviderInputTokens);
        var avgProviderOutput = batch.Average(x => x.ProviderOutputTokens);
        var avgProviderTotal = batch.Average(x => x.ProviderTotalTokens);
        var successful = batch.Count(x => x.Success);
        var failed = batch.Count - successful;
        var p50 = BenchmarkMath.Percentile(batch.Where(x => x.Success).Select(x => x.WallClockMs), 0.50);
        var p95 = BenchmarkMath.Percentile(batch.Where(x => x.Success).Select(x => x.WallClockMs), 0.95);
        var p99 = BenchmarkMath.Percentile(batch.Where(x => x.Success).Select(x => x.WallClockMs), 0.99);
        var peakHttp = batch.Count == 0 ? 0 : batch.Max(x => x.PeakActiveHttpRequests);
        var retries = batch.Sum(x => x.HttpRetries);
        var rps = maxWall > 0 ? successful / (maxWall / 1000.0) : 0;
        Console.WriteLine(
            $"{flow.DisplayName()} run={i}: concurrency={concurrency} workers={batch.Count} success={successful} failed={failed} rps={rps:F1} avgWall={avgWall:F1}ms p50={p50:F1}ms p95={p95:F1}ms p99={p99:F1} maxWall={maxWall:F1}ms toolCalls/flow={avgToolCalls:F1} estToolInputTokens={avgEstimatedInput:F0} estToolOutputTokens={avgEstimatedOutput:F0} estContextLoadTokens={avgEstimatedContext:F0} providerTokens={(avgProviderTotal > 0 ? $"{avgProviderInput:F0}/{avgProviderOutput:F0}/{avgProviderTotal:F0}" : "N/A")} activeHttpPeak={peakHttp} httpRetries={retries}");
        foreach (var error in batch.Where(x => !x.Success).GroupBy(x => x.ErrorType ?? "Unknown")
                     .OrderByDescending(x => x.Count()))
            Console.WriteLine($"  error={error.Key} count={error.Count()} sample=\"{error.First().ErrorMessage}\"");
    }
}

var comparison = Comparison.Create(results, expectedStates);
var report = new BenchmarkReport(
    DateTimeOffset.UtcNow,
    ScenarioName,
    mode,
    runs,
    warmups,
    concurrency,
    customerId,
    fixture,
    results,
    comparison);

var jsonPath = Path.Combine(reportDirectory, "agent-benchmark.json");
var markdownPath = Path.Combine(reportDirectory, "agent-benchmark.md");
await File.WriteAllTextAsync(jsonPath,
    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllTextAsync(markdownPath, report.ToMarkdown());

Console.WriteLine();
Console.WriteLine($"Concurrency: {concurrency}");
Console.WriteLine($"Estimated context-load saving: {comparison.EstimatedContextLoadSavingPercent:F1}%");
Console.WriteLine("Estimated model usage / run");
Console.WriteLine(
    $"  Conventional: input={comparison.Conventional.EstimatedToolInputTokens:F0}, output={comparison.Conventional.EstimatedToolOutputTokens:F0}, context-load={comparison.Conventional.EstimatedContextLoadTokens:F0}");
Console.WriteLine(
    $"  Foundgine:    input={comparison.Foundgine.EstimatedToolInputTokens:F0}, output={comparison.Foundgine.EstimatedToolOutputTokens:F0}, context-load={comparison.Foundgine.EstimatedContextLoadTokens:F0}");
Console.WriteLine($"Context-load saving: {comparison.EstimatedContextLoadSavingPercent:F1}%");
Console.WriteLine(
    $"Provider input-token saving: {(comparison.HasProviderTokenData ? $"{comparison.ProviderInputTokenSavingPercent:F1}%" : "Not measured (replay mode)")}");
Console.WriteLine(
    $"Provider total-token saving: {(comparison.HasProviderTokenData ? $"{comparison.ProviderTotalTokenSavingPercent:F1}%" : "Not measured (replay mode)")}");
Console.WriteLine($"Agent/tool round-trip saving: {comparison.AgentToolRoundTripSavingPercent:F1}%");
Console.WriteLine($"Agent/tool payload saving: {comparison.AgentToolPayloadSavingPercent:F1}%");
Console.WriteLine($"Tool-call saving: {comparison.ToolCallSavingPercent:F1}%");
Console.WriteLine($"Expected final state verified: {comparison.ExpectedFinalStateVerified}");
Console.WriteLine($"Verification failures: {comparison.Verification.Count(x => !x.IsMatch)}");
Console.WriteLine($"Expected state: {expectedStatePath}");
Console.WriteLine($"Reports: {jsonPath}; {markdownPath}");

static async Task<IReadOnlyList<RunResult>> RunConcurrentBatchAsync(
    FlowKind flow, string mode, string connectionString, string foundgineUrl,
    IReadOnlyList<int> customerIds, int concurrency, bool recordTrace)
{
    BenchmarkHttp.ResetCounters();
    var tasks = Enumerable.Range(0, concurrency)
        .Select(worker => RunAsync(
            flow, mode, connectionString, foundgineUrl,
            customerIds[worker % customerIds.Count], recordTrace))
        .ToArray();

    return await Task.WhenAll(tasks);
}


static async Task<RunResult> RunAsync(FlowKind flow, string mode, string connectionString, string foundgineUrl,
    int customerId, bool recordTrace)
{
    var trace = new TraceCollector(flow, customerId);
    var wall = Stopwatch.StartNew();

    try
    {
        if (mode == "replay")
        {
            if (flow == FlowKind.Conventional)
                await ConventionalReplay.RunAsync(connectionString, trace, customerId);
            else
                await FoundgineReplay.RunAsync(foundgineUrl, trace, customerId);
        }
        else if (mode == "live")
        {
            var endpoint = Environment.GetEnvironmentVariable("AGENT_MODEL_ENDPOINT")
                           ?? throw new InvalidOperationException("Live mode requires AGENT_MODEL_ENDPOINT.");
            var key = Environment.GetEnvironmentVariable("AGENT_MODEL_API_KEY") ?? string.Empty;
            var model = Environment.GetEnvironmentVariable("AGENT_MODEL")
                        ?? throw new InvalidOperationException("Live mode requires AGENT_MODEL.");
            var client = new OpenAiCompatibleAgentClient(endpoint, key, model);
            var tools = flow == FlowKind.Conventional
                ? ConventionalTools.Create(connectionString)
                : FoundgineTools.Create(foundgineUrl, customerId);
            await client.RunAsync(flow.SystemPrompt(), BenchmarkScenario.Request(customerId), tools, trace);
        }
        else
        {
            throw new InvalidOperationException("AGENT_BENCHMARK_MODE must be 'replay' or 'live'.");
        }

        await using var verification = new NpgsqlConnection(connectionString);
        await verification.OpenAsync();
        await DatabaseHelpers.SetSearchPathAsync(verification);
        trace.FinalState(JsonSerializer.Serialize(await ScenarioSnapshot.ReadAsync(verification, customerId)));
        wall.Stop();
        trace.WallClockMs = wall.Elapsed.TotalMilliseconds;
        trace.Success = true;
        return trace.ToResult(recordTrace, BenchmarkHttp.Peak, BenchmarkHttp.Requests, BenchmarkHttp.Retries);
    }
    catch (Exception ex)
    {
        wall.Stop();
        trace.WallClockMs = wall.Elapsed.TotalMilliseconds;
        trace.Success = false;
        trace.ErrorType = ClassifyError(ex);
        trace.ErrorMessage = ex.Message;
        return trace.ToResult(recordTrace, BenchmarkHttp.Peak, BenchmarkHttp.Requests, BenchmarkHttp.Retries);
    }
}

static string ClassifyError(Exception ex)
{
    var socket = ex as SocketException ?? ex.InnerException as SocketException;
    if (socket is not null) return $"Socket:{socket.SocketErrorCode}";
    if (ex is TaskCanceledException or OperationCanceledException) return "Timeout";
    if (ex is HttpRequestException) return "HttpRequest";
    if (ex is InvalidOperationException) return "InvalidOperation";
    return ex.GetType().Name;
}

static async Task ResetCustomersAsync(string connectionString, IReadOnlyList<int> customerIds)
{
    await using var db = new NpgsqlConnection(connectionString);
    await db.OpenAsync();
    await DatabaseHelpers.SetSearchPathAsync(db);
    await using var command = db.CreateCommand();
    command.CommandText = "UPDATE \"Banking\".\"Customer\" SET \"FullName\"=@name WHERE \"Id\"=@id;";
    var nameParameter = command.Parameters.Add("name", NpgsqlTypes.NpgsqlDbType.Text);
    var idParameter = command.Parameters.Add("id", NpgsqlTypes.NpgsqlDbType.Integer);
    foreach (var id in customerIds)
    {
        nameParameter.Value = BenchmarkConstants.BaselineFullName(id);
        idParameter.Value = id;
        await command.ExecuteNonQueryAsync();
    }
}

static int GetInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= 0 ? value : fallback;

public static class BenchmarkMath
{
    public static double Percentile(IEnumerable<double> source, double percentile)
    {
        var values = source.OrderBy(x => x).ToArray();
        if (values.Length == 0) return 0;
        var index = (values.Length - 1) * percentile;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return values[lower];
        return values[lower] + (values[upper] - values[lower]) * (index - lower);
    }
}

public static class DatabaseHelpers
{
    public static async Task SetSearchPathAsync(NpgsqlConnection db)
    {
        await using var command = db.CreateCommand();
        command.CommandText = "SET search_path TO \"Banking\", \"Lending\", \"Accounting\";";
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<IReadOnlyList<int>> ReadCustomerIdsAsync(NpgsqlConnection db, int limit)
    {
        await using var command = db.CreateCommand();
        command.CommandText = """SELECT "Id" FROM "Banking"."Customer" ORDER BY "Id" LIMIT @limit;""";
        command.Parameters.AddWithValue("limit", limit);
        var ids = new List<int>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetInt32(0));
        return ids;
    }
}

public enum FlowKind
{
    Conventional,
    Foundgine
}

public static class FlowKindExtensions
{
    public static string DisplayName(this FlowKind flow) =>
        flow == FlowKind.Conventional ? "Conventional" : "Foundgine";

    public static string SystemPrompt(this FlowKind flow) => flow == FlowKind.Conventional
        ? "You are a careful banking application agent. Use the available application tools to complete the request as one stateful process: query, mutate, query to verify, mutate again, then query to verify the final state. Inspect unfamiliar schema and use only returned fields."
        : "You are a careful banking agent using Foundgine. Treat semantic capabilities as the authoritative domain interface. Complete the request as one stateful process: query, mutate, query to verify, mutate again, then query to verify the final state. Do not request raw SQL or physical schema details.";
}

public static class BenchmarkScenario
{
    public static string Request(int customerId) => $"""
                                                     Review Customer {customerId} in the benchmark fixture as one stateful process.

                                                     1. QUERY: Traverse the customer's banking relationships, contracts and transactions and calculate total exposure as the sum of transaction Balance values.
                                                     2. MUTATION #1: If exposure is at least {BenchmarkConstants.ExposureThreshold:N0}, mark the customer as reviewed by setting FullName to exactly `{BenchmarkConstants.ReviewedFullName(customerId)}`.
                                                     3. QUERY: Re-read the customer graph and verify that the first mutation was applied and that exposure and relationship/contract/transaction counts are unchanged.
                                                     4. MUTATION #2: After the verification succeeds, mark the remediation follow-up complete by setting FullName to exactly `{BenchmarkConstants.FinalFullName(customerId)}`.
                                                     5. FINAL QUERY: Re-read the complete customer graph and verify the final state.

                                                     Do not modify any other customer or business data. Return the final customer key, relationship count, contract count, transaction count, exposure and final full name.
                                                     """;
}

public static class BenchmarkConstants
{
    public const decimal ExposureThreshold = 48_000m;
    public static string BaselineFullName(int customerId) => $"Customer {customerId} Benchmark";
    public static string ReviewedFullName(int customerId) => $"Customer {customerId} Benchmark | Reviewed";

    public static string FinalFullName(int customerId) =>
        $"Customer {customerId} Benchmark | Reviewed | Remediation Complete";
}

/// <summary>
/// Offline heuristic used by the current benchmark methodology.
/// Estimate = max(chars / 4, words * 1.3), rounded to the nearest whole token.
/// It is applied to every recorded tool input and tool output, plus the fixed
/// system prompt and scenario request. It does not estimate model reasoning or
/// model response tokens. Provider-reported usage remains authoritative in live mode.
/// </summary>
public static class TokenEstimator
{
    public static long Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var chars = text.Length;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return (long)Math.Round(Math.Max(chars / 4.0, words * 1.3), MidpointRounding.AwayFromZero);
    }

    public static long FixedOverheadTokens(FlowKind flow, int customerId) =>
        Estimate(flow.SystemPrompt()) + Estimate(BenchmarkScenario.Request(customerId));
}

public sealed class TraceCollector
{
    private readonly List<TraceEvent> _events = [];
    private long _estimatedToolInputTokens;
    private long _estimatedToolOutputTokens;
    private long _agentToolPayloadBytes;
    private long _agentToolRoundTrips;
    private long _providerInputTokens;
    private long _providerOutputTokens;
    private long _providerTotalTokens;
    private long _cachedInputTokens;
    private double _modelTimeMs;
    private double _toolTimeMs;

    public TraceCollector(FlowKind flow, int customerId)
    {
        Flow = flow;
        CustomerId = customerId;
    }

    public FlowKind Flow { get; }
    public int CustomerId { get; }
    public double WallClockMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }

    public void ModelCall(ModelUsage usage, double elapsedMs)
    {
        _modelTimeMs += elapsedMs;
        _providerInputTokens += usage.InputTokens;
        _providerOutputTokens += usage.OutputTokens;
        _providerTotalTokens += usage.TotalTokens;
        _cachedInputTokens += usage.CachedInputTokens;
        _events.Add(new TraceEvent(DateTimeOffset.UtcNow, "model", "model.call", "", "", elapsedMs, usage));
    }

    public void ToolCall(string name, string input, string output, double elapsedMs)
    {
        _toolTimeMs += elapsedMs;
        _estimatedToolInputTokens += TokenEstimator.Estimate(input);
        _estimatedToolOutputTokens += TokenEstimator.Estimate(output);
        _agentToolPayloadBytes += Encoding.UTF8.GetByteCount(input) + Encoding.UTF8.GetByteCount(output);
        _agentToolRoundTrips++;
        _events.Add(new TraceEvent(DateTimeOffset.UtcNow, "tool", name, input, output, elapsedMs, null));
    }

    public void FinalState(string output) =>
        _events.Add(new TraceEvent(DateTimeOffset.UtcNow, "final", "final.state", "", output, 0, null));

    public RunResult ToResult(bool includeTrace, long peakActiveHttpRequests, long httpRequests, long httpRetries)
    {
        var estimatedContext = TokenEstimator.FixedOverheadTokens(Flow, CustomerId) + _estimatedToolInputTokens +
                               _estimatedToolOutputTokens;
        return new(
            0, Flow.DisplayName(), CustomerId, WallClockMs, _modelTimeMs, _toolTimeMs,
            _events.Count(e => e.Kind == "model"), _events.Count(e => e.Kind == "tool"),
            _providerInputTokens, _providerOutputTokens, _providerTotalTokens, _cachedInputTokens,
            _estimatedToolInputTokens, _estimatedToolOutputTokens, estimatedContext,
            _agentToolPayloadBytes,
            _agentToolRoundTrips,
            _events.LastOrDefault(e => e.Name == "final.state")?.Output,
            includeTrace ? _events : null, Success, ErrorType, ErrorMessage, peakActiveHttpRequests, httpRequests,
            httpRetries);
    }
}

public sealed record TraceEvent(
    DateTimeOffset Timestamp,
    string Kind,
    string Name,
    string Input,
    string? Output,
    double ElapsedMs,
    ModelUsage? Usage);

public sealed record ModelUsage(long InputTokens, long OutputTokens, long TotalTokens, long CachedInputTokens = 0);

public sealed record RunResult(
    int Run,
    string Flow,
    int CustomerId,
    double WallClockMs,
    double ModelTimeMs,
    double ToolTimeMs,
    int ModelCalls,
    int ToolCalls,
    long ProviderInputTokens,
    long ProviderOutputTokens,
    long ProviderTotalTokens,
    long CachedInputTokens,
    long EstimatedToolInputTokens,
    long EstimatedToolOutputTokens,
    long EstimatedContextLoadTokens,
    long AgentToolPayloadBytes,
    long AgentToolRoundTrips,
    string? FinalState,
    IReadOnlyList<TraceEvent>? Trace,
    bool Success,
    string? ErrorType,
    string? ErrorMessage,
    long PeakActiveHttpRequests,
    long HttpRequests,
    long HttpRetries);

public sealed record ScenarioSnapshot(
    int CustomerId,
    Guid CustomerKey,
    string? FullName,
    int RelationshipCount,
    int ContractCount,
    int TransactionCount,
    decimal Exposure)
{
    public static async Task<ScenarioSnapshot> ReadAsync(NpgsqlConnection db, int customerId)
    {
        await using var command = db.CreateCommand();
        command.CommandText = """
                              SELECT c."Id", c."CustomerKey", c."FullName",
                                     COUNT(DISTINCT r."Id"), COUNT(DISTINCT ct."Id"), COUNT(DISTINCT t."Id"),
                                     COALESCE(SUM(t."Balance"), 0)
                              FROM "Banking"."Customer" c
                              LEFT JOIN "Banking"."CustomerBankingRelationship" r ON r."CustomerId"=c."Id"
                              LEFT JOIN "Lending"."Contract" ct ON ct."CustomerBankingRelationshipId"=r."Id"
                              LEFT JOIN "Lending"."Transaction" t ON t."ContractId"=ct."Id"
                              WHERE c."Id"=@id
                              GROUP BY c."Id", c."CustomerKey", c."FullName";
                              """;
        command.Parameters.AddWithValue("id", customerId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException($"Customer {customerId} was not found.");
        return new(reader.GetInt32(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetDecimal(6));
    }
}

public sealed class ToolRegistry
{
    private readonly Dictionary<string, Func<string, Task<string>>> _handlers;
    public ToolRegistry(Dictionary<string, Func<string, Task<string>>> handlers) => _handlers = handlers;

    public async Task<string> InvokeAsync(string name, string input, TraceCollector trace)
    {
        if (!_handlers.TryGetValue(name, out var handler))
            throw new InvalidOperationException($"Unknown tool '{name}'.");
        var sw = Stopwatch.StartNew();
        var output = await handler(input);
        sw.Stop();
        trace.ToolCall(name, input, output, sw.Elapsed.TotalMilliseconds);
        return output;
    }

    public IReadOnlyList<string> Names => _handlers.Keys.ToArray();
}

public static class ConventionalTools
{
    public static ToolRegistry Create(string connectionString) => new(new(StringComparer.Ordinal)
    {
        ["describe_schema"] = _ =>
            Task.FromResult(
                "Customer(Id, CustomerKey, FullName) -> CustomerBankingRelationship(Id, CustomerId) -> Contract(Id, CustomerBankingRelationshipId, Amount) -> Transaction(Id, ContractId, Amount, Balance)."),
        ["find_customer"] = input => QueryAsync(connectionString,
            "SELECT \"Id\", \"CustomerKey\", \"FullName\" FROM \"Banking\".\"Customer\" WHERE \"Id\"=@id", input,
            "customer"),
        ["list_relationships"] = input => QueryAsync(connectionString,
            "SELECT \"Id\" FROM \"Banking\".\"CustomerBankingRelationship\" WHERE \"CustomerId\"=@id ORDER BY \"Id\"",
            input, "relationships"),
        ["list_contracts"] = input => QueryAsync(connectionString,
            "SELECT ct.\"Id\", ct.\"Amount\" FROM \"Lending\".\"Contract\" ct JOIN \"Banking\".\"CustomerBankingRelationship\" r ON r.\"Id\"=ct.\"CustomerBankingRelationshipId\" WHERE r.\"CustomerId\"=@id ORDER BY ct.\"Id\"",
            input, "contracts"),
        ["list_transactions"] = input => QueryAsync(connectionString,
            "SELECT t.\"Id\", t.\"Amount\", t.\"Balance\" FROM \"Lending\".\"Transaction\" t JOIN \"Lending\".\"Contract\" ct ON ct.\"Id\"=t.\"ContractId\" JOIN \"Banking\".\"CustomerBankingRelationship\" r ON r.\"Id\"=ct.\"CustomerBankingRelationshipId\" WHERE r.\"CustomerId\"=@id ORDER BY t.\"Id\"",
            input, "transactions"),
        ["update_customer"] = input => UpdateCustomerAsync(connectionString, input),
        ["verify_customer"] = input => VerifyAsync(connectionString, input)
    });

    static async Task<string> QueryAsync(string cs, string sql, string input, string kind)
    {
        var node = JsonNode.Parse(input) ?? throw new InvalidOperationException("Invalid tool input.");
        var id = node["customerId"]?.GetValue<int>() ?? throw new InvalidOperationException("customerId is required.");
        await using var db = new NpgsqlConnection(cs);
        await db.OpenAsync();
        await DatabaseHelpers.SetSearchPathAsync(db);
        await using var command = db.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return JsonSerializer.Serialize(new { kind, rows });
    }

    static async Task<string> UpdateCustomerAsync(string cs, string input)
    {
        var node = JsonNode.Parse(input) ?? throw new InvalidOperationException("Invalid mutation input.");
        var id = node["customerId"]!.GetValue<int>();
        var name = node["fullName"]!.GetValue<string>();
        await using var db = new NpgsqlConnection(cs);
        await db.OpenAsync();
        await DatabaseHelpers.SetSearchPathAsync(db);
        await using var command = db.CreateCommand();
        command.CommandText =
            "UPDATE \"Banking\".\"Customer\" SET \"FullName\"=@name WHERE \"Id\"=@id RETURNING \"Id\", \"CustomerKey\", \"FullName\";";
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Update returned no row.");
        return JsonSerializer.Serialize(new
            { id = reader.GetInt32(0), customerKey = reader.GetGuid(1), fullName = reader.GetString(2) });
    }

    static async Task<string> VerifyAsync(string cs, string input)
    {
        var node = JsonNode.Parse(input) ?? throw new InvalidOperationException("Invalid verify input.");
        var id = node["customerId"]!.GetValue<int>();
        await using var db = new NpgsqlConnection(cs);
        await db.OpenAsync();
        await DatabaseHelpers.SetSearchPathAsync(db);
        return JsonSerializer.Serialize(await ScenarioSnapshot.ReadAsync(db, id));
    }
}

public static class BenchmarkHttp
{
    private static readonly SocketsHttpHandler Handler = new()
    {
        MaxConnectionsPerServer = 128,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    };

    public static readonly HttpClient Client = new(Handler)
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static long _active;
    private static long _peak;
    private static long _requests;
    private static long _retries;

    public static long Active => Interlocked.Read(ref _active);
    public static long Peak => Interlocked.Read(ref _peak);
    public static long Requests => Interlocked.Read(ref _requests);
    public static long Retries => Interlocked.Read(ref _retries);

    public static void ResetCounters()
    {
        Interlocked.Exchange(ref _active, 0);
        Interlocked.Exchange(ref _peak, 0);
        Interlocked.Exchange(ref _requests, 0);
        Interlocked.Exchange(ref _retries, 0);
    }

    public static async Task<HttpResponseMessage> PostAsync(string url, HttpContent content,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            Interlocked.Increment(ref _requests);
            var active = Interlocked.Increment(ref _active);
            while (true)
            {
                var peak = Interlocked.Read(ref _peak);
                if (active <= peak || Interlocked.CompareExchange(ref _peak, active, peak) == peak) break;
            }

            try
            {
                // HttpContent is single-use for retries; clone the small JSON payload per attempt.
                var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
                using var requestContent = new ByteArrayContent(bytes);
                requestContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                return await Client.PostAsync(url, requestContent, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                Interlocked.Increment(ref _retries);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * Math.Pow(2, attempt - 1)), cancellationToken);
            }
            catch (SocketException) when (attempt < maxAttempts)
            {
                Interlocked.Increment(ref _retries);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * Math.Pow(2, attempt - 1)), cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        throw new InvalidOperationException("HTTP retry loop exhausted unexpectedly.");
    }
}

public static class FoundgineTools
{
    public static ToolRegistry Create(string url, int customerId) => new(new(StringComparer.Ordinal)
    {
        ["semantic_capability"] = _ =>
            Task.FromResult(
                "Capability: customer exposure review. Semantic path: Customer -> CustomerBankingRelationship -> Contract -> Transaction. Provider details are not exposed to the agent."),
        ["semantic_graph"] = _ => GraphAsync(url, customerId),
        ["semantic_mutation"] = input => MutationAsync(url, input, customerId),
        ["semantic_verify"] = _ => GraphAsync(url, customerId)
    });

    static Task<string> GraphAsync(string url, int customerId)
    {
        var query = $$"""
                      query ReviewCustomer {
                        customer(where: { id: { eq: {{customerId}} } }, first: 1) {
                          id customerKey firstName lastName fullName
                          customerBankingRelationship {
                            id customerBankingRelationshipKey
                            contract { id contractKey amount transaction { id transactionKey amount balance } }
                          }
                        }
                      }
                      """;
        return PostGraphqlAsync(url, query, new { });
    }

    static async Task<string> MutationAsync(string url, string input, int customerId)
    {
        var node = JsonNode.Parse(input) ?? throw new InvalidOperationException("Invalid mutation input.");
        var name = node["fullName"]!.GetValue<string>();
        var query = "mutation ReviewCustomer { updateCustomer(input: { fullName: " + JsonSerializer.Serialize(name) +
                    " }, where: { id: { eq: " + customerId + " } }) { id customerKey firstName lastName fullName } }";
        return await PostGraphqlAsync(url, query, new { });
    }

    static async Task<string> PostGraphqlAsync(string url, string query, object variables)
    {
        var body = JsonSerializer.Serialize(new { query, variables });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await BenchmarkHttp.PostAsync(url, content);
        var result = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        return result;
    }
}

public static class ConventionalReplay
{
    public static async Task RunAsync(string cs, TraceCollector trace, int customerId)
    {
        var tools = ConventionalTools.Create(cs);

        // QUERY #1: inspect schema and traverse the customer's complete exposure graph.
        await tools.InvokeAsync("describe_schema", "{}", trace);
        await tools.InvokeAsync("find_customer", JsonSerializer.Serialize(new { customerId }), trace);
        await tools.InvokeAsync("list_relationships", JsonSerializer.Serialize(new { customerId }), trace);
        await tools.InvokeAsync("list_contracts", JsonSerializer.Serialize(new { customerId }), trace);
        await tools.InvokeAsync("list_transactions", JsonSerializer.Serialize(new { customerId }), trace);

        // MUTATION #1: mark the customer as reviewed.
        await tools.InvokeAsync(
            "update_customer",
            JsonSerializer.Serialize(new { customerId, fullName = BenchmarkConstants.ReviewedFullName(customerId) }),
            trace);

        // QUERY #2: verify the first mutation before continuing the process.
        await tools.InvokeAsync("verify_customer", JsonSerializer.Serialize(new { customerId }), trace);

        // MUTATION #2: complete the remediation follow-up.
        await tools.InvokeAsync(
            "update_customer",
            JsonSerializer.Serialize(new { customerId, fullName = BenchmarkConstants.FinalFullName(customerId) }),
            trace);

        // QUERY #3: final verification.
        await tools.InvokeAsync("verify_customer", JsonSerializer.Serialize(new { customerId }), trace);
    }
}

public static class FoundgineReplay
{
    public static async Task RunAsync(string url, TraceCollector trace, int customerId)
    {
        var tools = FoundgineTools.Create(url, customerId);

        // QUERY #1: semantic exposure graph.
        await tools.InvokeAsync("semantic_capability", "{}", trace);
        await tools.InvokeAsync("semantic_graph", "{}", trace);

        // MUTATION #1: mark the customer as reviewed.
        await tools.InvokeAsync(
            "semantic_mutation",
            JsonSerializer.Serialize(new { fullName = BenchmarkConstants.ReviewedFullName(customerId) }),
            trace);

        // QUERY #2: verify the first mutation before continuing the process.
        await tools.InvokeAsync("semantic_verify", "{}", trace);

        // MUTATION #2: complete the remediation follow-up.
        await tools.InvokeAsync(
            "semantic_mutation",
            JsonSerializer.Serialize(new { fullName = BenchmarkConstants.FinalFullName(customerId) }),
            trace);

        // QUERY #3: final verification.
        await tools.InvokeAsync("semantic_verify", "{}", trace);
    }
}

public sealed class OpenAiCompatibleAgentClient
{
    private readonly HttpClient _http = new();
    private readonly string _endpoint;
    private readonly string _model;

    public OpenAiCompatibleAgentClient(string endpoint, string apiKey, string model)
    {
        _endpoint = endpoint;
        _model = model;
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task RunAsync(string systemPrompt, string request, ToolRegistry tools, TraceCollector trace)
    {
        var definitions = new JsonArray(tools.Names.Select(name =>
            (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = name,
                    ["description"] = "Benchmark tool " + name,
                    ["parameters"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = true }
                }
            }).ToArray());

        var messages = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = systemPrompt },
            new JsonObject { ["role"] = "user", ["content"] = request });

        for (var iteration = 0; iteration < 16; iteration++)
        {
            var payload = new JsonObject
            {
                ["model"] = _model,
                ["messages"] = messages,
                ["tools"] = definitions,
                ["tool_choice"] = "auto",
                ["temperature"] = 0
            };
            var sw = Stopwatch.StartNew();
            using var response = await _http.PostAsync(_endpoint,
                new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
            var text = await response.Content.ReadAsStringAsync();
            sw.Stop();
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(text);
            var message = document.RootElement.GetProperty("choices")[0].GetProperty("message");
            trace.ModelCall(ParseUsage(document.RootElement), sw.Elapsed.TotalMilliseconds);
            messages.Add(JsonNode.Parse(message.GetRawText())!);
            if (!message.TryGetProperty("tool_calls", out var calls) || calls.GetArrayLength() == 0) return;
            foreach (var call in calls.EnumerateArray())
            {
                var id = call.GetProperty("id").GetString()!;
                var function = call.GetProperty("function");
                var name = function.GetProperty("name").GetString()!;
                var arguments = function.GetProperty("arguments").GetString() ?? "{}";
                var result = await tools.InvokeAsync(name, arguments, trace);
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = result });
            }
        }

        throw new InvalidOperationException("Agent exceeded 16 iterations.");
    }

    static ModelUsage ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage)) return new(0, 0, 0);
        var input = ReadLong(usage, "prompt_tokens", "input_tokens");
        var output = ReadLong(usage, "completion_tokens", "output_tokens");
        var total = ReadLong(usage, "total_tokens");
        if (total == 0) total = input + output;
        var cached = 0L;
        if (usage.TryGetProperty("prompt_tokens_details", out var p)) cached = ReadLong(p, "cached_tokens");
        if (cached == 0 && usage.TryGetProperty("input_tokens_details", out var i))
            cached = ReadLong(i, "cached_tokens");
        return new(input, output, total, cached);
    }

    static long ReadLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number))
                return number;
        return 0;
    }
}

public sealed record ExpectedState(
    int CustomerId,
    ScenarioSnapshot Baseline,
    ScenarioSnapshot Intermediate,
    ScenarioSnapshot Expected,
    bool ReviewRuleApplied);

public sealed record ExpectedStateVerification(
    int Run,
    string Flow,
    int CustomerId,
    bool IsMatch,
    ScenarioSnapshot Expected,
    ScenarioSnapshot? Actual,
    IReadOnlyList<string> Differences);

public static class ExpectedStateBuilder
{
    public static async Task<IReadOnlyList<ExpectedState>> BuildAsync(string connectionString,
        IReadOnlyList<int> customerIds)
    {
        await using var db = new NpgsqlConnection(connectionString);
        await db.OpenAsync();
        await DatabaseHelpers.SetSearchPathAsync(db);
        var states = new List<ExpectedState>();
        foreach (var customerId in customerIds.Distinct())
        {
            var baseline = await ScenarioSnapshot.ReadAsync(db, customerId);
            var review = baseline.Exposure >= BenchmarkConstants.ExposureThreshold;
            var intermediate = baseline with
            {
                FullName = review ? BenchmarkConstants.ReviewedFullName(customerId) : baseline.FullName
            };
            var expected = intermediate with
            {
                FullName = review ? BenchmarkConstants.FinalFullName(customerId) : intermediate.FullName
            };
            states.Add(new ExpectedState(customerId, baseline, intermediate, expected, review));
        }

        return states;
    }
}

public static class ExpectedStateVerifier
{
    public static ExpectedStateVerification Verify(RunResult result, IReadOnlyList<ExpectedState> expectedStates)
    {
        var expectedState = expectedStates.FirstOrDefault(x => x.CustomerId == result.CustomerId)
                            ?? throw new InvalidOperationException(
                                $"No expected state exists for customer {result.CustomerId}.");
        ScenarioSnapshot? actual = null;
        var differences = new List<string>();
        if (string.IsNullOrWhiteSpace(result.FinalState))
            differences.Add("Final state was not recorded.");
        else
        {
            try
            {
                actual = JsonSerializer.Deserialize<ScenarioSnapshot>(result.FinalState);
                if (actual is null) differences.Add("Final state could not be deserialized.");
            }
            catch (Exception ex)
            {
                differences.Add($"Final state JSON is invalid: {ex.Message}");
            }
        }

        if (actual is not null)
        {
            Compare(differences, "CustomerId", expectedState.Expected.CustomerId, actual.CustomerId);
            Compare(differences, "CustomerKey", expectedState.Expected.CustomerKey, actual.CustomerKey);
            Compare(differences, "FullName", expectedState.Expected.FullName, actual.FullName);
            Compare(differences, "RelationshipCount", expectedState.Expected.RelationshipCount,
                actual.RelationshipCount);
            Compare(differences, "ContractCount", expectedState.Expected.ContractCount, actual.ContractCount);
            Compare(differences, "TransactionCount", expectedState.Expected.TransactionCount, actual.TransactionCount);
            Compare(differences, "Exposure", expectedState.Expected.Exposure, actual.Exposure);
        }

        return new ExpectedStateVerification(result.Run, result.Flow, result.CustomerId, differences.Count == 0,
            expectedState.Expected, actual, differences);
    }

    static void Compare<T>(List<string> differences, string name, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            differences.Add($"{name}: expected={expected}, actual={actual}");
    }
}

public sealed record Comparison(
    Summary Conventional,
    Summary Foundgine,
    double EstimatedContextLoadSavingPercent,
    double ToolCallSavingPercent,
    double AgentToolPayloadSavingPercent,
    double AgentToolRoundTripSavingPercent,
    double ModelCallSavingPercent,
    double ProviderInputTokenSavingPercent,
    double ProviderTotalTokenSavingPercent,
    bool HasProviderTokenData,
    bool ExpectedFinalStateVerified,
    IReadOnlyList<ExpectedStateVerification> Verification)
{
    public static Comparison Create(IReadOnlyList<RunResult> results, IReadOnlyList<ExpectedState> expectedStates)
    {
        var conventional = Summary.From(results.Where(x => x.Flow == "Conventional").ToArray());
        var foundgine = Summary.From(results.Where(x => x.Flow == "Foundgine").ToArray());
        var estimated = Saving(conventional.EstimatedContextLoadTokens, foundgine.EstimatedContextLoadTokens);
        var tool = Saving(conventional.ToolCalls, foundgine.ToolCalls);
        var payload = Saving(conventional.AgentToolPayloadBytes, foundgine.AgentToolPayloadBytes);
        var roundTrips = Saving(conventional.AgentToolRoundTrips, foundgine.AgentToolRoundTrips);
        var model = Saving(conventional.ModelCalls, foundgine.ModelCalls);
        var hasProvider = conventional.ProviderTotalTokens > 0 || foundgine.ProviderTotalTokens > 0;
        var verification = results.Select(x => ExpectedStateVerifier.Verify(x, expectedStates)).ToArray();
        return new(conventional, foundgine, estimated, tool, payload, roundTrips, model,
            Saving(conventional.ProviderInputTokens, foundgine.ProviderInputTokens),
            Saving(conventional.ProviderTotalTokens, foundgine.ProviderTotalTokens), hasProvider,
            verification.All(x => x.IsMatch), verification);
    }

    static double Saving(double baseline, double optimized) =>
        baseline == 0 ? 0 : (baseline - optimized) / baseline * 100.0;
}

public sealed record Summary(
    double WallClockMs,
    double ModelTimeMs,
    double ToolTimeMs,
    double ModelCalls,
    double ToolCalls,
    double AgentToolPayloadBytes,
    double AgentToolRoundTrips,
    double ProviderInputTokens,
    double ProviderOutputTokens,
    double ProviderTotalTokens,
    double CachedInputTokens,
    double EstimatedToolInputTokens,
    double EstimatedToolOutputTokens,
    double EstimatedContextLoadTokens,
    double SuccessRate,
    double P50WallClockMs,
    double P95WallClockMs,
    double P99WallClockMs,
    double PeakActiveHttpRequests,
    double HttpRetries)
{
    public static Summary From(IReadOnlyList<RunResult> values) => new(
        values.Count == 0 ? 0 : values.Average(x => x.WallClockMs),
        values.Count == 0 ? 0 : values.Average(x => x.ModelTimeMs),
        values.Count == 0 ? 0 : values.Average(x => x.ToolTimeMs),
        values.Count == 0 ? 0 : values.Average(x => x.ModelCalls),
        values.Count == 0 ? 0 : values.Average(x => x.ToolCalls),
        values.Count == 0 ? 0 : values.Average(x => x.AgentToolPayloadBytes),
        values.Count == 0 ? 0 : values.Average(x => x.AgentToolRoundTrips),
        values.Count == 0 ? 0 : values.Average(x => x.ProviderInputTokens),
        values.Count == 0 ? 0 : values.Average(x => x.ProviderOutputTokens),
        values.Count == 0 ? 0 : values.Average(x => x.ProviderTotalTokens),
        values.Count == 0 ? 0 : values.Average(x => x.CachedInputTokens),
        values.Count == 0 ? 0 : values.Average(x => x.EstimatedToolInputTokens),
        values.Count == 0 ? 0 : values.Average(x => x.EstimatedToolOutputTokens),
        values.Count == 0 ? 0 : values.Average(x => x.EstimatedContextLoadTokens),
        values.Count == 0 ? 0 : values.Count(x => x.Success) * 100.0 / values.Count,
        BenchmarkMath.Percentile(
            values.Where(x => x.Success).Select(x => x.WallClockMs), 0.50),
        BenchmarkMath.Percentile(
            values.Where(x => x.Success).Select(x => x.WallClockMs), 0.95),
        BenchmarkMath.Percentile(
            values.Where(x => x.Success).Select(x => x.WallClockMs), 0.99),
        values.Count == 0 ? 0 : values.Max(x => x.PeakActiveHttpRequests),
        values.Sum(x => x.HttpRetries));
}

public sealed record BenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string Scenario,
    string Mode,
    int Runs,
    int Warmups,
    int Concurrency,
    int CustomerId,
    ScenarioSnapshot Fixture,
    IReadOnlyList<RunResult> Results,
    Comparison Comparison)
{
    public string ToMarkdown()
    {
        var c = Comparison.Conventional;
        var f = Comparison.Foundgine;
        var sb = new StringBuilder();
        sb.AppendLine($"# Foundgine Agent End-to-End Benchmark — {Scenario}");
        sb.AppendLine();
        sb.AppendLine($"Generated: `{GeneratedAtUtc:O}`  ");
        sb.AppendLine($"Mode: `{Mode}`  ");
        sb.AppendLine(
            $"Concurrency: `{Concurrency}`; runs: `{Runs}` measured / `{Warmups}` warmups; fixture customer `{CustomerId}`");
        sb.AppendLine();
        sb.AppendLine("## Headline");
        sb.AppendLine();
        sb.AppendLine($"- Estimated context-load saving: **{Comparison.EstimatedContextLoadSavingPercent:F1}%**");
        sb.AppendLine($"- Agent/tool round-trip saving: **{Comparison.AgentToolRoundTripSavingPercent:F1}%**");
        sb.AppendLine($"- Agent/tool payload saving: **{Comparison.AgentToolPayloadSavingPercent:F1}%**");
        sb.AppendLine($"- Tool-call saving: **{Comparison.ToolCallSavingPercent:F1}%**");
        sb.AppendLine($"- Model-call saving: **{Comparison.ModelCallSavingPercent:F1}%**");
        sb.AppendLine(
            $"- Provider-reported input-token saving: **{(Comparison.HasProviderTokenData ? Comparison.ProviderInputTokenSavingPercent.ToString("F1") + "%" : "N/A — replay mode")}**");
        sb.AppendLine(
            $"- Provider-reported total-token saving: **{(Comparison.HasProviderTokenData ? Comparison.ProviderTotalTokenSavingPercent.ToString("F1") + "%" : "N/A — replay mode")}**");
        sb.AppendLine($"- Expected final state verified: **{Comparison.ExpectedFinalStateVerified}**");
        sb.AppendLine($"- Verification failures: **{Comparison.Verification.Count(x => !x.IsMatch)}**");
        sb.AppendLine();
        sb.AppendLine("## Method");
        sb.AppendLine();
        sb.AppendLine(
            "Estimated tokens use the current benchmark method: `max(chars / 4, words × 1.3)`, rounded to the nearest whole token. The estimator is applied to every recorded tool input and tool output, then the fixed system prompt and scenario request are added once per run. This is a directional BPE approximation, not a provider tokenizer. It does not include model reasoning or model response tokens. Live runs also record provider-reported usage, which is the authoritative token measurement.");
        sb.AppendLine();
        sb.AppendLine("## Estimated model usage / run");
        sb.AppendLine();
        sb.AppendLine(
            "These are separate measurements: input/output token estimates describe the recorded agent/tool payloads, while context load includes the fixed system/request overhead. They should not be added together.");
        sb.AppendLine();
        sb.AppendLine($"- Context-load saving: **{Comparison.EstimatedContextLoadSavingPercent:F1}%**");
        sb.AppendLine(
            $"- Provider input-token saving: **{(Comparison.HasProviderTokenData ? Comparison.ProviderInputTokenSavingPercent.ToString("F1") + "%" : "Not measured (replay mode)")}**");
        sb.AppendLine(
            $"- Provider total-token saving: **{(Comparison.HasProviderTokenData ? Comparison.ProviderTotalTokenSavingPercent.ToString("F1") + "%" : "Not measured (replay mode)")}**");
        sb.AppendLine();
        sb.AppendLine("## Averages");
        sb.AppendLine();
        sb.AppendLine("| Metric | Conventional | Foundgine |");
        sb.AppendLine("|---|---:|---:|");
        Add(sb, "Wall clock (ms)", c.WallClockMs, f.WallClockMs);
        Add(sb, "Model time (ms)", c.ModelTimeMs, f.ModelTimeMs);
        Add(sb, "Tool time (ms)", c.ToolTimeMs, f.ToolTimeMs);
        Add(sb, "Success rate (%)", c.SuccessRate, f.SuccessRate);
        Add(sb, "p50 wall (ms)", c.P50WallClockMs, f.P50WallClockMs);
        Add(sb, "p95 wall (ms)", c.P95WallClockMs, f.P95WallClockMs);
        Add(sb, "p99 wall (ms)", c.P99WallClockMs, f.P99WallClockMs);
        Add(sb, "Peak active HTTP requests", c.PeakActiveHttpRequests, f.PeakActiveHttpRequests);
        Add(sb, "HTTP retries", c.HttpRetries, f.HttpRetries);
        Add(sb, "Model calls", c.ModelCalls, f.ModelCalls);
        Add(sb, "Tool calls", c.ToolCalls, f.ToolCalls);
        Add(sb, "Agent/tool round trips", c.AgentToolRoundTrips, f.AgentToolRoundTrips);
        Add(sb, "Agent/tool payload bytes", c.AgentToolPayloadBytes, f.AgentToolPayloadBytes);
        Add(sb, "Estimated tool-input tokens", c.EstimatedToolInputTokens, f.EstimatedToolInputTokens);
        Add(sb, "Estimated tool-output tokens", c.EstimatedToolOutputTokens, f.EstimatedToolOutputTokens);
        Add(sb, "Estimated context-load tokens", c.EstimatedContextLoadTokens, f.EstimatedContextLoadTokens);
        Add(sb, "Provider input tokens", c.ProviderInputTokens, f.ProviderInputTokens);
        Add(sb, "Provider output tokens", c.ProviderOutputTokens, f.ProviderOutputTokens);
        Add(sb, "Provider total tokens", c.ProviderTotalTokens, f.ProviderTotalTokens);
        Add(sb, "Cached input tokens", c.CachedInputTokens, f.CachedInputTokens);
        sb.AppendLine();
        sb.AppendLine("## Expected-state verification");
        sb.AppendLine();
        sb.AppendLine(
            "Each flow is compared against an explicit expected state generated from the reset baseline. The process is intentionally stateful: QUERY #1 reads the exposure graph, MUTATION #1 marks the customer reviewed, QUERY #2 verifies that intermediate state, MUTATION #2 completes the remediation follow-up, and QUERY #3 verifies the final state. The expected state preserves CustomerKey and all relationship/contract/transaction counts and exposure, with deterministic FullName transitions.");
        sb.AppendLine();
        sb.AppendLine("| Run | Flow | Customer | Match | Differences |");
        sb.AppendLine("|---:|---|---:|---|---|");
        foreach (var v in Comparison.Verification)
            sb.AppendLine(
                $"| {v.Run} | {v.Flow} | {v.CustomerId} | {(v.IsMatch ? "PASS" : "FAIL")} | {(v.IsMatch ? "—" : string.Join("; ", v.Differences))} |");
        sb.AppendLine();
        sb.AppendLine("## Trace interpretation");
        sb.AppendLine();
        sb.AppendLine(
            "The estimated context-load metric is intentionally narrower than a model's true prompt-token count. It measures the recorded application tool payloads plus the fixed system/request overhead specified by the benchmark methodology. It is useful for comparing how much tool-result context each architecture forces through the agent loop; it is not a replacement for provider-reported usage.");
        return sb.ToString();
    }

    static void Add(StringBuilder sb, string name, double c, double f) =>
        sb.AppendLine($"| {name} | {c:F1} | {f:F1} |");
}


static class AgentBenchmarkArgs
{
    public static int[] ParseIntArray(string[] args, string name, int[] defaults)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        return defaults;
    }
}