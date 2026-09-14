package com.foundgine.runtime;

/**
 * Port of {@code Foundgine.Runtime.IFoundgineCapability}.
 *
 * <p>
 * A named, self-contained unit of runtime configuration. Java has no C#
 * static-abstract interface members, so capabilities are ordinary strategy
 * objects and are configured through {@link FoundgineOptions#enable}.
 */
@FunctionalInterface
public interface IFoundgineCapability {
	void configure(FoundgineCapabilityContext context);
}
