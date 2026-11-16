using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Foundgine.Extensions.Temporal.Observability;

/// <summary>ActivitySource/Meter for the Temporal integration. Subscribe your OpenTelemetry SDK to <see cref="Name"/>.</summary>
public static class FoundgineTemporalDiagnostics
{
    public const string Name = "Foundgine.Temporal";

    public static ActivitySource ActivitySource { get; } = new(Name, "1.0.0");
    public static Meter Meter { get; } = new(Name, "1.0.0");

    private static readonly Counter<long> Starts = Meter.CreateCounter<long>("foundgine.temporal.workflows.started", "{workflow}");
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>("foundgine.temporal.operations", "{operation}");

    /// <summary>Outcome: started | duplicate. Process names are low-cardinality; tenants/ids are never tags.</summary>
    internal static void RecordStart(string process, string outcome) =>
        Starts.Add(1, new KeyValuePair<string, object?>("process", process), new KeyValuePair<string, object?>("outcome", outcome));

    internal static Activity? StartOperation(string operation, bool compensation)
    {
        Operations.Add(1, new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("compensation", compensation));
        var activity = ActivitySource.StartActivity("temporal.operation", ActivityKind.Internal);
        activity?.SetTag("foundgine.operation", operation);
        return activity;
    }
}
