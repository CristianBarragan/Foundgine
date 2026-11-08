using Foundgine.Extensions.Temporal.Configuration;
using Foundgine.Extensions.Temporal.Hosting;
using Foundgine.Providers.Outbox;

namespace Foundgine.Extensions.Temporal.Messaging;

/// <summary>
/// Bridges the Foundgine transactional outbox to Temporal. Register it as the dispatcher's
/// <see cref="IFoundgineOutboxPublisher"/>. The outbox message id becomes the workflow's business key, so
/// redelivery (the outbox is at-least-once) maps to the same workflow id and is absorbed instead of duplicated.
/// </summary>
public sealed class FoundgineTemporalOutboxPublisher(
    IFoundgineWorkflowStarter starter, FoundgineTemporalOptions options) : IFoundgineOutboxPublisher
{
    public const string InitiatorHeader = "initiator";

    public async ValueTask PublishAsync(FoundgineOutboxMessage message, CancellationToken cancellationToken = default)
    {
        if (!options.TopicRoutes.TryGetValue(message.Topic, out var process))
        {
            if (options.UnroutedTopicBehavior == UnroutedTopicBehavior.Fail)
                throw new InvalidOperationException($"No Temporal process is routed for outbox topic '{message.Topic}'.");
            return;
        }

        var initiator = message.Headers is not null && message.Headers.TryGetValue(InitiatorHeader, out var h) && !string.IsNullOrWhiteSpace(h)
            ? h
            : "system:outbox";

        await starter.StartAsync(new FoundgineWorkflowStartRequest(
            message.TenantId, initiator, process,
            BusinessKey: message.Id.ToString("N"),
            PayloadJson: message.PayloadJson,
            CorrelationId: message.CorrelationId,
            SourceMessageId: message.Id), cancellationToken).ConfigureAwait(false);
    }
}
