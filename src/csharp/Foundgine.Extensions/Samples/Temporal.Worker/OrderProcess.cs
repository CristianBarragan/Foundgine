using Foundgine.Extensions.Temporal;

/// <summary>Sales-order process: validate → reserve stock → (approval for large orders) → charge → confirm, with compensation.</summary>
public static class OrderProcess
{
    public const string Name = "sales-order";

    public static FoundgineProcessDefinition Definition { get; } = new(Name,
    [
        new("validate",  "orders.validate"),
        new("reserve",   "orders.reserve-stock", CompensationOperation: "orders.release-stock"),
        new("approve",   "orders.noop", RequiresApproval: true, ApprovalTimeout: TimeSpan.FromDays(2)),
        new("charge",    "orders.charge-payment", CompensationOperation: "orders.refund-payment"),
        new("confirm",   "orders.confirm")
    ]);
}
