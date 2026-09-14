using System.ComponentModel;
using System.Text.Json;
using Foundgine.HighAssurance.Banking;
using Foundgine.HighAssurance.EfCore;
using Foundgine.Core.Semantic.Authorization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
var cs = builder.Configuration["BankingConnectionString"]
         ?? throw new InvalidOperationException("BankingConnectionString is not configured.");

builder.Services.AddDbContext<BankingDbContext>(options => options.UseNpgsql(cs));
builder.Services.AddSingleton<IBankAuthorization, OwnershipAuthorization>();
builder.Services.AddScoped<EfTransferFundsService>(sp =>
    new EfTransferFundsService(
        sp.GetRequiredService<BankingDbContext>(),
        sp.GetRequiredService<IBankAuthorization>().CanTransfer));

// Same MCP wiring as MCP.Foundgine (Postgres/Foundgine.Providers.Storage.Sql) — the only
// difference between the two MCP servers is the execution boundary behind
// the tool (EfTransferFundsService here vs PostgresTransferFundsService
// there). Protocol, tool name, and request/response shape are identical so
// the comparison isolates the persistence layer, not the transport.
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<TransferFundsMcpTools>();

var app = builder.Build();
app.MapMcp("/mcp");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (BankingDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));

app.Run();

[McpServerToolType]
public sealed class TransferFundsMcpTools
{
    private readonly EfTransferFundsService _service;

    public TransferFundsMcpTools(EfTransferFundsService service) => _service = service;

    [McpServerTool(Name = "transfer_funds")]
    [Description(
        "Execute the high-assurance Foundgine TransferFunds capability through the EF Core execution boundary. Authorization and security invariants are re-evaluated at execution time; the account-balance mutation is a single atomic predicated UPDATE, so the row lock happens inside PostgreSQL rather than via a separate SELECT ... FOR UPDATE.")]
    public async Task<string> TransferFundsAsync(
        Guid actorId,
        int tenantId,
        Guid sourceAccountId,
        Guid destinationAccountId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var command = new TransferFundsCommand(sourceAccountId, destinationAccountId, amount, idempotencyKey);
        var receipt = await _service.ExecuteAsync(actorId, tenantId, command, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            transferId = receipt.TransferId,
            amount = receipt.Amount,
            replay = receipt.Replay
        });
    }
}