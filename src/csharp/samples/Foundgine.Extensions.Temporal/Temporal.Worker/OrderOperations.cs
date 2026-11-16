using System.Text.Json;
using Foundgine.Extensions.Temporal;

// Each operation is idempotent by context.IdempotencyKey (stable across Temporal retries).
// Replace the bodies with IFoundgineGatewayExecution / IFoundgine calls: the activity's security context is
// already in place, so those services authorize as the process initiator's tenant.

public abstract class OrderOperation(string name) : IFoundgineTemporalOperation
{
    public string Name { get; } = name;
    public abstract Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext context, CancellationToken ct);
    protected static JsonElement Payload(FoundgineOperationContext c) => JsonDocument.Parse(c.PayloadJson).RootElement;
}

public sealed class ValidateOrder() : OrderOperation("orders.validate")
{
    public override Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct)
    {
        var p = Payload(c);
        if (!p.TryGetProperty("total", out var total) || total.GetDecimal() <= 0)
            throw new FoundgineNonRetryableOperationException("Order total must be positive.");
        return Task.FromResult(new FoundgineOperationResult());
    }
}

public sealed class ReserveStock() : OrderOperation("orders.reserve-stock")
{
    public override Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct) =>
        Task.FromResult(new FoundgineOperationResult($"{{\"reservation\":\"{c.IdempotencyKey}\"}}"));
}

public sealed class ReleaseStock() : OrderOperation("orders.release-stock")
{
    public override Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct) =>
        Task.FromResult(new FoundgineOperationResult());
}

public sealed class NoOp() : OrderOperation("orders.noop")
{
    public override Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct) =>
        Task.FromResult(new FoundgineOperationResult());
}

public sealed class ChargePayment() : OrderOperation("orders.charge-payment")
{
    public override Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct) =>
        Task.FromResult(new FoundgineOperationResult($"{{\"charge\":\"{c.IdempotencyKey}\"}}"));
}

public sealed class RefundPayment() : OrderOperation("orders.refund-payment")
{
    public override Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct) =>
        Task.FromResult(new FoundgineOperationResult());
}

public sealed class ConfirmOrder() : OrderOperation("orders.confirm")
{
    public override Task<FoundgineOperationResult> ExecuteAsync(FoundgineOperationContext c, CancellationToken ct) =>
        Task.FromResult(new FoundgineOperationResult());
}
