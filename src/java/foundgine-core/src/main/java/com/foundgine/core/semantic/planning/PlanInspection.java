package com.foundgine.core.semantic.planning;

import java.util.*;

/** Provider-neutral description of an authorized plan suitable for inspection before execution. */
public record PlanInspection(SemanticPlan plan,String planFingerprint,List<PlanInspectionNode> nodes,PlanEffectSummary effects) {}
