package com.foundgine.runtime.routing;

import java.util.*;

public final class DefaultRoutingEngine implements IRoutingEngine {
	private final List<IRoutingRule> rules;

	public DefaultRoutingEngine(Collection<? extends IRoutingRule> rules) {
		this.rules = List.copyOf(rules == null ? List.of() : rules);
	}

	public DefaultRoutingEngine() {
		this(null);
	}

	public TaskContract route(RoutingContext context) {
		Objects.requireNonNull(context, "context");
		for (var rule : rules) {
			var d = rule.tryRoute(context);
			if (d != null && d.matched() && d.contract() != null)
				return d.contract();
		}
		return TaskContract.defaults(UUID.randomUUID().toString().replace("-", ""));
	}
}
