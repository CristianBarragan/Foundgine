package com.foundgine.runtime.controlplane.auditlog;

import java.util.List;

/**
 * Port of {@code Foundgine.Runtime.ControlPlane.AuditLog.IAuditLog}.
 *
 * <p>
 * <b>Porting decision:</b> the C# {@code Query} method takes two optional
 * ({@code string?} with default {@code null}) filter parameters. Java has no
 * default parameter values, so it is ported as three overloads narrowing to the
 * fully-specified {@link #query(String, String)}, mirroring the call shapes the
 * C# defaults allow ({@code Query()}, {@code Query(toolName)},
 * {@code Query(toolName, tenant)}).
 */
public interface IAuditLog {
	void record(AuditEvent auditEvent);

	List<AuditEvent> query(String toolName, String tenant);

	default List<AuditEvent> query(String toolName) {
		return query(toolName, null);
	}

	default List<AuditEvent> query() {
		return query(null, null);
	}
}
