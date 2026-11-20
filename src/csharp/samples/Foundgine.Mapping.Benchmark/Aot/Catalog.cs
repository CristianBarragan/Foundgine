using Foundgine.Core.Mapping;

namespace Mapping.Benchmark.Aot;

/// <summary>Runtime (fluent-built) models for every scenario: the source of truth the generated metadata must equal.</summary>
public static class Catalog
{
    public static readonly IReadOnlyDictionary<string, Func<MappingModel>> Runtime = new Dictionary<string, Func<MappingModel>>
    {
        ["Simple"] = Mapping.Benchmark.Scenarios.Simple.Build, ["Renamed"] = Mapping.Benchmark.Scenarios.Renamed.Build, ["Flattened"] = Mapping.Benchmark.Scenarios.Flattened.Build, ["Owned"] = Mapping.Benchmark.Scenarios.Owned.Build,
        ["Complex"] = Mapping.Benchmark.Scenarios.Complex.Build, ["InheritanceTph"] = Mapping.Benchmark.Scenarios.InheritanceTph.Build, ["InheritanceTpt"] = Mapping.Benchmark.Scenarios.InheritanceTpt.Build,
        ["ManyToMany"] = Mapping.Benchmark.Scenarios.ManyToMany.Build, ["Computed"] = Mapping.Benchmark.Scenarios.Computed.Build, ["Legacy"] = Mapping.Benchmark.Scenarios.Legacy.Build, ["MultiTenant"] = Mapping.Benchmark.Scenarios.MultiTenant.Build,
    };

    public static IReadOnlyDictionary<string, MappingModel> Built() => Runtime.ToDictionary(k => k.Key, k => k.Value());
}
