package com.foundgine.providers.storage.orm;

import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.Set;

/** Enforces bounded, acyclic predicate graphs before any provider compiles or evaluates them. */
public final class OrmPredicateValidator {
    public static final int MAX_NODES = 256;
    public static final int MAX_RELATIONSHIP_DEPTH = 8;

    private OrmPredicateValidator() {}

    public static void validate(OrmPredicate predicate) {
        Set<OrmPredicate> active = Collections.newSetFromMap(new IdentityHashMap<>());
        visit(predicate, active, 0, new int[] {0});
    }

    private static void visit(OrmPredicate predicate, Set<OrmPredicate> active,
            int relationshipDepth, int[] nodes) {
        if (predicate == null) throw new IllegalArgumentException("ORM predicate cannot be null.");
        if (++nodes[0] > MAX_NODES) {
            throw new IllegalArgumentException("ORM predicate exceeds the maximum of " + MAX_NODES + " nodes.");
        }
        if (!active.add(predicate)) {
            throw new IllegalArgumentException("Cyclic ORM predicate graph is not supported.");
        }
        try {
            if (predicate instanceof OrmPredicate.And and) {
                and.terms().forEach(term -> visit(term, active, relationshipDepth, nodes));
            } else if (predicate instanceof OrmPredicate.Or or) {
                or.terms().forEach(term -> visit(term, active, relationshipDepth, nodes));
            } else if (predicate instanceof OrmPredicate.Not not) {
                visit(not.term(), active, relationshipDepth, nodes);
            } else if (predicate instanceof OrmPredicate.Exists exists) {
                visitRelationship(exists.authorization(), exists.where(), active, relationshipDepth, nodes);
            } else if (predicate instanceof OrmPredicate.Quantified quantified) {
                visitRelationship(quantified.authorization(), quantified.where(), active, relationshipDepth, nodes);
            } else if (predicate instanceof OrmPredicate.Aggregate aggregate) {
                visitRelationship(aggregate.authorization(), aggregate.where(), active, relationshipDepth, nodes);
            }
        } finally {
            active.remove(predicate);
        }
    }

    private static void visitRelationship(OrmPredicate authorization, OrmPredicate where,
            Set<OrmPredicate> active, int relationshipDepth, int[] nodes) {
        int nextDepth = relationshipDepth + 1;
        if (nextDepth > MAX_RELATIONSHIP_DEPTH) {
            throw new IllegalArgumentException("ORM relationship traversal exceeds maximum depth of "
                    + MAX_RELATIONSHIP_DEPTH + ".");
        }
        visit(authorization, active, nextDepth, nodes);
        visit(where, active, nextDepth, nodes);
    }
}
