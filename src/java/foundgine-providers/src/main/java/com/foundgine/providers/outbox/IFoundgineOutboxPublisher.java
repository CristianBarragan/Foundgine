package com.foundgine.providers.outbox;

/** Broker adapter (Kafka, RabbitMQ, Service Bus, ...). Throw to signal failure; the dispatcher retries. */
@FunctionalInterface
public interface IFoundgineOutboxPublisher {
    void publish(FoundgineOutboxMessage message) throws Exception;
}
