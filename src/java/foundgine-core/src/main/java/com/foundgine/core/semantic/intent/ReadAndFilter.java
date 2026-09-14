package com.foundgine.core.semantic.intent;

import java.util.*;

public record ReadAndFilter(List<ReadFilter> expressions) implements ReadFilter {
	public ReadAndFilter {
		expressions = List.copyOf(expressions == null ? List.of() : expressions);
	}
}
