using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

const string ScenarioName = "customer-exposure-review";
const int DefaultCustomerId = 1;
const decimal ExposureThreshold = 48_000m;
const string BaselineFullName = "Customer 1 Benchmark";
const string ReviewedFullName = "Customer 1 Benchmark | Reviewed";

var mode = (Environment.GetEnvironmentVariable("AGENT_BENCHMARK_MODE") ?? "replay").Trim().ToLowerInvariant();
var runs = GetInt("AGENT_BENCHMARK_RUNS", 1);
var warmups = GetInt("AGENT_BENCHMARK_WARMUPS", 0);
var customerCount = GetInt("AGENT_BENCHMARK_CUSTOMER_COUNT", 1);
var concurrency = GetInt("AGENT_BENCHMARK_CONCURRENCY", 1);
var customerId = GetInt("AGENT_BENCHMARK_CUSTOMER_ID", DefaultCustomerId);
if (customerCount < 1) throw new InvalidOperationException("AGENT_BENCHMARK_CUSTOMER_COUNT must be positive.");
if (concurrency < 1) throw new InvalidOperationException("AGENT_BENCHMARK_CONCURRENCY must be positive.");
if (concurrency > customerCount)
    throw new InvalidOperationException(
        $"Concurrency {concurrency} exceeds customer count {customerCount}; each concurrent worker requires a distinct customer.");
var customerIds = Enumerable.Range(1, concurrency).ToArray();
if (customerId < 1 || customerId > customerCount) customerId = 1;
var reportDirectory = Environment.GetEnvironmentVariable("AGENT_BENCHMARK_REPORT_DIRECTORY") ??
                      "./artifacts/agent-benchmark";
var connectionString = Environment.GetEnvironmentVariable("BankingConnectionString")
                       ?? Environment.GetEnvironmentVariable("COFFEEBEANERY_CONNECTION")
                       ?? throw new InvalidOperationException(
                           "Set BankingConnectionString or COFFEEBEANERY_CONNECTION.");
var foundgineUrl = Environment.GetEnvironmentVariable("FOUNDGINE_GRAPHQL_URL") ?? "http://localhost:8080/graphql/warm";

Directory.CreateDirectory(reportDirectory);

Console.WriteLine("===============================================");
Console.WriteLine(" Foundgine / Agent End-to-End Benchmark");
Console.WriteLine("===============================================");
Console.WriteLine($"Scenario:     {ScenarioName}");
Console.WriteLine($"Mode:         {mode}");
Console.WriteLine($"Warmups:      {warmups}");
Console.WriteLine($"Measured:     {runs}");
Console.WriteLine($"Customer count: {customerCount}");
Console.WriteLine($"Concurrency:  {concurrency}");
Console.WriteLine($"Targets:      {string.Join(", ", customerIds)}");
Console.WriteLine($"Threshold:    {ExposureThreshold:N0}");
Console.WriteLine();

await using var db = new NpgsqlConnection(connectionString);
await db.OpenAsync();
await SetSearchPathAsync(db);

var scenario = await ScenarioSnapshot.ReadAsync(db, customerId);
Console.WriteLine(
    $"Fixture:      {scenario.RelationshipCount} relationships / {scenario.ContractCount} contracts / {scenario.TransactionCount} transactions");
Console.WriteLine($"Baseline:     {scenario.FullName}");

var allResults = new List<RunResult>();

foreach (var flow in new[] { FlowKind.Conventional, FlowKind.Foundgine })
{
    BenchmarkHttp.Reset();
    Console.WriteLine();
    Console.WriteLine($"== {flow.DisplayName()} ==");

    for (var i = 0; i < warmups; i++)
    {
        await ResetCustomersAsync(db, customerIds);
        await Task.WhenAll(customerIds.Select(id =>
            RunAsync(flow, mode, connectionString, foundgineUrl, id, record: false)));
    }

    for (var i = 1; i <= runs; i++)
    {
        await ResetCustomersAsync(db, customerIds);
        var workerResults = await Task.WhenAll(customerIds.Select(id =>
            RunAsync(flow, mode, connectionString, foundgineUrl, id, record: true)));
        var result = AggregateResults(workerResults, i);
        allResults.Add(result);
        Console.WriteLine(
            $"run={i} wall={result.WallClockMs:F1}ms model={result.ModelTimeMs:F1}ms tools={result.ToolTimeMs:F1}ms modelCalls={result.ModelCalls} toolCalls={result.ToolCalls} inputTokens={result.InputTokens} outputTokens={result.OutputTokens} totalTokens={result.TotalTokens} estContextLoadTokens~{result.EstimatedContextLoadTokens} workers={concurrency} httpRetries={BenchmarkHttp.Retries}");
    }
}

var comparison = Comparison.Create(allResults, scenario);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var report = new BenchmarkReport(
    GeneratedAtUtc: DateTimeOffset.UtcNow,
    Scenario: ScenarioName,
    Mode: mode,
    Configuration: new BenchmarkConfiguration(runs, warmups, customerId, ExposureThreshold, BaselineFullName,
        ReviewedFullName, customerCount, concurrency),
    Fixture: scenario,
    Results: allResults,
    Comparison: comparison);

var jsonPath = Path.Combine(reportDirectory, "agent-benchmark.json");
await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, jsonOptions));

var markdownPath = Path.Combine(reportDirectory, "agent-benchmark.md");
await File.WriteAllTextAsync(markdownPath, report.ToMarkdown());

Console.WriteLine();
Console.WriteLine("===============================================");
Console.WriteLine(" Comparison");
Console.WriteLine("===============================================");
if (comparison.HasProviderTokenData)
    Console.WriteLine(
        $"Token saving (provider-reported): {comparison.InputTokenSavingPercent:F1}% input / {comparison.TotalTokenSavingPercent:F1}% total");
else
    Console.WriteLine(
        $"Token saving (provider-reported): N/A — {mode} mode reports no model token usage (run in 'live' mode for real numbers)");
Console.WriteLine(
    $"Token saving (estimated, heuristic): {comparison.EstimatedContextLoadSavingPercent:F1}% — offline chars/words estimate of tool payload size, not a tokenizer count");
Console.WriteLine($"Tool-call saving:   {comparison.ToolCallSavingPercent:F1}%");
Console.WriteLine($"Model-call saving:  {comparison.ModelCallSavingPercent:F1}%");
Console.WriteLine($"Wall-clock change:  {comparison.WallClockChangePercent:F1}%");
Console.WriteLine($"Same final state:   {comparison.SameFinalState}");
Console.WriteLine($"JSON report:        {jsonPath}");
Console.WriteLine($"Markdown report:    {markdownPath}");

if (!comparison.SameFinalState)
    Environment.ExitCode = 2;

static async Task<RunResult> RunAsync(
    FlowKind flow,
    string mode,
    string connectionString,
    string foundgineUrl,
    int customerId,
    bool record)
{
    var trace = new TraceCollector(flow);
    var wall = Stopwatch.StartNew();

    if (mode == "live")
    {
        var endpoint = Environment.GetEnvironmentVariable("AGENT_MODEL_ENDPOINT")
                       ?? throw new InvalidOperationException("Live mode requires AGENT_MODEL_ENDPOINT.");
        var apiKey = Environment.GetEnvironmentVariable("AGENT_MODEL_API_KEY") ?? "";
        var model = Environment.GetEnvironmentVariable("AGENT_MODEL")
                    ?? throw new InvalidOperationException("Live mode requires AGENT_MODEL.");
        var client = new OpenAiCompatibleAgentClient(endpoint, apiKey, model);
        var tools = flow == FlowKind.Conventional
            ? ConventionalTools.Create(connectionString, trace, customerId)
            : FoundgineTools.Create(foundgineUrl, trace, null, customerId);
        await client.RunAsync(flow.SystemPrompt(), ScenarioRequest(customerId), tools, trace);
    }
    else if (mode == "replay")
    {
        if (flow == FlowKind.Conventional)
            await ConventionalReplay.RunAsync(connectionString, trace, customerId);
        else
            await FoundgineReplay.RunAsync(foundgineUrl, trace, customerId);
    }
    else
    {
        throw new InvalidOperationException("AGENT_BENCHMARK_MODE must be 'live' or 'replay'.");
    }

    await using (var verificationDb = new NpgsqlConnection(connectionString))
    {
        await verificationDb.OpenAsync();
        await SetSearchPathAsync(verificationDb);
        trace.FinalState(JsonSerializer.Serialize(await ScenarioSnapshot.ReadAsync(verificationDb, customerId)));
    }

    wall.Stop();
    trace.WallClockMs = wall.Elapsed.TotalMilliseconds;

    var result = trace.ToResult(record);
    return result;
}

static string ScenarioRequest(int customerId) => $"""
                                                  Review Customer {customerId} in the benchmark fixture. Traverse the customer's banking relationships, contracts and transactions and calculate total exposure as the sum of transaction Balance values. If exposure is at least 48,000, mark the customer as reviewed by setting FullName to exactly `{BenchmarkConstants.ReviewedName(customerId)}`. Then verify the final state. Return customer key, relationship count, contract count, transaction count, exposure and final full name. Do not modify any other customer or business data.
                                                  """;


static async Task ResetCustomersAsync(NpgsqlConnection db, IReadOnlyList<int> customerIds)
{
    await using var command = db.CreateCommand();
    command.CommandText = "UPDATE \"Banking\".\"Customer\" SET \"FullName\" = @name WHERE \"Id\" = @id;";
    var name = command.Parameters.Add("name", NpgsqlTypes.NpgsqlDbType.Text);
    var id = command.Parameters.Add("id", NpgsqlTypes.NpgsqlDbType.Integer);
    foreach (var customerId in customerIds)
    {
        name.Value = BenchmarkConstants.BaselineName(customerId);
        id.Value = customerId;
        await command.ExecuteNonQueryAsync();
    }
}

static RunResult AggregateResults(IReadOnlyList<RunResult> results, int run)
{
    if (results.Count == 0) throw new InvalidOperationException("No worker results were produced.");
    var primary = results.First(x => x.FinalState is not null);
    return new(
        run, primary.Flow, results.Max(x => x.WallClockMs), results.Sum(x => x.ModelTimeMs),
        results.Sum(x => x.ToolTimeMs),
        results.Sum(x => x.ModelCalls), results.Sum(x => x.ToolCalls), results.Sum(x => x.InputTokens),
        results.Sum(x => x.OutputTokens),
        results.Sum(x => x.TotalTokens), results.Sum(x => x.CachedInputTokens),
        results.Sum(x => x.EstimatedToolInputTokens),
        results.Sum(x => x.EstimatedToolOutputTokens), results.Sum(x => x.EstimatedContextLoadTokens),
        primary.FinalState, null);
}

static async Task SetSearchPathAsync(NpgsqlConnection db)
{
    await using var command = db.CreateCommand();
    command.CommandText = "SET search_path TO \"Banking\", \"Lending\", \"Accounting\";";
    await command.ExecuteNonQueryAsync();
}

static int GetInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= 0 ? value : fallback;

public static class BenchmarkConstants
{
    public const int CustomerId = 1;
    public const decimal ExposureThreshold = 48_000m;
    public const string BaselineFullName = "Customer 1 Benchmark";
    public const string ReviewedFullName = "Customer 1 Benchmark | Reviewed";

    public static string BaselineName(int customerId) => $"Customer {customerId} Benchmark";
    public static string ReviewedName(int customerId) => $"Customer {customerId} Benchmark | Reviewed";
}

public enum FlowKind
{
    Conventional,
    Foundgine
}

public static class FlowKindExtensions
{
    public static string DisplayName(this FlowKind flow) => flow == FlowKind.Conventional
        ? "Conventional application/AI flow"
        : "Foundgine semantic flow";

    public static string SystemPrompt(this FlowKind flow) => flow == FlowKind.Conventional
        ? "You are a careful banking application agent. Use the available application tools to complete the request. You must inspect the schema before querying unfamiliar data, never invent fields, and verify the final state after a mutation."
        : "You are a careful banking agent using Foundgine. Treat the semantic capability and graph/mutation tools as the authoritative domain interface. Do not request raw SQL or physical schema details. Verify the final state after a mutation.";
}

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
                              SELECT
                                  c."Id",
                                  c."CustomerKey",
                                  c."FullName",
                                  COUNT(DISTINCT r."Id") AS relationship_count,
                                  COUNT(DISTINCT ct."Id") AS contract_count,
                                  COUNT(DISTINCT t."Id") AS transaction_count,
                                  COALESCE(SUM(t."Balance"), 0) AS exposure
                              FROM "Banking"."Customer" c
                              LEFT JOIN "Banking"."CustomerBankingRelationship" r ON r."CustomerId" = c."Id"
                              LEFT JOIN "Lending"."Contract" ct ON ct."CustomerBankingRelationshipId" = r."Id"
                              LEFT JOIN "Lending"."Transaction" t ON t."ContractId" = ct."Id"
                              WHERE c."Id" = @id
                              GROUP BY c."Id", c."CustomerKey", c."FullName";
                              """;
        command.Parameters.AddWithValue("id", customerId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"Customer {customerId} was not found.");
        return new(
            reader.GetInt32(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetDecimal(6));
    }
}

/// <summary>
/// Offline, heuristic token-load estimator. This is NOT a real tokenizer and must never be
/// presented as provider-reported usage. It exists so that replay-mode runs — which never call
/// a model and therefore always report InputTokens/OutputTokens/TotalTokens == 0 — still carry a
/// directional signal about how much context each flow's tool payloads would cost if replayed
/// against a real model. Live-mode runs report both this estimate and the real provider usage,
/// so the estimate's accuracy can be checked against ground truth over time.
/// Method: tokens ~= max(chars / 4, words * 1.3), the standard order-of-magnitude approximation
/// for BPE-style tokenizers (cl100k_base, o200k_base, Claude's tokenizer, etc.) on English/JSON
/// payloads of the size seen in this benchmark. Typically within +/-15% of a real tokenizer count.
/// </summary>
public static class TokenEstimator
{
    public static int Estimate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var chars = text.Length;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)Math.Round(Math.Max(chars / 4.0, words * 1.3));
    }

    /// <summary>Fixed per-run overhead: the system prompt and the scenario request, which are
    /// identical on every run of a given flow and therefore paid once as input on every call.</summary>
    public static int FixedOverheadTokens(FlowKind flow) =>
        Estimate(flow.SystemPrompt()) + Estimate(ScenarioRequestText);

    // Kept in sync with the top-level ScenarioRequest() local function used for live-mode calls.
    public const string ScenarioRequestText =
        "Review Customer 1 in the benchmark fixture. Traverse the customer's banking relationships, contracts and transactions and calculate total exposure as the sum of transaction Balance values. If exposure is at least 48,000, mark the customer as reviewed by setting FullName to exactly `Customer 1 Benchmark | Reviewed`. Then verify the final state. Return customer key, relationship count, contract count, transaction count, exposure and final full name. Do not modify any other customer or business data.";
}

public sealed class TraceCollector
{
    private readonly List<TraceEvent> _events = [];
    private long _inputTokens;
    private long _outputTokens;
    private long _totalTokens;
    private long _cachedInputTokens;
    private double _modelTimeMs;
    private double _toolTimeMs;
    private long _estimatedToolInputTokens;
    private long _estimatedToolOutputTokens;

    public TraceCollector(FlowKind flow) => Flow = flow;
    public FlowKind Flow { get; }
    public double WallClockMs { get; set; }

    public void ModelCall(string requestSummary, ModelUsage usage, double elapsedMs)
    {
        _modelTimeMs += elapsedMs;
        _inputTokens += usage.InputTokens;
        _outputTokens += usage.OutputTokens;
        _totalTokens += usage.TotalTokens;
        _cachedInputTokens += usage.CachedInputTokens;
        _events.Add(
            new TraceEvent(DateTimeOffset.UtcNow, "model", "model.call", requestSummary, null, elapsedMs, usage));
    }

    public void ToolCall(string name, string input, string output, double elapsedMs)
    {
        _toolTimeMs += elapsedMs;
        _estimatedToolInputTokens += TokenEstimator.Estimate(input);
        _estimatedToolOutputTokens += TokenEstimator.Estimate(output);
        _events.Add(new TraceEvent(DateTimeOffset.UtcNow, "tool", name, input, output, elapsedMs, null));
    }

    public void FinalState(string output) =>
        _events.Add(new TraceEvent(DateTimeOffset.UtcNow, "final", "final.state", "{}", output, 0, null));

    public RunResult ToResult(bool includeTrace)
    {
        var fixedOverhead = TokenEstimator.FixedOverheadTokens(Flow);
        var estimatedContextLoad = fixedOverhead + _estimatedToolInputTokens + _estimatedToolOutputTokens;
        return new(
            Run: 0,
            Flow: Flow.DisplayName(),
            WallClockMs,
            ModelTimeMs: _modelTimeMs,
            ToolTimeMs: _toolTimeMs,
            ModelCalls: _events.Count(x => x.Kind == "model"),
            ToolCalls: _events.Count(x => x.Kind == "tool"),
            InputTokens: _inputTokens,
            OutputTokens: _outputTokens,
            TotalTokens: _totalTokens,
            CachedInputTokens: _cachedInputTokens,
            EstimatedToolInputTokens: _estimatedToolInputTokens,
            EstimatedToolOutputTokens: _estimatedToolOutputTokens,
            EstimatedContextLoadTokens: estimatedContextLoad,
            FinalState: _events.LastOrDefault(x => x.Name == "final.state")?.Output,
            Trace: includeTrace ? _events : null);
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
    double WallClockMs,
    double ModelTimeMs,
    double ToolTimeMs,
    int ModelCalls,
    int ToolCalls,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    long CachedInputTokens,
    long EstimatedToolInputTokens,
    long EstimatedToolOutputTokens,
    long EstimatedContextLoadTokens,
    string? FinalState,
    IReadOnlyList<TraceEvent>? Trace);

public static class BenchmarkScenario
{
    public static string Request() => """
                                      Find the highest-exposure eligible customer for the authenticated tenant with exposure above the configured threshold, then perform the authorized review mutation and verify the final state. The benchmark compares the conventional discovery/tool choreography against the Foundgine semantic capability choreography while asserting the same final state.
                                      """;
}

public sealed record BenchmarkConfiguration(
    int Runs,
    int Warmups,
    int CustomerId,
    decimal ExposureThreshold,
    string BaselineFullName,
    string ReviewedFullName,
    int CustomerCount = 1,
    int Concurrency = 1);

public sealed record BenchmarkReport(
    DateTimeOffset GeneratedAtUtc,
    string Scenario,
    string Mode,
    BenchmarkConfiguration Configuration,
    ScenarioSnapshot Fixture,
    IReadOnlyList<RunResult> Results,
    Comparison Comparison)
{
    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Foundgine Agent End-to-End Benchmark — {Scenario}");
        sb.AppendLine();
        sb.AppendLine($"Generated: `{GeneratedAtUtc:O}`  ");
        sb.AppendLine($"Mode: `{Mode}`  ");
        sb.AppendLine($"Runs: `{Configuration.Runs}` measured / `{Configuration.Warmups}` warmups");
        sb.AppendLine();
        sb.AppendLine("## Headline");
        sb.AppendLine();
        if (Comparison.HasProviderTokenData)
        {
            sb.AppendLine($"- Input-token saving (provider-reported): **{Comparison.InputTokenSavingPercent:F1}%**");
            sb.AppendLine($"- Total-token saving (provider-reported): **{Comparison.TotalTokenSavingPercent:F1}%**");
        }
        else
        {
            sb.AppendLine(
                $"- Input/total-token saving (provider-reported): **N/A** — `{Mode}` mode makes no model calls, so provider token usage is always zero. Run in `live` mode for real numbers.");
        }

        sb.AppendLine(
            $"- Estimated context-load saving (heuristic, all modes): **{Comparison.EstimatedContextLoadSavingPercent:F1}%** — see \"Tokens vs. API time vs. application load\" below.");
        sb.AppendLine($"- Tool-call saving: **{Comparison.ToolCallSavingPercent:F1}%**");
        sb.AppendLine($"- Model-call saving: **{Comparison.ModelCallSavingPercent:F1}%**");
        sb.AppendLine($"- Wall-clock change: **{Comparison.WallClockChangePercent:F1}%**");
        sb.AppendLine($"- Same final state: **{Comparison.SameFinalState}**");
        sb.AppendLine();
        sb.AppendLine("## Measured averages");
        sb.AppendLine();
        sb.AppendLine("| Metric | Conventional | Foundgine |");
        sb.AppendLine("|---|---:|---:|");
        Add("Wall clock (ms)", Comparison.Conventional.WallClockMs, Comparison.Foundgine.WallClockMs);
        Add("Model time (ms)", Comparison.Conventional.ModelTimeMs, Comparison.Foundgine.ModelTimeMs);
        Add("Tool time (ms)", Comparison.Conventional.ToolTimeMs, Comparison.Foundgine.ToolTimeMs);
        Add("Model calls", Comparison.Conventional.ModelCalls, Comparison.Foundgine.ModelCalls);
        Add("Tool calls", Comparison.Conventional.ToolCalls, Comparison.Foundgine.ToolCalls);
        Add("Input tokens", Comparison.Conventional.InputTokens, Comparison.Foundgine.InputTokens);
        Add("Output tokens", Comparison.Conventional.OutputTokens, Comparison.Foundgine.OutputTokens);
        Add("Total tokens", Comparison.Conventional.TotalTokens, Comparison.Foundgine.TotalTokens);
        Add("Cached input tokens", Comparison.Conventional.CachedInputTokens, Comparison.Foundgine.CachedInputTokens);
        Add("Estimated tool-input tokens (heuristic)", Comparison.Conventional.EstimatedToolInputTokens,
            Comparison.Foundgine.EstimatedToolInputTokens);
        Add("Estimated tool-output tokens (heuristic)", Comparison.Conventional.EstimatedToolOutputTokens,
            Comparison.Foundgine.EstimatedToolOutputTokens);
        Add("Estimated context load (heuristic, incl. system+request)",
            Comparison.Conventional.EstimatedContextLoadTokens, Comparison.Foundgine.EstimatedContextLoadTokens);
        sb.AppendLine();
        sb.AppendLine("## Method");
        sb.AppendLine();
        sb.AppendLine(
            "Both flows run against the same PostgreSQL fixture, the same authenticated benchmark request, the same deterministic Customer 1 graph, and the same final-state assertion. Live mode records provider-reported usage; replay mode is for validating the harness and must not be presented as real model-token evidence.");
        sb.AppendLine();
        sb.AppendLine("## Tokens vs. API time vs. application load");
        sb.AppendLine();
        sb.AppendLine("These are three different measurements and none of them substitutes for the others:");
        sb.AppendLine();
        sb.AppendLine(
            "- **Tokens** measure how much *context* an agent has to carry — the size of what it reads and writes. This is what drives per-request API cost and how much of a model's context window a task consumes. `Input/Output/TotalTokens` above are real, provider-reported numbers and are only populated in `live` mode. The `Estimated *` rows are an offline chars/words heuristic (see `TokenEstimator` in this file) that approximates the same thing from tool payload sizes alone, so replay mode still gives a directional signal instead of a hard zero.");
        sb.AppendLine(
            "- **API/model time** (`ModelTimeMs`) measures how long the model spent thinking and responding — wall-clock time actually billed to inference. It moves with tokens but not proportionally: a short, hard reasoning turn can cost more time than a long, easy one.");
        sb.AppendLine(
            "- **Application load** (`ToolTimeMs`, `WallClockMs`, CPU, memory) measures how long and how much compute the *application side* — the tool calls, the database, the semantic engine — spent doing the work the agent asked for. This can go up even when tokens go down: Foundgine's `WallClockMs` was higher than the conventional flow's in this replay precisely because it front-loads more resolution work into the application boundary so the agent doesn't have to.");
        sb.AppendLine();
        sb.AppendLine(
            "A flow can therefore win on one axis and lose on another. The headline claim this benchmark supports is narrower than \"faster\" or \"cheaper\": in this scenario, the semantic boundary reduced the number and size of round trips the agent had to coordinate (tool calls, and — per the estimate above — token load), at the cost of higher measured application wall-clock time in replay mode. Judge the trade-off against what you are optimizing for: per-call API spend and context-window pressure favor fewer/smaller round trips; raw end-to-end latency does not automatically follow.");
        sb.AppendLine();
        sb.AppendLine("## Scenario");
        sb.AppendLine();
        sb.AppendLine(BenchmarkScenario.Request());
        return sb.ToString();

        void Add(string name, double conventional, double foundgine) =>
            sb.AppendLine($"| {name} | {conventional:F1} | {foundgine:F1} |");
    }
}

public sealed record Comparison(
    Summary Conventional,
    Summary Foundgine,
    double InputTokenSavingPercent,
    double TotalTokenSavingPercent,
    double ToolCallSavingPercent,
    double ModelCallSavingPercent,
    double WallClockChangePercent,
    double EstimatedContextLoadSavingPercent,
    bool HasProviderTokenData,
    bool SameFinalState)
{
    public static Comparison Create(IReadOnlyList<RunResult> results, ScenarioSnapshot baseline)
    {
        var conventional = Summary.From(results.Where(x => x.Flow == FlowKind.Conventional.DisplayName()).ToArray());
        var foundgine = Summary.From(results.Where(x => x.Flow == FlowKind.Foundgine.DisplayName()).ToArray());
        var finalStates = results.Where(x => x.FinalState is not null).Select(x => NormalizeFinalState(x.FinalState!))
            .Distinct().ToArray();
        var same = finalStates.Length == 1 && finalStates[0]
            .Contains(BenchmarkConstants.ReviewedFullName.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant(),
                StringComparison.Ordinal);
        // Provider tokens are only meaningful in live mode. In replay mode InputTokens/TotalTokens
        // are always 0 by design (see ConventionalReplay/FoundgineReplay), so InputTokenSavingPercent
        // and TotalTokenSavingPercent below are NOT a real signal — check HasProviderTokenData before
        // presenting them, and prefer EstimatedContextLoadSavingPercent, which is always populated.
        var hasProviderTokenData = results.Any(x => x.TotalTokens > 0);
        return new(
            conventional,
            foundgine,
            PercentSaved(conventional.InputTokens, foundgine.InputTokens),
            PercentSaved(conventional.TotalTokens, foundgine.TotalTokens),
            PercentSaved(conventional.ToolCalls, foundgine.ToolCalls),
            PercentSaved(conventional.ModelCalls, foundgine.ModelCalls),
            PercentChange(conventional.WallClockMs, foundgine.WallClockMs),
            PercentSaved(conventional.EstimatedContextLoadTokens, foundgine.EstimatedContextLoadTokens),
            hasProviderTokenData,
            same);
    }

    private static string NormalizeFinalState(string value) =>
        value.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();

    private static double PercentSaved(double conventional, double foundgine) =>
        conventional == 0 ? 0 : (conventional - foundgine) / conventional * 100d;

    private static double PercentChange(double conventional, double foundgine) =>
        conventional == 0 ? 0 : (foundgine - conventional) / conventional * 100d;
}

public sealed record Summary(
    double WallClockMs,
    double ModelTimeMs,
    double ToolTimeMs,
    double ModelCalls,
    double ToolCalls,
    double InputTokens,
    double OutputTokens,
    double TotalTokens,
    double CachedInputTokens,
    double EstimatedToolInputTokens,
    double EstimatedToolOutputTokens,
    double EstimatedContextLoadTokens)
{
    public static Summary From(IReadOnlyList<RunResult> values)
    {
        if (values.Count == 0) return new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return new(
            values.Average(x => x.WallClockMs),
            values.Average(x => x.ModelTimeMs),
            values.Average(x => x.ToolTimeMs),
            values.Average(x => x.ModelCalls),
            values.Average(x => x.ToolCalls),
            values.Average(x => x.InputTokens),
            values.Average(x => x.OutputTokens),
            values.Average(x => x.TotalTokens),
            values.Average(x => x.CachedInputTokens),
            values.Average(x => x.EstimatedToolInputTokens),
            values.Average(x => x.EstimatedToolOutputTokens),
            values.Average(x => x.EstimatedContextLoadTokens));
    }
}

public static class ConventionalReplay
{
    public static async Task RunAsync(string connectionString, TraceCollector trace, int customerId)
    {
        var tools = ConventionalTools.Create(connectionString, trace, customerId);
        await tools.InvokeAsync("describe_schema", "{}", trace);
        await tools.InvokeAsync("find_customer", JsonSerializer.Serialize(new { customerId }), trace);
        await tools.InvokeAsync("list_relationships", JsonSerializer.Serialize(new { customerId }), trace);
        await tools.InvokeAsync("list_contracts", JsonSerializer.Serialize(new { customerId }), trace);
        await tools.InvokeAsync("list_transactions", JsonSerializer.Serialize(new { customerId }), trace);
        await tools.InvokeAsync("update_customer",
            JsonSerializer.Serialize(new { customerId, fullName = BenchmarkConstants.ReviewedName(customerId) }),
            trace);
        await tools.InvokeAsync("verify_customer", JsonSerializer.Serialize(new { customerId }), trace);
        // Replay intentionally has no model token counts. It is a correctness/trace harness only.
    }
}

public static class FoundgineReplay
{
    public static async Task RunAsync(string foundgineUrl, TraceCollector trace, int customerId)
    {
        var tools = FoundgineTools.Create(foundgineUrl, trace, null, customerId);
        await tools.InvokeAsync("foundgine_capability", "{}", trace);
        await tools.InvokeAsync("foundgine_graph", "{}", trace);
        await tools.InvokeAsync("foundgine_update_customer",
            JsonSerializer.Serialize(new
                { customerKey = (string?)null, fullName = BenchmarkConstants.ReviewedName(customerId) }), trace);
        await tools.InvokeAsync("foundgine_verify", "{}", trace);
    }
}

public sealed class ToolRegistry
{
    private readonly Dictionary<string, Func<string, Task<string>>> _handlers;
    public ToolRegistry(Dictionary<string, Func<string, Task<string>>> handlers) => _handlers = handlers;

    public Task<string> InvokeAsync(string name, string input, TraceCollector trace)
    {
        if (!_handlers.TryGetValue(name, out var handler))
            throw new InvalidOperationException($"Unknown tool '{name}'.");
        return InvokeMeasuredAsync(name, input, handler, trace);
    }

    private static async Task<string> InvokeMeasuredAsync(string name, string input, Func<string, Task<string>> handler,
        TraceCollector trace)
    {
        var sw = Stopwatch.StartNew();
        var output = await handler(input);
        sw.Stop();
        trace.ToolCall(name, input, output, sw.Elapsed.TotalMilliseconds);
        return output;
    }
}

public static class ConventionalTools
{
    public static ToolRegistry Create(string connectionString, TraceCollector trace, int customerId) => new(new()
    {
        ["describe_schema"] = _ =>
            Task.FromResult(
                "Customer(Id, CustomerKey, FirstName, LastName, FullName) -> CustomerBankingRelationship(Id, CustomerId) -> Contract(Id, CustomerBankingRelationshipId, ContractType, Amount) -> Transaction(Id, ContractId, Amount, Balance). Customer 1 is the benchmark target."),
        ["find_customer"] = async input => await QueryAsync(connectionString,
            "SELECT \"Id\", \"CustomerKey\", \"FullName\" FROM \"Banking\".\"Customer\" WHERE \"Id\"=@id", input,
            "customer"),
        ["list_relationships"] = async input => await QueryAsync(connectionString,
            "SELECT \"Id\" FROM \"Banking\".\"CustomerBankingRelationship\" WHERE \"CustomerId\"=@id ORDER BY \"Id\"",
            input, "relationships"),
        ["list_contracts"] = async input => await QueryAsync(connectionString,
            "SELECT ct.\"Id\", ct.\"Amount\" FROM \"Lending\".\"Contract\" ct JOIN \"Banking\".\"CustomerBankingRelationship\" r ON r.\"Id\"=ct.\"CustomerBankingRelationshipId\" WHERE r.\"CustomerId\"=@id ORDER BY ct.\"Id\"",
            input, "contracts"),
        ["list_transactions"] = async input => await QueryAsync(connectionString,
            "SELECT t.\"Id\", t.\"Amount\", t.\"Balance\" FROM \"Lending\".\"Transaction\" t JOIN \"Lending\".\"Contract\" ct ON ct.\"Id\"=t.\"ContractId\" JOIN \"Banking\".\"CustomerBankingRelationship\" r ON r.\"Id\"=ct.\"CustomerBankingRelationshipId\" WHERE r.\"CustomerId\"=@id ORDER BY t.\"Id\"",
            input, "transactions"),
        ["update_customer"] = async input => await UpdateCustomerAsync(connectionString, input),
        ["verify_customer"] = async input => await VerifyAsync(connectionString, customerId),
    });

    private static async Task<string> QueryAsync(string cs, string sql, string input, string kind)
    {
        var parsed = JsonNode.Parse(input);
        var id = parsed?["customerId"]?.GetValue<int>() ?? 1;
        await using var db = new NpgsqlConnection(cs);
        await db.OpenAsync();
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

    private static async Task<string> UpdateCustomerAsync(string cs, string input)
    {
        var node = JsonNode.Parse(input) ?? throw new InvalidOperationException("Invalid update input.");
        await using var db = new NpgsqlConnection(cs);
        await db.OpenAsync();
        await using var command = db.CreateCommand();
        command.CommandText =
            "UPDATE \"Banking\".\"Customer\" SET \"FullName\"=@name WHERE \"Id\"=@id RETURNING \"Id\", \"CustomerKey\", \"FullName\";";
        command.Parameters.AddWithValue("id", node["customerId"]!.GetValue<int>());
        command.Parameters.AddWithValue("name", node["fullName"]!.GetValue<string>());
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Customer update returned no row.");
        return JsonSerializer.Serialize(new
            { id = reader.GetInt32(0), customerKey = reader.GetGuid(1), fullName = reader.GetString(2) });
    }

    private static async Task<string> VerifyAsync(string cs, int customerId)
    {
        await using var db = new NpgsqlConnection(cs);
        await db.OpenAsync();
        return JsonSerializer.Serialize(await ScenarioSnapshot.ReadAsync(db, customerId));
    }
}

public static class BenchmarkHttp
{
    private static readonly SocketsHttpHandler Handler = new()
    {
        MaxConnectionsPerServer = 128,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(10)
    };

    public static readonly HttpClient Client = new(Handler) { Timeout = TimeSpan.FromSeconds(60) };
    private static long _retries;
    public static long Retries => Interlocked.Read(ref _retries);
    public static void Reset() => Interlocked.Exchange(ref _retries, 0);

    public static async Task<HttpResponseMessage> PostAsync(string url, HttpContent content,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 4;
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var requestContent = new ByteArrayContent(bytes);
                requestContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return await Client.PostAsync(url, requestContent, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                Interlocked.Increment(ref _retries);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }

        throw new InvalidOperationException("HTTP retry loop exhausted unexpectedly.");
    }
}

public static class FoundgineTools
{
    public static ToolRegistry Create(string url, TraceCollector trace, string? customerKey, int customerId) => new(
        new()
        {
            ["foundgine_capability"] = _ =>
                Task.FromResult(
                    "Capability: Customer graph review. Allowed semantic path: Customer -> CustomerBankingRelationship -> Contract -> Transaction. Allowed mutation: Customer.FullName. Provider details are intentionally hidden from the agent."),
            ["foundgine_graph"] = _ => GraphAsync(url, customerId),
            ["foundgine_update_customer"] = input => MutationAsync(url, input, customerId),
            ["foundgine_verify"] = _ => GraphAsync(url, customerId),
        });

    private static async Task<string> GraphAsync(string url, int customerId)
    {
        var query =
            "query ReviewCustomer { " +
            "customer(where: { id: { eq: " + customerId + " } }, first: 1) { " +
            "id customerKey firstName lastName fullName " +
            "customerBankingRelationship { id customerBankingRelationshipKey " +
            "contract { id contractKey amount transaction { id transactionKey amount balance } } " +
            "} } }";
        return await PostGraphqlAsync(url, query, new { });
    }

    private static async Task<string> MutationAsync(string url, string input, int customerId)
    {
        var node = JsonNode.Parse(input) ?? throw new InvalidOperationException("Invalid mutation input.");
        var fullName = node["fullName"]?.GetValue<string>()
                       ?? throw new InvalidOperationException("Mutation input must contain fullName.");

        // Keep the benchmark deterministic and avoid crossing the GraphQL
        // variable/value JSON boundary for the integer identity filter.
        // The same semantic mutation is still produced: Customer.Id == 1 and
        // only FullName is changed. This also makes a runtime parameter-type
        // mismatch visible in the benchmark only if the literal GraphQL path
        // itself is broken.
        var escapedFullName = JsonSerializer.Serialize(fullName);
        var query =
            "mutation ReviewCustomer { " +
            "updateCustomer(" +
            "input: { fullName: " + escapedFullName + " }, " +
            "where: { id: { eq: " + customerId + " } }" +
            ") { id customerKey firstName lastName fullName }" +
            " }";

        return await PostGraphqlAsync(url, query, new { });
    }

    private static async Task<string> PostGraphqlAsync(string url, string query, object variables)
    {
        var body = JsonSerializer.Serialize(new { query, variables });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await BenchmarkHttp.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GraphQL request failed with {(int)response.StatusCode} ({response.StatusCode}). " +
                $"Response: {responseBody}");
        }

        return responseBody;
    }
}

public sealed class OpenAiCompatibleAgentClient
{
    private readonly HttpClient _http = new();
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;

    public OpenAiCompatibleAgentClient(string endpoint, string apiKey, string model)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _model = model;
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task RunAsync(string systemPrompt, string request, ToolRegistry tools, TraceCollector trace)
    {
        var definitions = BuildDefinitions(tools, trace.Flow);
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
            var choice = document.RootElement.GetProperty("choices")[0];
            var message = choice.GetProperty("message");
            var usage = ParseUsage(document.RootElement);
            trace.ModelCall($"iteration={iteration + 1}", usage, sw.Elapsed.TotalMilliseconds);
            messages.Add(JsonNode.Parse(message.GetRawText())!);

            if (!message.TryGetProperty("tool_calls", out var calls) || calls.GetArrayLength() == 0)
            {
                trace.ToolCall("final.state", "{}",
                    message.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "", 0);
                return;
            }

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

        throw new InvalidOperationException("Agent exceeded the maximum of 16 iterations.");
    }

    private static JsonArray BuildDefinitions(ToolRegistry tools, FlowKind flow)
    {
        return flow == FlowKind.Conventional
            ? new JsonArray(
                Tool("describe_schema", "Inspect the physical customer graph schema before using application tools.",
                    "{\"type\":\"object\",\"properties\":{}}"),
                Tool("find_customer", "Find one customer by physical integer id.",
                    "{\"type\":\"object\",\"properties\":{\"customerId\":{\"type\":\"integer\"}},\"required\":[\"customerId\"]}"),
                Tool("list_relationships", "List banking relationship rows for a customer.",
                    "{\"type\":\"object\",\"properties\":{\"customerId\":{\"type\":\"integer\"}},\"required\":[\"customerId\"]}"),
                Tool("list_contracts", "List lending contracts for a customer.",
                    "{\"type\":\"object\",\"properties\":{\"customerId\":{\"type\":\"integer\"}},\"required\":[\"customerId\"]}"),
                Tool("list_transactions", "List all transaction balances for a customer.",
                    "{\"type\":\"object\",\"properties\":{\"customerId\":{\"type\":\"integer\"}},\"required\":[\"customerId\"]}"),
                Tool("update_customer", "Update a customer's full name.",
                    "{\"type\":\"object\",\"properties\":{\"customerId\":{\"type\":\"integer\"},\"fullName\":{\"type\":\"string\"}},\"required\":[\"customerId\",\"fullName\"]}"),
                Tool("verify_customer", "Verify the final customer state and graph counts.",
                    "{\"type\":\"object\",\"properties\":{\"customerId\":{\"type\":\"integer\"}},\"required\":[\"customerId\"]}"))
            : new JsonArray(
                Tool("foundgine_capability",
                    "Describe the current semantic capability. Physical tables, joins and SQL are not exposed.",
                    "{\"type\":\"object\",\"properties\":{}}"),
                Tool("foundgine_graph",
                    "Execute the authorized Customer exposure graph and return the business-shaped result.",
                    "{\"type\":\"object\",\"properties\":{}}"),
                Tool("foundgine_update_customer", "Apply the authorized Customer review mutation.",
                    "{\"type\":\"object\",\"properties\":{\"fullName\":{\"type\":\"string\"}},\"required\":[\"fullName\"]}"),
                Tool("foundgine_verify", "Verify the authorized Customer exposure graph after the mutation.",
                    "{\"type\":\"object\",\"properties\":{}}"));

        static JsonObject Tool(string name, string description, string parameters) => new()
        {
            ["type"] = "function",
            ["function"] = new JsonObject
                { ["name"] = name, ["description"] = description, ["parameters"] = JsonNode.Parse(parameters) }
        };
    }

    private static ModelUsage ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage)) return new(0, 0, 0);
        var input = ReadLong(usage, "prompt_tokens", "input_tokens");
        var output = ReadLong(usage, "completion_tokens", "output_tokens");
        var total = ReadLong(usage, "total_tokens");
        if (total == 0) total = input + output;
        var cached = 0L;
        if (usage.TryGetProperty("prompt_tokens_details", out var details)) cached = ReadLong(details, "cached_tokens");
        if (cached == 0 && usage.TryGetProperty("input_tokens_details", out var inputDetails))
            cached = ReadLong(inputDetails, "cached_tokens");
        return new(input, output, total, cached);
    }

    private static long ReadLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number))
                return number;
        return 0;
    }
}