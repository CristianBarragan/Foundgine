using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;

namespace Foundgine.Core.Mapping;

/// <summary>
/// Adds Domain -> Persistence mappings to a registry during <c>FoundgineModel.Build()</c>. Validates everything up front
/// and reports every problem in one exception: duplicate domain names, collisions with entities the fluent compiler
/// produced (the registry itself silently overwrites), and tables that tenant/owner rules cannot reach.
/// </summary>
internal static class MappingIntegration
{
    public static void Apply(IReadOnlyList<MappingModel> models, MetadataRegistry registry)
    {
        if (models.Count == 0) return;
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var model in models)
        {
            foreach (var mapping in model.Mappings)
            {
                if (!seen.Add(mapping.Domain))
                    errors.Add($"Domain type '{mapping.Domain}' is mapped more than once through UseMappings.");
                else if (mapping.Tables.Count > 0 && registry.TryGet(EntityId.Create(mapping.Domain), out _))
                    errors.Add($"Domain type '{mapping.Domain}' is mapped through UseMappings and also by the fluent compiler.");

                if (!ScopeResolver.DeclaresScope(model, mapping.Domain)) continue;
                try { ScopeResolver.Resolve(model, mapping.Domain); }
                catch (MappingScopeException e) { errors.Add($"{mapping.Domain}: {e.Message}"); }
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Foundgine mapping configuration is invalid:\n  " + string.Join("\n  ", errors));

        foreach (var model in models)
            MappingBridge.Register(model, registry);
    }
}
