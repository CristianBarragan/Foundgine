using Foundgine.Runtime;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Execution;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Core.Semantic.Authorization;
using Foundgine.Providers.Storage.Sql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Run4.McpFoundgine;
using System.Data.Common;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration.GetConnectionString("BankingConnectionString") ??
         throw new InvalidOperationException("Missing connection string.");
var model = CoffeeBeanerySemanticModel.Build();
var metadata = CoffeeBeaneryMetadata.Build();
var policy = new AllowAllSemanticAuthorizationPolicy();

builder.Services.AddSingleton<IProviderPlanCompiler>(_ => new SqlCompiler(metadata));
builder.Services.AddSingleton<IExecutionProvider>(_ => new PooledSqlExecutionProvider(cs));
builder.Services.AddFoundgine(model, policy);
builder.Services.AddFoundgineMcp(() => new ExecutionContext());
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<FoundgineMcpTools>();

var app = builder.Build();
app.MapMcp("/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (CancellationToken ct) =>
{
    await using var db = new NpgsqlConnection(cs);
    await db.OpenAsync(ct);
    return Results.Ok(new { status = "ready" });
});
app.Run();

sealed class PooledSqlExecutionProvider : IExecutionProvider
{
    private readonly string _connectionString;
    public PooledSqlExecutionProvider(string connectionString) => _connectionString = connectionString;

    public async Task<ExecutionResult> ExecuteAsync(ProviderPlan plan, ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var search = connection.CreateCommand();
        search.CommandText = "SET search_path TO \"Banking\", \"Lending\", \"Accounting\";";
        await search.ExecuteNonQueryAsync(cancellationToken);
        return await new SqlExecutionProvider(connection).ExecuteAsync(plan, context, cancellationToken);
    }
}