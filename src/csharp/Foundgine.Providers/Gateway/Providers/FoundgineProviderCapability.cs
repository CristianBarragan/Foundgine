using System.Numerics;

namespace Foundgine.Providers.Gateway.Providers;

/// <summary>
/// Capabilities a provider declares. A flags enum keeps negotiation allocation-free and reflection-free
/// (Native AOT friendly). Distinct from <see cref="FoundgineGatewayCapability"/>, which is the HTTP discovery DTO.
/// </summary>
[Flags]
public enum FoundgineProviderCapability : long
{
    None = 0,
    DurableStorage = 1L << 0,
    OrderedEvents = 1L << 1,
    EventReplay = 1L << 2,
    TransactionalAppend = 1L << 3,
    TransactionalOutbox = 1L << 4,
    DurableExecution = 1L << 5,
    WorkflowSignals = 1L << 6,
    WorkflowQueries = 1L << 7,
    Retry = 1L << 8,
    DeadLetter = 1L << 9,
    Streaming = 1L << 10,
    TenantIsolation = 1L << 11
}

/// <summary>The infrastructure role a provider fills. Requirements are matched per kind.</summary>
public enum FoundgineProviderKind
{
    EventStore,
    Outbox,
    Workflow,
    Messaging,
    Realtime,
    Persistence,
    Identity,
    Diagnostics,
    Metadata
}

/// <summary>Switch-free, reflection-free flag naming for descriptors, diagnostics and error messages.</summary>
public static class FoundgineProviderCapabilityNames
{
    private static readonly (FoundgineProviderCapability Flag, string Name)[] Table =
    [
        (FoundgineProviderCapability.DurableStorage, nameof(FoundgineProviderCapability.DurableStorage)),
        (FoundgineProviderCapability.OrderedEvents, nameof(FoundgineProviderCapability.OrderedEvents)),
        (FoundgineProviderCapability.EventReplay, nameof(FoundgineProviderCapability.EventReplay)),
        (FoundgineProviderCapability.TransactionalAppend, nameof(FoundgineProviderCapability.TransactionalAppend)),
        (FoundgineProviderCapability.TransactionalOutbox, nameof(FoundgineProviderCapability.TransactionalOutbox)),
        (FoundgineProviderCapability.DurableExecution, nameof(FoundgineProviderCapability.DurableExecution)),
        (FoundgineProviderCapability.WorkflowSignals, nameof(FoundgineProviderCapability.WorkflowSignals)),
        (FoundgineProviderCapability.WorkflowQueries, nameof(FoundgineProviderCapability.WorkflowQueries)),
        (FoundgineProviderCapability.Retry, nameof(FoundgineProviderCapability.Retry)),
        (FoundgineProviderCapability.DeadLetter, nameof(FoundgineProviderCapability.DeadLetter)),
        (FoundgineProviderCapability.Streaming, nameof(FoundgineProviderCapability.Streaming)),
        (FoundgineProviderCapability.TenantIsolation, nameof(FoundgineProviderCapability.TenantIsolation))
    ];

    public static IReadOnlyList<string> ToNames(FoundgineProviderCapability capabilities)
    {
        var names = new List<string>(BitOperations.PopCount((ulong)capabilities));
        foreach (var (flag, name) in Table)
            if ((capabilities & flag) == flag) names.Add(name);
        return names;
    }

    public static string Describe(FoundgineProviderCapability capabilities) =>
        capabilities == FoundgineProviderCapability.None ? "None" : string.Join(", ", ToNames(capabilities));
}
