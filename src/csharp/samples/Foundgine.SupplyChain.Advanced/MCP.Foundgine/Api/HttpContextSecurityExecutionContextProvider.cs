using Foundgine.Core.Semantic.Security.Execution;
using Microsoft.AspNetCore.Http;

namespace Foundgine.SupplyChain.Advanced.OpenIntent.Api;

public sealed class HttpContextSecurityExecutionContextProvider : ISecurityExecutionContextProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextSecurityExecutionContextProvider(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public SecurityExecutionContext? GetSecurityExecutionContext() =>
        _httpContextAccessor.HttpContext is { } context
            ? OpenIntentDemoSecurity.GetSecurityExecutionContext(context)
            : null;
}
