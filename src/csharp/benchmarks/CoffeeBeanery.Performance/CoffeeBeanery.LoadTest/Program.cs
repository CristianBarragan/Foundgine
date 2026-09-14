using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

const string query = """
                     query Top50Customers {
                       customer(first: 50) {
                         id
                         customerKey
                         firstName
                         lastName
                         fullName
                         customerBankingRelationship {
                           id
                           customerBankingRelationshipKey
                           contract {
                             id
                             contractKey
                             amount
                             transaction {
                               id
                               transactionKey
                               amount
                               balance
                             }
                           }
                         }
                       }
                     }
                     """;

const string mutation = """
                        mutation UpsertCustomer($input: CustomerInput!) {
                          upsertCustomer(input: $input, onConflict: ["CustomerKey"]) {
                            id
                            customerKey
                            firstName
                            lastName
                            fullName
                          }
                        }
                        """;

var duration = GetInt("BENCHMARK_DURATION_SECONDS", 10);
var warmup = GetInt("BENCHMARK_WARMUP_SECONDS", 3);
var requestTimeoutSeconds = GetInt("BENCHMARK_REQUEST_TIMEOUT_SECONDS", 5);
var readinessTimeoutSeconds = GetInt("BENCHMARK_READINESS_TIMEOUT_SECONDS", 180);
var drainTimeoutSeconds = GetInt("BENCHMARK_RESET_TIMEOUT_SECONDS", 180);
var concurrency = (Environment.GetEnvironmentVariable("BENCHMARK_CONCURRENCY") ?? "1,8,32")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(int.Parse)
    .Where(x => x > 0)
    .Distinct()
    .ToArray();

if (concurrency.Length == 0)
    throw new InvalidOperationException("BENCHMARK_CONCURRENCY must contain at least one positive integer.");

var batchSizes = (Environment.GetEnvironmentVariable("BENCHMARK_BATCH_SIZES") ?? "1,10,50")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(int.Parse)
    .Where(x => x > 0)
    .Distinct()
    .ToArray();

if (batchSizes.Length == 0)
    throw new InvalidOperationException("BENCHMARK_BATCH_SIZES must contain at least one positive integer.");

var dockerMetricsContainer = Environment.GetEnvironmentVariable("BENCHMARK_DOCKER_CONTAINER");
var failOnErrors = string.Equals(
    Environment.GetEnvironmentVariable("BENCHMARK_FAIL_ON_ERRORS"),
    "true",
    StringComparison.OrdinalIgnoreCase);

var reportDirectory =
    Environment.GetEnvironmentVariable("BENCHMARK_REPORT_DIRECTORY") ?? "/reports";

Directory.CreateDirectory(reportDirectory);

var targetName = GetRequiredEnvironmentVariable("BENCHMARK_TARGET_NAME");
var targetUrl = GetRequiredEnvironmentVariable("BENCHMARK_TARGET_URL");

var targets = new[]
{
    new Target(targetName, targetUrl)
};

using var client = new HttpClient
{
    // Request cancellation is controlled explicitly below.
    Timeout = Timeout.InfiniteTimeSpan
};

client.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Foundgine-CoffeeBeanery-Benchmark/3.0");

Console.WriteLine("==============================================");
Console.WriteLine(" CoffeeBeanery / Single API Benchmark");
Console.WriteLine("==============================================");
Console.WriteLine($"Warm-up:              {warmup}s");
Console.WriteLine($"Measurement:          {duration}s");
Console.WriteLine($"Request timeout:      {requestTimeoutSeconds}s");
Console.WriteLine($"Readiness timeout:    {readinessTimeoutSeconds}s");
Console.WriteLine($"Drain timeout:        {drainTimeoutSeconds}s");
Console.WriteLine($"Concurrency:          {string.Join(", ", concurrency)}");
Console.WriteLine($"Batch sizes:           {string.Join(", ", batchSizes)}");
Console.WriteLine($"Docker metrics:        {dockerMetricsContainer ?? "disabled"}");
Console.WriteLine();

await WaitForTargetsAsync(targets, client, readinessTimeoutSeconds);

var results = new List<BenchmarkResult>();

foreach (var operation in new[]
         {
             BenchmarkOperation.QueryTop50,
             BenchmarkOperation.MutationWholeGraph,
             BenchmarkOperation.MutationThenQuery
         })
{
    Console.WriteLine($"== {BenchmarkOperationExtensions.DisplayName(operation)} ==");

    foreach (var target in targets)
    {
        var operationBatchSizes =
            operation is BenchmarkOperation.MutationWholeGraph or BenchmarkOperation.MutationThenQuery
                ? batchSizes
                : new[] { 1 };

        foreach (var batchSize in operationBatchSizes)
        {
            if (operation is BenchmarkOperation.MutationWholeGraph or BenchmarkOperation.MutationThenQuery)
                Console.WriteLine($"  Batch size={batchSize}");

            foreach (var c in concurrency)
            {
                Console.WriteLine(
                    $"  {target.Name} C={c} batch={batchSize}: warm-up...");

                var warmupResult =
                    await Run(
                        target,
                        operation,
                        c,
                        batchSize,
                        warmup,
                        requestTimeoutSeconds,
                        drainTimeoutSeconds,
                        client,
                        dockerMetricsContainer);

                Console.WriteLine(
                    $"    warm-up completed={warmupResult.Requests}, " +
                    $"errors={warmupResult.Errors}, " +
                    $"timeouts={warmupResult.Timeouts}, " +
                    $"cancelled={warmupResult.Cancelled}");

                // Warm-up is diagnostic. A slow/erroring warm-up request must
                // not prevent the actual measurement from running as long as
                // at least one request completed successfully.
                if (warmupResult.Errors != 0 || warmupResult.Timeouts != 0)
                {
                    Console.WriteLine(
                        $"    PREFLIGHT WARNING: {DescribeFirstError(warmupResult)}");
                }

                if (warmupResult.Requests == 0)
                {
                    Console.WriteLine(
                        "    PREFLIGHT FAILED: zero completed requests.");

                    results.Add(BenchmarkResult.Failed(operation, target, c, batchSize, 1));
                    continue;
                }

                Console.WriteLine(
                    $"  {target.Name} C={c} batch={batchSize}: measuring...");

                var result =
                    await Run(
                        target,
                        operation,
                        c,
                        batchSize,
                        duration,
                        requestTimeoutSeconds,
                        drainTimeoutSeconds,
                        client,
                        dockerMetricsContainer);

                var benchmark = new BenchmarkResult(
                    operation,
                    target.Name,
                    c,
                    batchSize,
                    result.RequestsPerSecond,
                    result.RequestsPerSecond * batchSize,
                    Percentile(result.Latencies, .50),
                    Percentile(result.Latencies, .95),
                    Percentile(result.Latencies, .99),
                    result.Errors + result.Timeouts,
                    result.CpuAveragePercent,
                    result.CpuMaxPercent,
                    result.MemoryAverageMb,
                    result.MemoryMaxMb,
                    result.MemoryEndMb,
                    result.Requests > 0 && result.Errors == 0 && result.Timeouts == 0);

                results.Add(benchmark);

                Console.WriteLine(
                    $"    RPS={benchmark.RequestsPerSecond:F2} " +
                    $"logical/s={benchmark.LogicalRequestsPerSecond:F2} " +
                    $"p50={benchmark.P50Ms:F1}ms " +
                    $"p95={benchmark.P95Ms:F1}ms " +
                    $"p99={benchmark.P99Ms:F1}ms " +
                    $"CPU avg/max={benchmark.CpuAveragePercent:F1}%/{benchmark.CpuMaxPercent:F1}% " +
                    $"MEM avg/max/end={benchmark.MemoryAverageMb:F1}/{benchmark.MemoryMaxMb:F1}/{benchmark.MemoryEndMb:F1}MB " +
                    $"completed={result.Requests} errors={result.Errors} timeouts={result.Timeouts}");

                if (result.Errors != 0 || result.Timeouts != 0)
                    Console.WriteLine($"    First error: {DescribeFirstError(result)}");
            }

            Console.WriteLine();
        }
    }
}

var timestamp =
    DateTimeOffset.UtcNow.ToString(
        "yyyyMMdd-HHmmss",
        CultureInfo.InvariantCulture);

var report = new BenchmarkReport(
    DateTimeOffset.UtcNow,
    warmup,
    duration,
    requestTimeoutSeconds,
    readinessTimeoutSeconds,
    drainTimeoutSeconds,
    concurrency,
    results);

var safeTargetName = string.Concat(
    targetName.Select(c => char.IsLetterOrDigit(c) ? c : '-'));

var jsonPath =
    Path.Combine(
        reportDirectory,
        $"benchmark-{safeTargetName}-{timestamp}.json");

var csvPath =
    Path.Combine(
        reportDirectory,
        $"benchmark-{safeTargetName}-{timestamp}.csv");

var mdPath =
    Path.Combine(
        reportDirectory,
        $"benchmark-{safeTargetName}-{timestamp}.md");

await File.WriteAllTextAsync(
    jsonPath,
    JsonSerializer.Serialize(
        report,
        new JsonSerializerOptions
        {
            WriteIndented = true
        }));

var csv = new StringBuilder();

csv.AppendLine(
    "operation,target,concurrency,batch_size,requests_per_second,logical_requests_per_second," +
    "p50_ms,p95_ms,p99_ms,cpu_avg_percent,cpu_max_percent,memory_avg_mb,memory_max_mb,memory_end_mb,errors,successful");

foreach (var r in results)
{
    csv.AppendLine(
        string.Join(',',
            Csv(BenchmarkOperationExtensions.DisplayName(r.Operation)),
            Csv(r.Target),
            r.Concurrency,
            r.BatchSize,
            Number(r.RequestsPerSecond),
            Number(r.LogicalRequestsPerSecond),
            Number(r.P50Ms),
            Number(r.P95Ms),
            Number(r.P99Ms),
            Number(r.CpuAveragePercent),
            Number(r.CpuMaxPercent),
            Number(r.MemoryAverageMb),
            Number(r.MemoryMaxMb),
            Number(r.MemoryEndMb),
            r.Errors,
            r.Successful));
}

await File.WriteAllTextAsync(csvPath, csv.ToString());
await File.WriteAllTextAsync(mdPath, BuildMarkdownReport(report));

Console.WriteLine("==============================================");
Console.WriteLine(" Benchmark completed");
Console.WriteLine("==============================================");
Console.WriteLine($"JSON report: {jsonPath}");
Console.WriteLine($"CSV report:  {csvPath}");
Console.WriteLine($"Markdown:    {mdPath}");

if (failOnErrors)
{
    var failedResults = results.Count(r => !r.Successful);

    if (failedResults > 0)
    {
        Console.Error.WriteLine(
            $"BENCHMARK FAILED: {failedResults} measurement(s) reported errors or timeouts.");
        Environment.ExitCode = 1;
    }
}

static async Task WaitForTargetsAsync(
    IReadOnlyList<Target> targets,
    HttpClient client,
    int readinessTimeoutSeconds)
{
    foreach (var target in targets)
    {
        var health = ToHealthUrl(target.Url);
        var deadline = Stopwatch.StartNew();
        var ready = false;

        while (deadline.Elapsed < TimeSpan.FromSeconds(readinessTimeoutSeconds))
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var response = await client.GetAsync(health, timeoutCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Ready: {target.Name}");
                    ready = true;
                    break;
                }
            }
            catch
            {
                // Target is still starting.
            }

            await Task.Delay(1000);
        }

        if (!ready)
        {
            throw new InvalidOperationException(
                $"Target did not become ready within {readinessTimeoutSeconds}s: " +
                $"{target.Name} ({health})");
        }
    }

    Console.WriteLine();
}

static async Task<RunResult> Run(
    Target target,
    BenchmarkOperation operation,
    int concurrency,
    int batchSize,
    int seconds,
    int requestTimeoutSeconds,
    int drainTimeoutSeconds,
    HttpClient client,
    string? dockerContainer)
{
    var latencies = new ConcurrentBag<double>();
    var errors = 0;
    var timeouts = 0;
    var requests = 0L;
    string? firstError = null;

    // IMPORTANT:
    // Do not cancel worker tasks at the phase boundary.
    // The old implementation used a CancellationToken to stop the phase,
    // which created a race where workers could observe cancellation before
    // starting their first request. That produced:
    //
    //   completed=0, errors=0, timeouts=0, cancelled=N
    //
    // The benchmark phase is now controlled only by a deadline. Workers stop
    // STARTING new requests after the deadline, while any request already in
    // flight is allowed to finish (or hit its request timeout).
    var deadline = Stopwatch.StartNew();
    using var metricsCts = new CancellationTokenSource();
    var metricsTask = dockerContainer is null
        ? Task.FromResult(new DockerMetrics())
        : SampleDockerMetricsAsync(dockerContainer, metricsCts.Token, deadline, TimeSpan.FromSeconds(seconds));

    var workers = Enumerable.Range(0, concurrency)
        .Select(_ => WorkerAsync())
        .ToArray();

    // Wait until every worker has stopped naturally.
    // There is deliberately no phase cancellation here.
    var allWorkers = Task.WhenAll(workers);

    var completed = await Task.WhenAny(
        allWorkers,
        Task.Delay(TimeSpan.FromSeconds(
            Math.Max(
                seconds + requestTimeoutSeconds + 5,
                drainTimeoutSeconds))));

    if (completed != allWorkers)
    {
        // This should only happen if the entire worker group has exceeded the
        // configured safety drain period. Do not cancel HTTP requests here:
        // cancellation would corrupt the benchmark accounting.
        Console.WriteLine(
            $"    WARNING: {target.Name} C={concurrency} " +
            $"did not drain within {drainTimeoutSeconds}s.");
    }

    try
    {
        await allWorkers;
    }
    catch
    {
        // WorkerAsync captures request-level failures.
    }

    metricsCts.Cancel();
    var metrics = await metricsTask;

    return new RunResult(
        requests,
        errors,
        timeouts,
        0,
        latencies.ToArray(),
        seconds,
        firstError,
        0,
        metrics.CpuAveragePercent,
        metrics.CpuMaxPercent,
        metrics.MemoryAverageMb,
        metrics.MemoryMaxMb,
        metrics.MemoryEndMb);

    async Task WorkerAsync()
    {
        while (deadline.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            if (operation == BenchmarkOperation.MutationThenQuery)
            {
                await RunMutationThenQueryAsync();
                continue;
            }

            var body = operation == BenchmarkOperation.QueryTop50
                ? JsonSerializer.Serialize(new { query })
                : JsonSerializer.Serialize(BuildMutationBatch(batchSize));

            using var content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json");

            using var requestCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(requestTimeoutSeconds));

            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var response = await client.PostAsync(
                    target.Url,
                    content,
                    requestCts.Token);

                var responseBody =
                    await response.Content.ReadAsStringAsync();

                stopwatch.Stop();

                Interlocked.Increment(ref requests);
                latencies.Add(stopwatch.Elapsed.TotalMilliseconds);

                if (!response.IsSuccessStatusCode ||
                    responseBody.Contains(
                        "\"errors\"",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref errors);

                    Interlocked.CompareExchange(
                        ref firstError,
                        responseBody,
                        null);
                }
            }
            catch (OperationCanceledException)
                when (requestCts.IsCancellationRequested)
            {
                stopwatch.Stop();

                Interlocked.Increment(ref timeouts);

                Interlocked.CompareExchange(
                    ref firstError,
                    $"request timeout after {requestTimeoutSeconds}s",
                    null);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                Interlocked.Increment(ref errors);

                Interlocked.CompareExchange(
                    ref firstError,
                    ex.ToString(),
                    null);
            }

            // The deadline is checked again at the top of the loop.
            // Therefore an in-flight request that finishes after the phase
            // boundary is counted, but no new request is started.
        }

        // Upsert-then-select as ONE measured logical unit: upsert existing deterministic
        // Customer rows, then immediately execute the exact same QueryTop50 graph
        // used by the standalone query benchmark. One stopwatch spans both calls
        // so p50/p95/p99 represent the complete write-then-refetch client path.
        async Task RunMutationThenQueryAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            string? errorBody = null;
            var timedOut = false;

            async Task<bool> PostAsync(string requestBody)
            {
                using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                using var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(requestTimeoutSeconds));

                try
                {
                    using var response = await client.PostAsync(target.Url, content, requestCts.Token);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode ||
                        responseBody.Contains("\"errors\"", StringComparison.OrdinalIgnoreCase))
                    {
                        errorBody ??= responseBody;
                        return false;
                    }

                    return true;
                }
                catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
                {
                    timedOut = true;
                    errorBody ??= $"request timeout after {requestTimeoutSeconds}s";
                    return false;
                }
                catch (Exception ex)
                {
                    errorBody ??= ex.ToString();
                    return false;
                }
            }

            var mutationOk = await PostAsync(JsonSerializer.Serialize(BuildMutationBatch(batchSize)));

            // Only refetch if the write succeeded - charging read latency against
            // a failed write would misrepresent both halves of the measurement.
            var queryOk = mutationOk && await PostAsync(JsonSerializer.Serialize(new { query }));

            stopwatch.Stop();
            Interlocked.Increment(ref requests);
            latencies.Add(stopwatch.Elapsed.TotalMilliseconds);

            if (!mutationOk || !queryOk)
            {
                if (timedOut)
                    Interlocked.Increment(ref timeouts);
                else
                    Interlocked.Increment(ref errors);

                Interlocked.CompareExchange(ref firstError, errorBody, null);
            }
        }
    }
}

static object BuildMutationBatch(int batchSize)
{
    if (batchSize == 1)
    {
        return new
        {
            query = mutation,
            operationName = "UpsertCustomer",
            variables = new
            {
                input = CreateUpsertInput(1)
            }
        };
    }

    var sb = new StringBuilder();
    sb.AppendLine("mutation BenchmarkUpsertBatch {");
    for (var i = 1; i <= batchSize; i++)
    {
        var alias = "m" + i;
        var key = DeterministicGuid("customer", i);
        sb.Append("  ").Append(alias)
            .Append(": upsertCustomer(input: {")
            .Append(" customerKey: \"").Append(key).Append("\"")
            .Append(" firstName: \"Benchmark\"")
            .Append(" lastName: \"Customer\"")
            .Append(" fullName: \"Benchmark Customer ").Append(i).Append("\"")
            .Append(" }, onConflict: [\"CustomerKey\"]) { id customerKey firstName lastName fullName }")
            .AppendLine();
    }

    sb.AppendLine("}");
    return new { query = sb.ToString(), operationName = "BenchmarkUpsertBatch" };
}

static object CreateUpsertInput(int customerId)
{
    return new
    {
        customerKey = DeterministicGuid("customer", customerId),
        firstName = "Benchmark",
        lastName = "Customer",
        fullName = $"Benchmark Customer {customerId}"
    };
}

static Guid DeterministicGuid(string prefix, int value) =>
    GuidUtility.Create(
        GuidUtility.UrlNamespace,
        $"coffee-beanery/{prefix}/{value}");


static async Task<DockerMetrics> SampleDockerMetricsAsync(string container, CancellationToken cancellationToken,
    Stopwatch phase, TimeSpan duration)
{
    var cpu = new List<double>();
    var mem = new List<double>();
    while (!cancellationToken.IsCancellationRequested && phase.Elapsed < duration + TimeSpan.FromSeconds(2))
    {
        var sample = await ReadDockerStatsAsync(container);
        if (sample is not null)
        {
            cpu.Add(sample.Value.CpuPercent);
            mem.Add(sample.Value.MemoryMb);
        }

        try
        {
            await Task.Delay(500, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    return new DockerMetrics(
        cpu.Count == 0 ? 0 : cpu.Average(),
        cpu.Count == 0 ? 0 : cpu.Max(),
        mem.Count == 0 ? 0 : mem.Average(),
        mem.Count == 0 ? 0 : mem.Max(),
        mem.Count == 0 ? 0 : mem[^1]);
}

static async Task<(double CpuPercent, double MemoryMb)?> ReadDockerStatsAsync(string container)
{
    var psi = new ProcessStartInfo("docker", $"stats --no-stream --format \"{{.CPUPerc}};{{.MemUsage}}\" {container}")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    using var process = Process.Start(psi);
    if (process is null) return null;
    var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
    await process.WaitForExitAsync();
    if (string.IsNullOrWhiteSpace(output)) return null;
    var parts = output.Split(';', 2);
    if (parts.Length != 2 || !double.TryParse(parts[0].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture,
            out var cpu)) return null;
    var memText = parts[1].Split('/', 2)[0].Trim();
    var multiplier = memText.EndsWith("GiB", StringComparison.OrdinalIgnoreCase) ? 1024 :
        memText.EndsWith("KiB", StringComparison.OrdinalIgnoreCase) ? 1.0 / 1024 : 1;
    var numeric = memText.Replace("GiB", "", StringComparison.OrdinalIgnoreCase)
        .Replace("MiB", "", StringComparison.OrdinalIgnoreCase).Replace("KiB", "", StringComparison.OrdinalIgnoreCase)
        .Replace("B", "", StringComparison.OrdinalIgnoreCase).Trim();
    if (!double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var mem)) return null;
    if (memText.Contains("MiB", StringComparison.OrdinalIgnoreCase)) multiplier = 1;
    if (memText.Contains("B", StringComparison.OrdinalIgnoreCase) &&
        !memText.Contains("iB", StringComparison.OrdinalIgnoreCase)) multiplier = 1.0 / (1024 * 1024);
    return (cpu, mem * multiplier);
}

static string ToHealthUrl(string url)
{
    if (url.EndsWith("/graphql/cold", StringComparison.OrdinalIgnoreCase) ||
        url.EndsWith("/graphql/warm", StringComparison.OrdinalIgnoreCase))
    {
        return url[..url.LastIndexOf("/graphql", StringComparison.OrdinalIgnoreCase)] + "/health";
    }

    if (url.EndsWith("/graphql", StringComparison.OrdinalIgnoreCase))
        return url[..^"/graphql".Length] + "/health";

    return url.TrimEnd('/') + "/health";
}

static string GetRequiredEnvironmentVariable(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException(
            $"Required environment variable '{name}' is not set.");

static double Percentile(
    double[] values,
    double percentile)
{
    if (values.Length == 0)
        return 0;

    Array.Sort(values);

    var index =
        (int)Math.Ceiling(
            percentile * values.Length) - 1;

    return values[
        Math.Clamp(
            index,
            0,
            values.Length - 1)];
}

static int GetInt(
    string name,
    int fallback) =>
    int.TryParse(
        Environment.GetEnvironmentVariable(name),
        out var value) &&
    value > 0
        ? value
        : fallback;

static string Csv(string value) =>
    $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

static string Number(double value) =>
    double.IsNaN(value) ||
    double.IsInfinity(value)
        ? ""
        : value.ToString(
            "F2",
            CultureInfo.InvariantCulture);

static string DescribeFirstError(RunResult result) =>
    result.FirstError ??
    (result.Timeouts > 0
        ? "request timeout"
        : result.Errors > 0
            ? "request returned an error"
            : "no error");

static string BuildMarkdownReport(
    BenchmarkReport report)
{
    var sb = new StringBuilder();

    sb.AppendLine("# CoffeeBeanery / Three API Performance Benchmark");
    sb.AppendLine();
    sb.AppendLine($"- Generated: `{report.GeneratedAt:O}`");
    sb.AppendLine($"- Warm-up: `{report.WarmupSeconds}s`");
    sb.AppendLine($"- Measurement: `{report.DurationSeconds}s`");
    sb.AppendLine($"- Request timeout: `{report.RequestTimeoutSeconds}s`");
    sb.AppendLine($"- Readiness timeout: `{report.ReadinessTimeoutSeconds}s`");
    sb.AppendLine($"- Drain timeout: `{report.DrainTimeoutSeconds}s`");
    sb.AppendLine($"- Concurrency: `{string.Join(", ", report.Concurrency)}`");
    sb.AppendLine();
    sb.AppendLine("## Results");
    sb.AppendLine();
    sb.AppendLine(
        "| Operation | Target | Concurrency | Batch | RPS | Logical/s | p50 | p95 | p99 | CPU avg/max | MEM avg/max/end | Errors |");
    sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

    foreach (var r in report.Results)
    {
        sb.AppendLine(
            $"| {BenchmarkOperationExtensions.DisplayName(r.Operation)} | " +
            $"{r.Target} | " +
            $"{r.Concurrency} | " +
            $"{r.BatchSize} | " +
            $"{Number(r.RequestsPerSecond)} | " +
            $"{Number(r.LogicalRequestsPerSecond)} | " +
            $"{Number(r.P50Ms)} ms | " +
            $"{Number(r.P95Ms)} ms | " +
            $"{Number(r.P99Ms)} ms | " +
            $"{Number(r.CpuAveragePercent)}/{Number(r.CpuMaxPercent)}% | " +
            $"{Number(r.MemoryAverageMb)}/{Number(r.MemoryMaxMb)}/{Number(r.MemoryEndMb)} MB | " +
            $"{r.Errors} |");
    }

    sb.AppendLine();
    sb.AppendLine("## Workloads");
    sb.AppendLine();
    sb.AppendLine(
        "**MutationWholeGraph** creates one Customer, one CustomerBankingRelationship, " +
        "one Contract and two Transactions in one GraphQL mutation. " +
        "The child foreign keys are resolved from the parent operation results by Foundgine.");
    sb.AppendLine();
    sb.AppendLine(
        "**QueryTop50** selects the first 50 customers and traverses " +
        "Customer -> CustomerBankingRelationship -> Contract -> Transaction.");
    sb.AppendLine();
    sb.AppendLine(
        "The Foundgine warm cache applies to the provider execution plan for the " +
        "read/query workload. Mutation plans are compiled per request because their " +
        "parameter values are intentionally dynamic.");
    sb.AppendLine();
    sb.AppendLine(
        "Each warm-up and measurement phase has a hard wall-clock boundary. " +
        "At phase expiry, new requests stop immediately and in-flight HTTP requests " +
        "are cancelled. Cancelled requests at the phase boundary are not counted as " +
        "errors or measured samples.");
    sb.AppendLine();
    sb.AppendLine(
        "Requests that exceed the explicit request timeout are counted as timeouts. " +
        "The benchmark no longer uses a 30-second HttpClient timeout to drain a phase.");

    return sb.ToString();
}