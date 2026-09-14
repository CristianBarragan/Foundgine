namespace Foundgine.Runtime.ControlPlane.ToolRegistry;

/// <summary>Source of truth for which tools exist and their governance metadata.</summary>
public interface IToolRegistry
{
    bool TryGet(string toolName, out ToolDescriptor? descriptor);

    IReadOnlyCollection<ToolDescriptor> ListActive();

    void Register(ToolDescriptor descriptor);
}

/// <summary>
/// Process-local tool registry. Suitable for a single host instance; a
/// distributed deployment should back <see cref="IToolRegistry"/> with a
/// shared store instead, the same substitution pattern used by
/// <c>IProviderPlanCache</c> / <c>MemoryProviderPlanCache</c>.
/// </summary>
public sealed class InMemoryToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ToolDescriptor> _tools = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <param name="seed">
    /// Descriptors to register immediately, e.g. from DI-collected
    /// <see cref="ToolDescriptor"/> registrations made via
    /// <c>ToolGovernanceBuilder.RegisterTool</c>.
    /// </param>
    public InMemoryToolRegistry(IEnumerable<ToolDescriptor>? seed = null)
    {
        foreach (var descriptor in seed ?? Enumerable.Empty<ToolDescriptor>())
            Register(descriptor);
    }

    public bool TryGet(string toolName, out ToolDescriptor? descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        lock (_gate)
        {
            return _tools.TryGetValue(toolName, out descriptor);
        }
    }

    public IReadOnlyCollection<ToolDescriptor> ListActive()
    {
        lock (_gate)
        {
            return _tools.Values.Where(t => t.Status == ToolStatus.Active).ToArray();
        }
    }

    public void Register(ToolDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_gate)
        {
            _tools[descriptor.ToolName] = descriptor;
        }
    }
}
