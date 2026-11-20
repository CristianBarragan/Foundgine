using Foundgine.Providers.Tools.MCP;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.Shop;

/// <summary>
/// The whole cost of a second protocol. MCP reuses the semantic model, the row-level policy and the JWT bridge that GraphQL
/// already uses: there is no per-protocol mapping, resolver or authorization code to write or keep in sync.
/// </summary>
public static class ShopMcp
{
    public static IServiceCollection AddShopMcp(this IServiceCollection services, Func<ExecutionContext> context)
    {
        services.AddFoundgineMcp(context); // the caller's SecurityExecutionContext comes from the registered JwtSecurityContextProvider
        services.AddMcpServer().WithHttpTransport(o => o.Stateless = true).WithTools<FoundgineMcpTools>();
        return services;
    }

    public static IEndpointRouteBuilder MapShopMcp(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMcp("/mcp").RequireAuthorization();
        return endpoints;
    }
}
