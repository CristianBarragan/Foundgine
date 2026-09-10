package com.foundgine.core.semantic.planning;
public record RewriteRuleCandidate(String ruleName,double benefitEstimate,double costImpact,double score,int priority) {}
