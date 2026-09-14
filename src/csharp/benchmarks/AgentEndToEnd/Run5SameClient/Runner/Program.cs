using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;


// Run5SameClient is deliberately rebased on the WORKING Run5 client.
// Do not introduce ModelContextProtocol.Client here: Run5 already has a
// proven MCP-over-HTTP client that works against both servers. The experiment
// changes only what that identical client asks each endpoint to execute.
var runs = GetInt("RUN5_RUNS", 30);
var warmups = GetInt("RUN5_WARMUPS", 5);
var concurrency = GetInt("RUN5_CONCURRENCY", 8);
var customers = GetInt("RUN5_CUSTOMER_COUNT", 10);
var batchSize = GetInt("RUN5_BATCH_SIZE", 8);
var efcoreMcp = Environment.GetEnvironmentVariable("RUN5_EFCORE_MCP_URL") ?? "http://localhost:4411/mcp";
var mcp = Environment.GetEnvironmentVariable("RUN5_MCP_URL") ?? "http://localhost:4412/mcp";
var reportDirectory = Environment.GetEnvironmentVariable("RUN5_REPORT_DIRECTORY");

using var http = new HttpClient(new SocketsHttpHandler
{
    MaxConnectionsPerServer = Math.Max(128, concurrency * 2),
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
}) { Timeout = TimeSpan.FromSeconds(60) };

await WaitFor(http, efcoreMcp.Replace("/mcp", "/health/ready"));
await WaitFor(http, mcp.Replace("/mcp", "/health/ready"));

Console.WriteLine(
    $"Run 5 Same Client (rebased on working Run5): customers={customers} concurrency={concurrency} runs={runs} warmups={warmups} batchSize={batchSize}");
Console.WriteLine("Client: identical HttpClient + JSON-RPC/MCP transport for both endpoints");

// Conventional side: the SAME client sends batchSize individual MCP calls.
var conventional = await Measure("MCP + EF Core (same client, individual calls)", runs, warmups, concurrency, customers,
    async customer =>
    {
        var totalInput = 0;
        var totalOutput = 0;
        var calls = 0;
        for (var i = 0; i < batchSize; i++)
        {
            var logicalCustomer = ((customer - 1 + i) % customers) + 1;
            var request = BuildMcpRequest(logicalCustomer, $"run5-same-client-ef-{customer}-{i}");
            var body = await PostMcp(http, efcoreMcp, request);
            totalInput += request.Length;
            totalOutput += body.Length;
            calls++;
        }

        return new OperationResult(totalInput, totalOutput, calls, batchSize);
    });

// Foundgine side: the SAME client sends one MCP batch tool call containing
// exactly the same number of logical transfers.
var foundgine = await Measure("MCP + Foundgine (same client, semantic batch)", runs, warmups, concurrency, customers,
    async customer =>
    {
        var request = BuildMcpBatchRequest(customer, customers, batchSize);
        var body = await PostMcp(http, mcp, request);
        return new OperationResult(request.Length, body.Length, 1, batchSize);
    });

Print(conventional);
Print(foundgine);

Console.WriteLine();
Console.WriteLine(
    $"Tool/MCP calls per task: EF Core={conventional.AverageCallsPerTask:F2}; Foundgine={foundgine.AverageCallsPerTask:F2}");
Console.WriteLine(
    $"Logical operations per task: EF Core={conventional.AverageLogicalOpsPerTask:F2}; Foundgine={foundgine.AverageLogicalOpsPerTask:F2}");
Console.WriteLine(
    $"Call reduction: {(1 - foundgine.AverageCallsPerTask / conventional.AverageCallsPerTask) * 100:F1}%");
var averageInputPayloadPerCallChange =
    conventional.AverageInputBytesPerCall == 0
        ? 0
        : (foundgine.AverageInputBytesPerCall / conventional.AverageInputBytesPerCall - 1) * 100;

var totalTaskPayloadChange =
    conventional.AverageTotalPayloadBytes == 0
        ? 0
        : (foundgine.AverageTotalPayloadBytes / conventional.AverageTotalPayloadBytes - 1) * 100;

var conventionalPayloadPerLogicalOp =
    conventional.AverageLogicalOpsPerTask == 0
        ? 0
        : conventional.AverageTotalPayloadBytes / conventional.AverageLogicalOpsPerTask;

var foundginePayloadPerLogicalOp =
    foundgine.AverageLogicalOpsPerTask == 0
        ? 0
        : foundgine.AverageTotalPayloadBytes / foundgine.AverageLogicalOpsPerTask;

Console.WriteLine(
    $"Average input payload per MCP call change: {averageInputPayloadPerCallChange:+0.0;-0.0;0.0}% (Foundgine batch calls are intentionally larger)");
Console.WriteLine(
    $"Total task payload: EF Core={conventional.AverageTotalPayloadBytes:F0} bytes; Foundgine={foundgine.AverageTotalPayloadBytes:F0} bytes");
Console.WriteLine($"Total task payload change: {totalTaskPayloadChange:+0.0;-0.0;0.0}% (not a reduction metric)");
Console.WriteLine(
    $"Payload per logical operation: EF Core={conventionalPayloadPerLogicalOp:F0} bytes; Foundgine={foundginePayloadPerLogicalOp:F0} bytes");
Console.WriteLine(
    $"Payload per logical operation change: {(conventionalPayloadPerLogicalOp == 0 ? 0 : (foundginePayloadPerLogicalOp / conventionalPayloadPerLogicalOp - 1) * 100):+0.0;-0.0;0.0}%");

if (!string.IsNullOrWhiteSpace(reportDirectory))
{
    Directory.CreateDirectory(reportDirectory);
    var report = new
    {
        schemaVersion = 2,
        utc = DateTimeOffset.UtcNow,
        runs,
        warmups,
        customers,
        concurrency,
        batchSize,
        client = "Original Run5 HttpClient + manual JSON-RPC MCP transport, unchanged",
        scenario =
            "Same client performs the same logical transfer task against conventional EF Core and Foundgine; only execution capability differs.",
        conventional = "batchSize individual transfer_funds MCP calls",
        foundgine = "one transfer_funds_batch MCP call containing batchSize logical transfers",
        correctness =
            "Each task contains the same number of logical transfers; final-state correctness must be verified separately by the fixture/DB contract.",
        samples = new[] { conventional, foundgine }.SelectMany(x => x.Samples.Select(r => new
        {
            implementation = x.Name,
            r.Success,
            r.WallMs,
            r.InputBytes,
            r.OutputBytes,
            r.ToolCalls,
            r.LogicalOps,
            r.TotalPayloadBytes,
            error = r.Error
        })).ToArray()
    };
    await File.WriteAllTextAsync(Path.Combine(reportDirectory, "run5-same-client-metadata.json"),
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

static async Task<ConcurrentStats> Measure(string name, int runs, int warmups, int concurrency, int customers,
    Func<int, Task<OperationResult>> action)
{
    for (var i = 0; i < warmups; i++)
    {
        var warmup = await Batch(concurrency, customers, action);
        if (warmup.Any(x => !x.Success))
            throw new InvalidOperationException(
                $"Warmup failed for {name}: {string.Join(" | ", warmup.Where(x => !x.Success).Select(x => x.Error))}");
    }

    var summaries = new List<RunSummary>();
    var samples = new List<Sample>();
    for (var run = 1; run <= runs; run++)
    {
        var batch = await Batch(concurrency, customers, action);
        samples.AddRange(batch);
        var ok = batch.Where(x => x.Success).ToArray();
        var lat = ok.Select(x => x.WallMs).ToArray();
        var max = lat.Length == 0 ? 0 : lat.Max();
        var avg = lat.Length == 0 ? 0 : lat.Average();
        var rps = max > 0 ? ok.Sum(x => x.LogicalOps) / (max / 1000) : 0;
        summaries.Add(new RunSummary(run, rps, avg, P(lat, .5), P(lat, .95), P(lat, .99), max,
            ok.Length, batch.Length - ok.Length,
            ok.Sum(x => x.ToolCalls), ok.Sum(x => x.LogicalOps),
            ok.Sum(x => x.InputBytes), ok.Sum(x => x.OutputBytes)));
        Console.WriteLine(
            $"{name} run={run}: tasks={ok.Length} failed={batch.Length - ok.Length} logicalOps={ok.Sum(x => x.LogicalOps)} toolCalls={ok.Sum(x => x.ToolCalls)} rps={rps:F1} avg={avg:F1}ms p50={P(lat, .5):F1}ms p95={P(lat, .95):F1}ms p99={P(lat, .99):F1}ms payload={ok.Sum(x => x.InputBytes + x.OutputBytes)}B");
    }

    return new ConcurrentStats(name, samples, summaries);
}

static async Task<Sample[]> Batch(int concurrency, int customers, Func<int, Task<OperationResult>> action) =>
    await Task.WhenAll(Enumerable.Range(0, concurrency).Select(i => One((i % customers) + 1, action)));

static async Task<Sample> One(int customer, Func<int, Task<OperationResult>> action)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var r = await action(customer);
        sw.Stop();
        return new(true, sw.Elapsed.TotalMilliseconds, r.InputBytes, r.OutputBytes, r.ToolCalls, r.LogicalOps, null);
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new(false, sw.Elapsed.TotalMilliseconds, 0, 0, 0, 0, ex.Message);
    }
}

static void Print(ConcurrentStats s) => Console.WriteLine(
    $"{s.Name}: avg={s.Average:F1}ms p50={P(s.Samples.Where(x => x.Success).Select(x => x.WallMs).ToArray(), .5):F1}ms p95={P(s.Samples.Where(x => x.Success).Select(x => x.WallMs).ToArray(), .95):F1}ms p99={P(s.Samples.Where(x => x.Success).Select(x => x.WallMs).ToArray(), .99):F1}ms avgRps={s.AverageRps:F1}");

static string BuildMcpRequest(int customer, string keyPrefix)
{
    var (source, destination) = Accounts(customer);
    var key = $"{keyPrefix}-{customer}-{Guid.NewGuid():N}";
    return JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0", id = Guid.NewGuid().ToString("N"), method = "tools/call",
        @params = new
        {
            name = "transfer_funds",
            arguments = new
            {
                actorId = Actor(), tenantId = 1, sourceAccountId = source, destinationAccountId = destination,
                amount = 1m, idempotencyKey = key
            }
        }
    });
}

static string BuildMcpBatchRequest(int customer, int customers, int batchSize)
{
    // Preserve the exact logical transfer set used by the conventional client,
    // but acquire accounts in one canonical order. The previous cyclic rotation
    // could produce lock-order inversion at PostgreSQL when concurrent semantic
    // batches overlapped (for example 1..8, 2..9, ..., 8..10,1..5).
    //
    // Canonical ordering keeps the benchmark's overlap/contention while removing
    // an artificial deadlock caused solely by different transactions acquiring
    // the same accounts in different orders.
    var logicalCustomers = Enumerable.Range(0, batchSize)
        .Select(i => ((customer - 1 + i) % customers) + 1)
        .OrderBy(i => i)
        .ToArray();

    var transfers = logicalCustomers.Select((logicalCustomer, index) =>
    {
        var (source, destination) = Accounts(logicalCustomer);
        return new
        {
            sourceAccountId = source,
            destinationAccountId = destination,
            amount = 1m,
            idempotencyKey = $"run5-batch-{customer}-{index}-{Guid.NewGuid():N}"
        };
    }).ToArray();

    return JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0",
        id = Guid.NewGuid().ToString("N"),
        method = "tools/call",
        @params = new
        {
            name = "transfer_funds_batch",
            arguments = new { actorId = Actor(), tenantId = 1, transfers }
        }
    });
}

static async Task<string> PostMcp(HttpClient http, string url, string request)
{
    // HttpRequestMessage is single-use. The retry path must create a NEW
    // request for every attempt; re-sending the same message throws:
    // "The request message was already sent."
    using var r = await Send(http, async () =>
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(request, Encoding.UTF8, "application/json")
        };
        msg.Headers.Accept.ParseAdd("application/json, text/event-stream");
        msg.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
        return await http.SendAsync(msg);
    });
    var b = await r.Content.ReadAsStringAsync();
    if (!r.IsSuccessStatusCode)
        throw new HttpRequestException($"MCP HTTP {(int)r.StatusCode} {r.ReasonPhrase}: {b}");

    foreach (var json in ExtractJsonResponses(b))
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new HttpRequestException($"MCP JSON-RPC error: {error}");
            if (root.TryGetProperty("result", out var result) &&
                result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
                throw new HttpRequestException($"MCP tool error: {result}");
        }
        catch (JsonException)
        {
            // Ignore non-JSON transport framing; the HTTP status is authoritative for transport failures.
        }
    }

    return b;
}

static IEnumerable<string> ExtractJsonResponses(string body)
{
    if (string.IsNullOrWhiteSpace(body)) yield break;
    if (body.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var line in body.Split('\n'))
        {
            var value = line.Trim();
            if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var data = value[5..].Trim();
                if (!string.IsNullOrWhiteSpace(data) && data != "[DONE]") yield return data;
            }
        }
    }
    else
    {
        yield return body;
    }
}

static async Task<HttpResponseMessage> Send(HttpClient http, Func<Task<HttpResponseMessage>> send)
{
    Exception? last = null;
    for (var i = 1; i <= 4; i++)
        try
        {
            return await send();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && i < 4)
        {
            last = ex;
            await Task.Delay(100 * (1 << (i - 1)));
        }

    throw last ?? new HttpRequestException();
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

    throw new InvalidOperationException($"Endpoint not ready: {url}");
}

static (Guid, Guid) Accounts(int customer) =>
    (GuidFrom($"run5:{customer}:source"), GuidFrom($"run5:{customer}:destination"));

static Guid Actor() => Guid.Parse("11111111-1111-1111-1111-111111111111");

static Guid GuidFrom(string s)
{
    var b = SHA256.HashData(Encoding.UTF8.GetBytes(s));
    return new Guid(b[..16]);
}

static int GetInt(string n, int d) => int.TryParse(Environment.GetEnvironmentVariable(n), out var v) ? v : d;

static double P(double[] v, double p)
{
    if (v.Length == 0) return 0;
    var x = v.OrderBy(z => z).ToArray();
    var i = (x.Length - 1) * p;
    var lo = (int)Math.Floor(i);
    var hi = (int)Math.Ceiling(i);
    return lo == hi ? x[lo] : x[lo] + (x[hi] - x[lo]) * (i - lo);
}

record OperationResult(int InputBytes, int OutputBytes, int ToolCalls, int LogicalOps);

record Sample(
    bool Success,
    double WallMs,
    int InputBytes,
    int OutputBytes,
    int ToolCalls,
    int LogicalOps,
    string? Error)
{
    public int TotalPayloadBytes => InputBytes + OutputBytes;
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
    int LogicalOps,
    int InputBytes,
    int OutputBytes)
{
    public int TotalPayloadBytes => InputBytes + OutputBytes;
}

record ConcurrentStats(string Name, List<Sample> Samples, List<RunSummary> Runs)
{
    public double Average => Samples.Where(x => x.Success).Select(x => x.WallMs).DefaultIfEmpty().Average();
    public double AverageRps => Runs.Average(x => x.Rps);

    public double AverageCallsPerTask =>
        Samples.Where(x => x.Success).Select(x => (double)x.ToolCalls).DefaultIfEmpty().Average();

    public double AverageLogicalOpsPerTask =>
        Samples.Where(x => x.Success).Select(x => (double)x.LogicalOps).DefaultIfEmpty().Average();

    public double AverageTotalPayloadBytes => Samples.Where(x => x.Success).Select(x => (double)x.TotalPayloadBytes)
        .DefaultIfEmpty().Average();

    public double AverageInputBytesPerCall => Samples.Where(x => x.Success)
        .Select(x => x.ToolCalls == 0 ? 0 : (double)x.InputBytes / x.ToolCalls).DefaultIfEmpty().Average();
}