package com.foundgine.core.semantic.expressions;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;
import com.foundgine.core.semantic.*;
import java.util.*;

/** Common provider-independent semantic expression calculus. */
public interface SemanticExpression {
	SemanticType resultType();

	record Literal(SemanticValue value, SemanticType type) implements SemanticExpression {
		public SemanticType resultType() {
			return type;
		}
	}

	record FieldReference(FieldId field, SemanticType type) implements SemanticExpression {
		public SemanticType resultType() {
			return type;
		}
	}

	record RelationshipReference(RelationshipId relationship, SemanticType type) implements SemanticExpression {
		public SemanticType resultType() {
			return type;
		}
	}

	record Path(SemanticExpression source, List<RelationshipId> relationships, SemanticType type)
			implements SemanticExpression {
		public Path {
			relationships = List.copyOf(relationships);
		}

		public SemanticType resultType() {
			return type;
		}
	}

	record Unary(String operator, SemanticExpression operand, SemanticType type) implements SemanticExpression {
		public SemanticType resultType() {
			return type;
		}
	}

	record Binary(String operator, SemanticExpression left, SemanticExpression right, SemanticType type)
			implements SemanticExpression {
		public SemanticType resultType() {
			return type;
		}
	}

	record Logical(LogicalOperator operator, List<SemanticExpression> operands) implements SemanticExpression {
		public Logical {
			operands = List.copyOf(operands);
		}

		public SemanticType resultType() {
			return SemanticExpressionTypes.BOOLEAN;
		}
	}

	record Aggregate(AggregateExpressionKind aggregate, SemanticExpression source, SemanticExpression argument)
			implements SemanticExpression {
		public SemanticType resultType() {
			return switch (aggregate) {
			case COUNT -> SemanticExpressionTypes.INT64;
			case MIN, MAX, SUM, AVERAGE -> argument != null ? argument.resultType() : source.resultType();
			};
		}
	}

	record Function(String name, List<SemanticExpression> arguments, SemanticType type) implements SemanticExpression {
		public Function {
			arguments = List.copyOf(arguments);
		}

		public SemanticType resultType() {
			return type;
		}
	}

	enum LogicalOperator {
		AND, OR
	}

	enum AggregateExpressionKind {
		COUNT, MIN, MAX, SUM, AVERAGE
	}

	final class SemanticExpressionTypes {
		private SemanticExpressionTypes() {
		}

		public static final SemanticType BOOLEAN = new SemanticType.Scalar(SemanticScalarKind.BOOLEAN);
		public static final SemanticType INT64 = new SemanticType.Scalar(SemanticScalarKind.INT64);
	}
}
