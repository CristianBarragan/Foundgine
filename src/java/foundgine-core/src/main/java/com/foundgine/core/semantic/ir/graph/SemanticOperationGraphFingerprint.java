package com.foundgine.core.semantic.ir.graph;

import com.foundgine.core.abstractions.AuthorizationPredicate;
import com.foundgine.core.semantic.SemanticValue;
import com.foundgine.core.semantic.query.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

/** Deterministic SHA-256 identity for semantic operation shape and values. */
public final class SemanticOperationGraphFingerprint {
    private SemanticOperationGraphFingerprint(){}
    public static String create(SemanticOperationGraph graph){Objects.requireNonNull(graph);var b=new StringBuilder("foundgine.semantic-operation-graph.v1\n");appendNode(b,graph,graph.root());return sha256(b.toString());}
    private static void appendNode(StringBuilder b,SemanticOperationGraph g,SemanticOperationGraphNode n){
        append(b,"node",n.id(),n.entityId().value(),n.parentId()==null?"-":n.parentId(),n.viaRelationship()==null?"-":n.viaRelationship().value(),n.viaConnection()==null?"-":n.viaConnection().value());
        n.fields().forEach(f->append(b,"field",f.value())); n.requiredFields().forEach(f->append(b,"required",f.value())); appendQuery(b,n.queryOptions()); appendAuth(b,n.authorization()); n.children().forEach(c->append(b,"edge",n.id(),c)); n.children().forEach(c->appendNode(b,g,g.getNode(c)));
    }
    private static void appendQuery(StringBuilder b,SemanticQueryOptions o){if(o==null){append(b,"query","-");return;} append(b,"limit",o.limit()==null?"-":o.limit());append(b,"offset",o.offset()==null?"-":o.offset());append(b,"after",o.after()==null?"-":o.after());for(var x:o.effectiveOrder())append(b,"order",x.field().value(),x.direction().ordinal(),x.aggregate().ordinal(),x.path().stream().map(v->Long.toUnsignedString(v.value())).reduce((a,c)->a+"."+c).orElse(""));appendFilter(b,o.filter());}
    private static void appendFilter(StringBuilder b,SemanticFilterExpression f){
        if(f==null){append(b,"filter","-");return;}
        if(f instanceof SemanticFieldFilter x){append(b,"field-filter",x.field().value(),x.operator().ordinal(),valueText(x.value()));return;}
        if(f instanceof SemanticRelationshipFilter x){append(b,"relationship-filter",x.relationship().value(),x.quantifier().ordinal());appendFilter(b,x.predicate());return;}
        if(f instanceof SemanticAggregateFilter x){append(b,"aggregate-filter",x.relationship().value(),x.aggregate().ordinal(),x.field()==null?"-":x.field().value(),x.operator().ordinal(),valueText(x.value()));appendFilter(b,x.predicate());return;}
        if(f instanceof SemanticAndFilter x){append(b,"and",x.expressions().size());x.expressions().forEach(e->appendFilter(b,e));return;}
        if(f instanceof SemanticOrFilter x){append(b,"or",x.expressions().size());x.expressions().forEach(e->appendFilter(b,e));return;}
        throw new UnsupportedOperationException("Unsupported semantic filter '"+f.getClass().getName()+"'.");
    }
    private static String valueText(Object v){return SemanticValue.from(v).toString();}
    private static void appendAuth(StringBuilder b,AuthorizationPredicate p){if(p==null){append(b,"auth","-");return;}append(b,"auth",p.kind().ordinal(),p.name()==null?"-":p.name(),p.value()==null?"-":p.value());appendAuth(b,p.left());appendAuth(b,p.right());}
    private static void append(StringBuilder b,String kind,Object... vals){b.append(kind);for(var v:vals){var s=String.valueOf(v);b.append('|').append(s.length()).append(':').append(s);}b.append('\n');}
    private static String sha256(String s){try{var d=MessageDigest.getInstance("SHA-256").digest(s.getBytes(StandardCharsets.UTF_8));var b=new StringBuilder(64);for(byte x:d)b.append(String.format("%02x",x));return b.toString();}catch(Exception e){throw new IllegalStateException(e);}}
}
