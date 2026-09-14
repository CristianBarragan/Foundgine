using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

var runs = GetInt("RUN5_RUNS", 30);
var warmups = GetInt("RUN5_WARMUPS", 5);
var concurrency = GetInt("RUN5_CONCURRENCY", 8);
var customers = GetInt("RUN5_CUSTOMER_COUNT", 10);
var batchSize = GetInt("RUN5_BATCH_SIZE", 64);
// MCP.EfCore only exposes the MCP transport (/mcp) - there is no REST
// /api/transfer-funds endpoint on that service, so this must be treated as
// an MCP URL, not a WebApi base URL.
var efcoreMcp = Environment.GetEnvironmentVariable("RUN5_EFCORE_MCP_URL") ?? "http://localhost:4411/mcp";
var mcp = Environment.GetEnvironmentVariable("RUN5_MCP_URL") ?? "http://localhost:4412/mcp";
var reportDirectory = Environment.GetEnvironmentVariable("RUN5_REPORT_DIRECTORY");
using var http = new HttpClient(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = Math.Max(128, concurrency * 2), PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    { Timeout = TimeSpan.FromSeconds(60) };
await WaitFor(http, efcoreMcp.Replace("/mcp", "/health/ready"));
await WaitFor(http, mcp.Replace("/mcp", "/health/ready"));

// EF Core has no batch tool, so it is measured at its single-transfer best case.
// Foundgine is measured at ITS best case too: the UNNEST batch path, not the
// single-transfer path, since that is the fastest Foundgine can go.
var stats = new List<ConcurrentStats>();
stats.Add(await Measure("MCP + EF Core Postgres", runs, warmups, concurrency, customers, async id =>
{
    var request = BuildMcpRequest(id);
    var body = await PostMcp(http, efcoreMcp, request);
    return (request.Length, body.Length);
}));
var batched = await MeasureBatch("MCP + Foundgine Postgres (UNNEST batch)", runs, warmups, concurrency, customers,
    batchSize, async id =>
    {
        var request = BuildMcpBatchRequest(id, customers, batchSize);
        var body = await PostMcp(http, mcp, request);
        return (request.Length, body.Length);
    });

foreach (var s in stats) Print(s);
Print(batched);
var conventional = stats[0];
var foundgineOpLatencyMs = batched.Average / batchSize; // amortized per-transfer latency inside a batch
Console.WriteLine(
    $"Throughput delta (Foundgine UNNEST batch vs EF Core, both MCP): {(batched.AverageRps / conventional.AverageRps - 1) * 100:F1}%");
Console.WriteLine(
    $"Effective per-operation latency delta (Foundgine UNNEST batch, amortized, vs EF Core single-transfer): {(foundgineOpLatencyMs / conventional.Average - 1) * 100:F1}%");

if (!string.IsNullOrWhiteSpace(reportDirectory))
{
    Directory.CreateDirectory(reportDirectory);
    var report = new
    {
        schemaVersion = 1, utc = DateTimeOffset.UtcNow, runs, warmups, customers, concurrency, batchSize,
        scenario =
            "High-assurance transferFunds: one authenticated actor, one tenant, deterministic account pair, unique idempotency key per operation",
        conventional = "MCP + EF Core Postgres (single transfer, EF Core has no batch tool)",
        foundgine = "MCP + Foundgine HighAssurance Postgres (UNNEST batch — fastest available Foundgine path)",
        semantics =
            "Same TransferFundsCommand, authorization rule, tenant/frozen/available-funds/daily-limit checks, idempotency, audit, atomic transaction",
        samples = stats.Append(batched).SelectMany(x => x.Runs.Select(r => new
        {
            implementation = x.Name, r.Run, r.Rps, r.AvgWallMs, r.P50Ms, r.P95Ms, r.P99Ms, r.MaxWallMs, r.Success,
            r.Failed, r.ToolCalls, r.EstimatedInputTokens, r.EstimatedOutputTokens
        })).ToArray()
    };
    await File.WriteAllTextAsync(Path.Combine(reportDirectory, "run5-metadata.json"),
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}

static string BuildMcpRequest(int customer)
{
    var (source, destination) = Accounts(customer);
    var key = $"run5-{customer}-{Guid.NewGuid():N}";
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
    var transfers = Enumerable.Range(0, batchSize).Select(i =>
    {
        var logicalCustomer = ((customer - 1 + i) % customers) + 1;
        var (source, destination) = Accounts(logicalCustomer);
        return new
        {
            sourceAccountId = source, destinationAccountId = destination, amount = 1m,
            idempotencyKey = $"run5-batch-{customer}-{i}-{Guid.NewGuid():N}"
        };
    }).ToArray();
    return JsonSerializer.Serialize(new
    {
        jsonrpc = "2.0", id = Guid.NewGuid().ToString("N"), method = "tools/call",
        @params = new { name = "transfer_funds_batch", arguments = new { actorId = Actor(), tenantId = 1, transfers } }
    });
}

static async Task<ConcurrentStats> Measure(string name, int runs, int warmups, int concurrency, int customers,
    Func<int, Task<(int, int)>> action)
{
    for (var i = 0; i < warmups; i++)
    {
        var w = await Batch(concurrency, customers, action);
        if (w.Any(x => !x.Success))
        {
            var failures = string.Join(" | ", w.Where(x => !x.Success).Select((x, i) => $"#{i + 1}: {x.Error}"));
            throw new InvalidOperationException($"Warmup failed for {name}. {failures}");
        }
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
        var rps = max > 0 ? ok.Length / (max / 1000) : 0;
        summaries.Add(new RunSummary(run, rps, avg, P(lat, .5), P(lat, .95), P(lat, .99), max, ok.Length,
            batch.Length - ok.Length, 1, ok.Length == 0 ? 0 : ok.Average(x => x.InputChars / 4.0),
            ok.Length == 0 ? 0 : ok.Average(x => x.OutputChars / 4.0)));
        Console.WriteLine(
            $"{name} run={run}: success={ok.Length} failed={batch.Length - ok.Length} rps={rps:F1} avg={avg:F1}ms p50={P(lat, .5):F1}ms p95={P(lat, .95):F1}ms p99={P(lat, .99):F1}ms");
    }

    return new ConcurrentStats(name, samples, summaries);
}

static async Task<ConcurrentStats> MeasureBatch(string name, int runs, int warmups, int concurrency, int customers,
    int batchSize, Func<int, Task<(int, int)>> action)
{
    for (var i = 0; i < warmups; i++)
    {
        var w = await Batch(concurrency, customers, action);
        if (w.Any(x => !x.Success))
            throw new InvalidOperationException(
                $"Warmup failed for {name}: {string.Join(" | ", w.Where(x => !x.Success).Select(x => x.Error))}");
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
        var requestRps = max > 0 ? ok.Length / (max / 1000) : 0;
        var rps = requestRps * batchSize;
        summaries.Add(new RunSummary(run, rps, avg, P(lat, .5), P(lat, .95), P(lat, .99), max, ok.Length * batchSize,
            batch.Length == ok.Length ? 0 : (batch.Length - ok.Length) * batchSize, 1,
            ok.Length == 0 ? 0 : ok.Average(x => x.InputChars / 4.0),
            ok.Length == 0 ? 0 : ok.Average(x => x.OutputChars / 4.0)));
        Console.WriteLine(
            $"{name} run={run}: batches={ok.Length} failed={batch.Length - ok.Length} operations={ok.Length * batchSize} ops/s={rps:F1} batchAvg={avg:F1}ms p50={P(lat, .5):F1}ms p95={P(lat, .95):F1}ms p99={P(lat, .99):F1}ms");
    }

    return new ConcurrentStats(name, samples, summaries);
}

static async Task<Sample[]> Batch(int concurrency, int customers, Func<int, Task<(int, int)>> action) =>
    await Task.WhenAll(Enumerable.Range(0, concurrency).Select(i => One((i % customers) + 1, action)));

static async Task<Sample> One(int customer, Func<int, Task<(int, int)>> action)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var r = await action(customer);
        sw.Stop();
        return new(true, sw.Elapsed.TotalMilliseconds, r.Item1, r.Item2, null);
    }
    catch (Exception ex)
    {
        sw.Stop();
        return new(false, sw.Elapsed.TotalMilliseconds, 0, 0, ex.Message);
    }
}

static async Task<string> PostMcp(HttpClient http, string url, string request)
{
    // HttpRequestMessage can only be sent once - build a fresh instance on
    // every attempt. Previously a single message was captured in the retry
    // closure, so a transient failure that triggered a second attempt threw
    // "The request message was already sent" instead of actually retrying.
    HttpRequestMessage BuildMessage()
    {
        var m = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(request, Encoding.UTF8, "application/json")
        };
        m.Headers.Accept.ParseAdd("application/json, text/event-stream");
        m.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
        return m;
    }

    using var r = await Send(http, async () =>
    {
        using var msg = BuildMessage();
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

static void Print(ConcurrentStats s)
{
    Console.WriteLine(
        $"{s.Name}: avg={s.Average:F1}ms p50={P(s.SuccessfulLatencies, .5):F1}ms p95={P(s.SuccessfulLatencies, .95):F1}ms p99={P(s.SuccessfulLatencies, .99):F1}ms avgRps={s.AverageRps:F1}");
}

record Sample(bool Success, double WallMs, int InputChars, int OutputChars, string? Error);

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
    double EstimatedOutputTokens);

record ConcurrentStats(string Name, List<Sample> Samples, List<RunSummary> Runs)
{
    public double Average => Samples.Where(x => x.Success).Select(x => x.WallMs).DefaultIfEmpty().Average();
    public double AverageRps => Runs.Average(x => x.Rps);
    public double[] SuccessfulLatencies => Samples.Where(x => x.Success).Select(x => x.WallMs).ToArray();

    public double AvgInputChars =>
        Samples.Where(x => x.Success).Select(x => (double)x.InputChars).DefaultIfEmpty().Average();
}