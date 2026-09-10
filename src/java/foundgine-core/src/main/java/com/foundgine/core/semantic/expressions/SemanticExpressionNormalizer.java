package com.foundgine.core.semantic.expressions;

import com.foundgine.core.semantic.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

/** Conservative semantics-preserving expression normalization. */
public final class SemanticExpressionNormalizer {
    private SemanticExpressionNormalizer() {}
    public static SemanticExpression normalize(SemanticExpression e) {
        Objects.requireNonNull(e);
        return switch (e) {
            case SemanticExpression.Logical x -> normalizeLogical(x);
            case SemanticExpression.Unary x -> new SemanticExpression.Unary(x.operator(), normalize(x.operand()), x.type());
            case SemanticExpression.Binary x -> new SemanticExpression.Binary(x.operator(), normalize(x.left()), normalize(x.right()), x.type());
            case SemanticExpression.Path x -> new SemanticExpression.Path(normalize(x.source()), x.relationships(), x.type());
            case SemanticExpression.Aggregate x -> new SemanticExpression.Aggregate(x.aggregate(), normalize(x.source()), x.argument()==null?null:normalize(x.argument()));
            case SemanticExpression.Function x -> new SemanticExpression.Function(x.name(), x.arguments().stream().map(SemanticExpressionNormalizer::normalize).toList(), x.type());
            default -> e;
        };
    }
    public static String canonicalize(SemanticExpression e) { return write(normalize(e)); }
    public static String hash(SemanticExpression e) { try { var d=MessageDigest.getInstance("SHA-256"); var b=d.digest(canonicalize(e).getBytes(StandardCharsets.UTF_8)); var sb=new StringBuilder(); for(byte x:b) sb.append(String.format("%02x",x)); return sb.toString(); } catch(Exception ex){ throw new IllegalStateException(ex); } }
    private static SemanticExpression normalizeLogical(SemanticExpression.Logical x) {
        var children=new ArrayList<SemanticExpression>();
        for(var operand:x.operands().stream().map(SemanticExpressionNormalizer::normalize).toList()) {
            if(operand instanceof SemanticExpression.Logical nested && nested.operator()==x.operator()) children.addAll(nested.operands()); else if(!isIdentity(x.operator(),operand)) children.add(operand);
        }
        children.sort(Comparator.comparing(SemanticExpressionNormalizer::write));
        var distinct=children.stream().distinct().toList();
        if(distinct.isEmpty()) return new SemanticExpression.Literal(SemanticValue.from(x.operator()==SemanticExpression.LogicalOperator.AND), SemanticExpression.SemanticExpressionTypes.BOOLEAN);
        if(distinct.size()==1) return distinct.get(0);
        return new SemanticExpression.Logical(x.operator(), distinct);
    }
    private static boolean isIdentity(SemanticExpression.LogicalOperator op, SemanticExpression e) { return e instanceof SemanticExpression.Literal l && l.value().kind()==SemanticValueKind.BOOLEAN && l.value().value() instanceof Boolean b && ((op==SemanticExpression.LogicalOperator.AND && b)||(op==SemanticExpression.LogicalOperator.OR&&!b)); }
    private static String write(SemanticExpression e) {
        return switch(e) {
            case SemanticExpression.Literal x -> "lit:"+x.value().kind()+":"+x.value();
            case SemanticExpression.FieldReference x -> "field:"+x.field().value()+":"+x.type();
            case SemanticExpression.RelationshipReference x -> "rel:"+x.relationship().value()+":"+x.type();
            case SemanticExpression.Path x -> "path:"+write(x.source())+"("+x.relationships().stream().map(Object::toString).reduce((a,b)->a+","+b).orElse("")+"):"+x.type();
            case SemanticExpression.Unary x -> "unary:"+x.operator()+"("+write(x.operand())+")";
            case SemanticExpression.Binary x -> "binary:"+x.operator()+"("+write(x.left())+","+write(x.right())+")";
            case SemanticExpression.Logical x -> "logical:"+x.operator()+"("+x.operands().stream().map(SemanticExpressionNormalizer::write).reduce((a,b)->a+","+b).orElse("")+")";
            case SemanticExpression.Aggregate x -> "aggregate:"+x.aggregate()+"("+write(x.source())+","+(x.argument()==null?"":write(x.argument()))+")";
            case SemanticExpression.Function x -> "function:"+x.name()+"("+x.arguments().stream().map(SemanticExpressionNormalizer::write).reduce((a,b)->a+","+b).orElse("")+"):"+x.type();
            default -> throw new IllegalStateException("Unsupported semantic expression '"+e.getClass().getSimpleName()+"'.");
        };
    }
}
