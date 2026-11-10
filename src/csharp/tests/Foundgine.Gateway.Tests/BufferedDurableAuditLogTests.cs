using Foundgine.Runtime.ControlPlane.AuditLog;
using Xunit;

namespace Foundgine.Gateway.Tests;

public sealed class BufferedDurableAuditLogTests
{
    private sealed class Store : IAuditEventStore
    {
        public readonly List<AuditEvent> Events = [];
        public bool Fail;
        public ValueTask AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("sink unavailable");
            lock (Events) Events.Add(auditEvent);
            return ValueTask.CompletedTask;
        }
    }

    private static AuditEvent Event(string name) => AuditEvent.Create(
        AuditCategory.Routed, name, "actor", "tenant", "fp", "summary");

    [Fact]
    public async Task RecordAsync_CompletesOnlyAfterDurableSinkAcknowledges()
    {
        var store = new Store();
        await using var log = new BufferedDurableAuditLog(store, 1);
        var item = Event("tool-a");
        await log.RecordAsync(item);
        Assert.Single(store.Events);
        Assert.Single(log.Query("tool-a", "tenant"));
        Assert.Equal(1, log.Metrics.Accepted);
        Assert.Equal(1, log.Metrics.Persisted);
        Assert.Equal(0, log.Metrics.Failed);
    }

    [Fact]
    public async Task SinkFailure_IsReturnedToCallerAndEventIsNotReportedAsCommitted()
    {
        var store = new Store { Fail = true };
        await using var log = new BufferedDurableAuditLog(store, 1);
        await Assert.ThrowsAsync<IOException>(() => log.RecordAsync(Event("tool-b")).AsTask());
        Assert.Empty(log.Query());
        Assert.Equal(1, log.Metrics.Failed);
        Assert.Equal(0, log.Metrics.Persisted);
    }

    [Fact]
    public async Task DisposeAsync_DrainsAcceptedEventsBeforeReturning()
    {
        var store = new Store();
        var log = new BufferedDurableAuditLog(store, 2);
        await log.RecordAsync(Event("one"));
        await log.RecordAsync(Event("two"));
        await log.DisposeAsync();
        Assert.Equal(2, store.Events.Count);
    }
}
