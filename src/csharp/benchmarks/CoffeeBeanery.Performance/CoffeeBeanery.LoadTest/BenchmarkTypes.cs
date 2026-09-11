using System.Text;

static class GuidUtility
{
    public static readonly Guid UrlNamespace =
        new("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

    public static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, namespaceBytes.Length, nameBytes.Length);

        using var sha1 = System.Security.Cryptography.SHA1.Create();
        var hash = sha1.ComputeHash(data);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        SwapByteOrder(bytes);
        return new Guid(bytes);
    }

    private static void SwapByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}

record Target(
    string Name,
    string Url);

enum BenchmarkOperation
{
    QueryTop50,
    MutationWholeGraph,
    MutationThenQuery
}

static class BenchmarkOperationExtensions
{
    public static string DisplayName(
        this BenchmarkOperation operation) =>
        operation switch
        {
            BenchmarkOperation.QueryTop50 =>
                "Query top 50 graph",

            BenchmarkOperation.MutationWholeGraph =>
                "Mutation whole graph",

            BenchmarkOperation.MutationThenQuery =>
                "Upsert + select (upsert then query top 50 full graph)",

            _ => operation.ToString()
        };
}

record RunResult(
    long Requests,
    int Errors,
    int Timeouts,
    int Cancelled,
    double[] Latencies,
    int DurationSeconds,
    string? FirstError,
    int Drained,
    double CpuAveragePercent,
    double CpuMaxPercent,
    double MemoryAverageMb,
    double MemoryMaxMb,
    double MemoryEndMb)
{
    public double RequestsPerSecond => Requests / (double)Math.Max(1, DurationSeconds);
}

record DockerMetrics(
    double CpuAveragePercent = 0,
    double CpuMaxPercent = 0,
    double MemoryAverageMb = 0,
    double MemoryMaxMb = 0,
    double MemoryEndMb = 0);

record BenchmarkResult(
    BenchmarkOperation Operation,
    string Target,
    int Concurrency,
    int BatchSize,
    double RequestsPerSecond,
    double LogicalRequestsPerSecond,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    int Errors,
    double CpuAveragePercent,
    double CpuMaxPercent,
    double MemoryAverageMb,
    double MemoryMaxMb,
    double MemoryEndMb,
    bool Successful)
{
    public static BenchmarkResult Failed(BenchmarkOperation operation, Target target, int concurrency, int batchSize,
        int errors) =>
        new(operation, target.Name, concurrency, batchSize, 0, 0, 0, 0, 0, errors, 0, 0, 0, 0, 0, false);
}

record BenchmarkReport(
    DateTimeOffset GeneratedAt,
    int WarmupSeconds,
    int DurationSeconds,
    int RequestTimeoutSeconds,
    int ReadinessTimeoutSeconds,
    int DrainTimeoutSeconds,
    int[] Concurrency,
    IReadOnlyCollection<BenchmarkResult> Results);