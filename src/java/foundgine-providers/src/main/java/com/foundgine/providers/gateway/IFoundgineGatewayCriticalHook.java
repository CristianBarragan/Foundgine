package com.foundgine.providers.gateway;

/**
 * Marks a hook whose success is required before an event reaches the journal, other hooks or subscribers.
 * A failing critical hook fails the publishing operation (fail-closed).
 */
public interface IFoundgineGatewayCriticalHook extends IFoundgineGatewayHook { }
