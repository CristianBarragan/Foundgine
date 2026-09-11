package com.foundgine.runtime.routing;
import com.foundgine.core.semantic.security.execution.SecurityExecutionContext;
import com.foundgine.runtime.controlplane.risk.RiskScore;
import java.util.*;
public record RoutingContext(String toolName,SecurityExecutionContext security,RiskScore riskScore,Map<String,Object> hints){public RoutingContext{hints=Map.copyOf(hints==null?Map.of():hints);}public Optional<Object> hint(String key){return Optional.ofNullable(hints.get(key));}}
