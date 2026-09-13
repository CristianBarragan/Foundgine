package com.foundgine.core.semantic.query;

import com.foundgine.core.semantic.*;
import java.util.*;

public final class SemanticValueValidator {
	private SemanticValueValidator() {
	}

	public static void validate(Object value, SemanticField field, String operation) {
		if (value == null)
			return;
		if (value instanceof Iterable<?> it && operation.equalsIgnoreCase("IN")) {
			for (var x : it)
				validateSingle(x, field, operation);
			return;
		}
		validateSingle(value, field, operation);
	}

	private static void validateSingle(Object value, SemanticField field, String operation) {
		if (value == null)
			return;
		var actual = SemanticType.fromClass(value.getClass());
		var expected = field.effectiveSemanticType();
		if (compatible(expected, actual))
			return;
		throw new IllegalStateException("Semantic " + operation + " value for field '" + field.name() + "' has type '"
				+ actual + "', but the field requires '" + expected + "'.");
	}

	private static boolean compatible(SemanticType e, SemanticType a) {
		if (e.equals(a))
			return true;
		if (e instanceof SemanticType.Scalar es && a instanceof SemanticType.Scalar as) {
			if ((es.kind() == SemanticScalarKind.INT32 || es.kind() == SemanticScalarKind.INT64)
					&& (as.kind() == SemanticScalarKind.INT32 || as.kind() == SemanticScalarKind.INT64))
				return true;
			if (es.kind() == SemanticScalarKind.DECIMAL && (as.kind() == SemanticScalarKind.INT32
					|| as.kind() == SemanticScalarKind.INT64 || as.kind() == SemanticScalarKind.DECIMAL))
				return true;
		}
		return false;
	}
}
