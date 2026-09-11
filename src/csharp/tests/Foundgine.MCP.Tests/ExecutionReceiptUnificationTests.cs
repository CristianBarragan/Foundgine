using Foundgine.Core.Execution;
using Xunit;

namespace Foundgine.Providers.Tools.MCP.Tests;

public sealed class ExecutionReceiptUnificationTests
{
    [Fact]
    public void Canonical_receipt_can_represent_read_execution()
    {
        var evidence = new ExecutionEvidence(
            "sql",
            "plan-fp",
            new[] { 1, 2 },
            10,
            4,
            IntentFingerprint: "intent-fp",
            AuthorizationFingerprint: "auth-fp");

        var started = DateTimeOffset.UtcNow;
        var receipt = ExecutionReceiptFactory.Create(
            "req-read",
            evidence,
            "result-fp",
            new[] { 1, 2 },
            new[] { "read" },
            started,
            started.AddMilliseconds(4),
            1,
            1,
            1,
            1,
            "model-1");

        Assert.Equal("succeeded", receipt.Status);
        Assert.Null(receipt.ApprovalId);
        Assert.Equal("plan-fp", receipt.PlanFingerprint);
        Assert.Equal("result-fp", receipt.ResultFingerprint);
    }

    [Fact]
    public void Canonical_receipt_can_represent_approved_mutation_execution()
    {
        var evidence = new ExecutionEvidence(
            "postgres",
            "mutation-plan-fp",
            new[] { 8123, 55 },
            1,
            12,
            IntentFingerprint: "mutation-intent-fp",
            AuthorizationFingerprint: "mutation-auth-fp");

        var started = DateTimeOffset.UtcNow;
        var approved = started.AddSeconds(-1);

        var receipt = ExecutionReceiptFactory.Create(
            "req-write",
            evidence,
            "result-fp",
            new[] { 8123, 55 },
            new[] { "Order.update", "Payment.refund", "Audit.create" },
            started,
            started.AddMilliseconds(12),
            1,
            2,
            1,
            1,
            "model-2",
            "approval-1",
            "operator",
            approved);

        Assert.Equal("succeeded", receipt.Status);
        Assert.Equal("approval-1", receipt.ApprovalId);
        Assert.Equal("operator", receipt.ApprovedBy);
        Assert.Contains("Payment.refund", receipt.Effects);
        Assert.Equal(new[] { 55, 8123 }, receipt.AffectedNodeIds);
    }
}