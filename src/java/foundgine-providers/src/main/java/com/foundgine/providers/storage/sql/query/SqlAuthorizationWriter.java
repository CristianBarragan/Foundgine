package com.foundgine.providers.storage.sql.query;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.providers.storage.sql.SqlCompiler;
import java.math.BigDecimal;
import java.util.Collection;

/** Lowers the AOT authorization predicate IR into parameterized SQL. */
public final class SqlAuthorizationWriter {
    private SqlAuthorizationWriter() { }
    public static String write(AuthorizationPredicate predicate, EntityMetadata resource, String resourceAlias, Collection<SqlParameterBinding> parameters) {
        if (predicate == null || resource == null || parameters == null) throw new NullPointerException();
        int[] counter = {0}; return writeNode(predicate, resource, resourceAlias, parameters, counter);
    }
    private static String writeNode(AuthorizationPredicate node, EntityMetadata resource, String alias, Collection<SqlParameterBinding> parameters, int[] counter) {
        return switch (node.kind()) {
            case CONTEXT_PARAMETER -> throw new IllegalStateException("A context parameter must be followed by member access.");
            case RESOURCE_PARAMETER -> alias;
            case PARAMETER -> throw new UnsupportedOperationException("Untyped authorization parameters are not supported by the SQL provider.");
            case MEMBER_ACCESS -> writeMember(node, resource, alias, parameters, counter);
            case CONSTANT -> addConstant(node.value(), parameters, counter);
            case EQUAL -> binary(node, "=", resource, alias, parameters, counter);
            case NOT_EQUAL -> binary(node, "<>", resource, alias, parameters, counter);
            case AND -> binary(node, "AND", resource, alias, parameters, counter);
            case OR -> binary(node, "OR", resource, alias, parameters, counter);
            case NOT -> "NOT (" + required(node.left(), resource, alias, parameters, counter) + ")";
        };
    }
    private static String writeMember(AuthorizationPredicate node, EntityMetadata resource, String alias, Collection<SqlParameterBinding> parameters, int[] counter) {
        AuthorizationPredicate target = node.left(); if (target == null) throw new IllegalStateException("Member access has no target.");
        if (target.kind() == AuthorizationPredicateKind.RESOURCE_PARAMETER) {
            String name = node.name(); if (name == null) throw new IllegalStateException("Resource member has no name.");
            FieldMetadata field = resource.effectiveFields().stream().filter(x -> x.name().equals(name)).findFirst().orElse(null);
            if (field == null || field.column() == null) throw new IllegalArgumentException("Authorization resource member '" + resource.name() + "." + name + "' has no storage column mapping.");
            ColumnMetadata column = resource.columns().stream().filter(x -> x.id().equals(field.column().columnId())).findFirst()
                    .orElseThrow(() -> new IllegalArgumentException("Authorization resource member '" + resource.name() + "." + name + "' references a missing column."));
            return SqlCompiler.quoteIdentifier(alias) + "." + SqlCompiler.quoteIdentifier(column.effectiveStorageName());
        }
        if (target.kind() == AuthorizationPredicateKind.CONTEXT_PARAMETER) {
            String contextName = target.name(); if (contextName == null) throw new IllegalStateException("Context parameter has no name.");
            String member = node.name(); if (member == null) throw new IllegalStateException("Context member has no name.");
            String name = "auth" + counter[0]++; parameters.add(new SqlParameterBinding(name, null, null, contextName + "." + member, null)); return "@" + name;
        }
        throw new UnsupportedOperationException("Only direct resource and context member access is supported by the SQL authorization provider.");
    }
    private static String binary(AuthorizationPredicate node, String op, EntityMetadata resource, String alias, Collection<SqlParameterBinding> parameters, int[] counter) {
        return "(" + required(node.left(), resource, alias, parameters, counter) + " " + op + " " + required(node.right(), resource, alias, parameters, counter) + ")";
    }
    private static String addConstant(String value, Collection<SqlParameterBinding> parameters, int[] counter) { String name="auth"+counter[0]++; parameters.add(new SqlParameterBinding(name, parseConstant(value))); return "@"+name; }
    private static Object parseConstant(String value) {
        if (value == null || value.equals("null")) return null;
        if (value.equals("true")) return true; if(value.equals("false")) return false;
        try { return Integer.parseInt(value); } catch(Exception ignored) {}
        try { return Long.parseLong(value); } catch(Exception ignored) {}
        try { return new BigDecimal(value); } catch(Exception ignored) {}
        return value;
    }
    private static String required(AuthorizationPredicate node, EntityMetadata resource, String alias, Collection<SqlParameterBinding> parameters, int[] counter) { if(node==null) throw new IllegalStateException("Authorization predicate node is incomplete."); return writeNode(node,resource,alias,parameters,counter); }
}
