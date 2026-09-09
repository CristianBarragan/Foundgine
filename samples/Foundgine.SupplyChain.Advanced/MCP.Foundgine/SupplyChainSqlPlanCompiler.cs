using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Generated;
using Foundgine.Providers.Storage.Sql;

namespace Foundgine.SupplyChain.Advanced.OpenIntent;

/// <summary>
/// The SQL provider owns the physical storage mapping. It is deliberately not
/// registered as an IMetadataProvider and is never exposed through the MCP
/// capability contract. Callers submit semantic intents only.
/// </summary>
public sealed class SupplyChainSqlPlanCompiler : IProviderPlanCompiler
{
    private readonly SqlCompiler _compiler;

    public SupplyChainSqlPlanCompiler()
    {
        _compiler = new SqlCompiler(GeneratedMetadata.Build());
    }

    public ProviderPlan Compile(ExecutionIR ir) => _compiler.Compile(ir);
}
