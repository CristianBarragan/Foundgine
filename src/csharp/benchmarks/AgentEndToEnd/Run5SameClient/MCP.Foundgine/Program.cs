using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.Postgres;
using Foundgine.HighAssurance.Postgres.Execution;
using Foundgine.Providers.Tools.MCP;
using Foundgine.Core.Semantic.Authorization;
using ModelContextProtocol.Server;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration["BankingConnectionString"]
         ?? Environment.GetEnvironmentVariable("BankingConnectionString")
         ?? throw new InvalidOperationException("BankingConnectionString is not configured.");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(cs);
var dataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<IBankAuthorization, OwnershipAuthorization>();
builder.Services.AddScoped<PostgresTransferFundsExecutor>(sp =>
    new PostgresTransferFundsExecutor(dataSource, sp.GetRequiredService<IBankAuthorization>().CanTransfer));
builder.Services.AddScoped<PostgresTransferFundsService>();

// This is deliberately the MCP boundary: MCP transports a capability invocation,
// while the high-assurance Postgres service remains the domain execution boundary.
builder.Services.AddFoundgineMcp(() => new Foundgine.Core.Execution.ExecutionContext());
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<TransferMcpTools>();

var app = builder.Build();
app.MapMcp("/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (CancellationToken ct) =>
{
    await using var connection = await dataSource.OpenConnectionAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT 1;";
    await command.ExecuteScalarAsync(ct);
    return Results.Ok(new { status = "ready" });
});
app.Run();

[McpServerToolType]
public sealed class TransferMcpTools
{
    private readonly IServiceScopeFactory _scopeFactory;
    public TransferMcpTools(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    [McpServerTool(Name = "transfer_funds_batch")]
    public async Task<object> TransferFundsBatch(
        Guid actorId,
        int tenantId,
        TransferFundsBatchItem[] transfers,
        CancellationToken cancellationToken = default)
    {
        if (transfers is null || transfers.Length == 0)
            throw new ArgumentException("transfers must contain at least one item.");
        var commands = transfers.Select(x => new TransferFundsCommand(
            x.SourceAccountId, x.DestinationAccountId, x.Amount, x.IdempotencyKey)).ToArray();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<PostgresTransferFundsService>();
        var receipts = await service.ExecuteBatchAsync(actorId, tenantId, commands, cancellationToken);
        return receipts.Select((receipt, i) => new
        {
            Index = i, receipt.TransferId, receipt.SourceAccountId, receipt.DestinationAccountId,
            receipt.Amount, receipt.Replay, receipt.SecurityProof
        }).ToArray();
    }

    [McpServerTool(Name = "transfer_funds")]
    public async Task<object> TransferFunds(
        Guid actorId,
        int tenantId,
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var command = new TransferFundsCommand(sourceAccountId, destinationAccountId, amount, idempotencyKey);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<PostgresTransferFundsService>();
        var receipt = await service.ExecuteAsync(actorId, tenantId, command, cancellationToken);
        return new
        {
            receipt.TransferId, receipt.SourceAccountId, receipt.DestinationAccountId, receipt.Amount, receipt.Replay,
            receipt.SecurityProof
        };
    }
}

public sealed record TransferFundsBatchItem(
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string IdempotencyKey);