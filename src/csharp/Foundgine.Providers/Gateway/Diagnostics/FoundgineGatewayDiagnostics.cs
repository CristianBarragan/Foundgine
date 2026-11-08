using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Foundgine.Providers.Gateway.Diagnostics;

/// <summary>
/// OpenTelemetry-compatible diagnostics built on the .NET ActivitySource and Meter APIs.
/// Hosts opt in by subscribing their OpenTelemetry SDK to these names; no SDK dependency is required.
/// </summary>
public static class FoundgineGatewayDiagnostics
{
    public const string InstrumentationName = "Foundgine.Gateway";
    public const string InstrumentationVersion = "1.0.0";

    public static ActivitySource ActivitySource { get; } =
        new(InstrumentationName, InstrumentationVersion);

    public static Meter Meter { get; } =
        new(InstrumentationName, InstrumentationVersion);

    private static readonly Counter<long> EventsPublished =
        Meter.CreateCounter<long>("foundgine.gateway.events.published", "{event}");
    private static readonly Counter<long> HookFailures =
        Meter.CreateCounter<long>("foundgine.gateway.hooks.failures", "{failure}");
    private static readonly Counter<long> SubscriberDeliveries =
        Meter.CreateCounter<long>("foundgine.gateway.events.enqueued", "{event}");
    private static readonly Counter<long> OutboxMessages =
        Meter.CreateCounter<long>("foundgine.outbox.messages", "{message}");
    private static readonly Histogram<double> PublishDuration =
        Meter.CreateHistogram<double>("foundgine.gateway.events.publish.duration", "ms");

    internal static Activity? StartPublishActivity(FoundgineGatewayEventType type, string? providerId)
    {
        var activity = ActivitySource.StartActivity("gateway.event.publish", ActivityKind.Internal);
        activity?.SetTag("foundgine.event.type", type.ToString());
        if (!string.IsNullOrWhiteSpace(providerId))
            activity?.SetTag("foundgine.provider.id", providerId);
        return activity;
    }

    internal static void RecordPublished(FoundgineGatewayEventType type, string? providerId, double elapsedMs)
    {
        var tags = new TagList { { "event.type", type.ToString() } };
        if (!string.IsNullOrWhiteSpace(providerId)) tags.Add("provider.id", providerId);
        EventsPublished.Add(1, tags);
        PublishDuration.Record(elapsedMs, tags);
    }

    internal static void RecordEnqueued(FoundgineGatewayEventType type) =>
        SubscriberDeliveries.Add(1, new KeyValuePair<string, object?>("event.type", type.ToString()));

    internal static void RecordHookFailure(string hookType, bool critical) =>
        HookFailures.Add(1,
            new KeyValuePair<string, object?>("hook.type", hookType),
            new KeyValuePair<string, object?>("hook.critical", critical));

    internal static Activity? StartOutboxActivity(string topic)
    {
        var activity = ActivitySource.StartActivity("outbox.dispatch", ActivityKind.Producer);
        activity?.SetTag("foundgine.outbox.topic", topic);
        return activity;
    }

    /// <summary>Outcome is one of: published, retry_scheduled, dead_lettered, loop_error (low cardinality by design).</summary>
    internal static void RecordOutbox(string outcome) =>
        OutboxMessages.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
