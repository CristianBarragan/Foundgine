package com.foundgine.core.semantic;

public record SemanticConstraint(SemanticConstraintKind kind, String value, java.math.BigDecimal minimum, java.math.BigDecimal maximum) {
    public static SemanticConstraint range(java.math.BigDecimal min, java.math.BigDecimal max) { return new SemanticConstraint(SemanticConstraintKind.RANGE, null, min, max); }
    public static SemanticConstraint pattern(String pattern) { return new SemanticConstraint(SemanticConstraintKind.PATTERN, pattern, null, null); }
    public static SemanticConstraint temporal(String semantics) { return new SemanticConstraint(SemanticConstraintKind.TEMPORAL, semantics, null, null); }
    public static SemanticConstraint currency(String code) { return new SemanticConstraint(SemanticConstraintKind.CURRENCY, code, null, null); }
    public static SemanticConstraint countryCode(String code) { return new SemanticConstraint(SemanticConstraintKind.COUNTRY_CODE, code, null, null); }
}
