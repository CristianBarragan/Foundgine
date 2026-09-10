package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.*;

/** Canonical provider-independent SHA-256 fingerprint of a semantic model. */
public final class SemanticModelFingerprint {
    private SemanticModelFingerprint() {}
    public static String compute(SemanticModel model) {
        Objects.requireNonNull(model);
        var canonical=new StringBuilder("foundgine.semantic-contract.v1\n");
        model.entities().stream().sorted(Comparator.comparingLong(e -> e.id().value())).forEach(e -> {
            append(canonical,"entity",e.id().value()); append(canonical,"name",e.name());
            append(canonical,"identity",e.identity().fieldId().value(),e.identity().name()); appendAliases(canonical,e.effectiveAliases());
            e.fields().stream().sorted(Comparator.comparingLong(f -> f.id().value())).forEach(f -> {
                append(canonical,"field",f.id().value()); append(canonical,"field-name",f.name()); append(canonical,"field-type",canonicalType(f.effectiveSemanticType()));
                append(canonical,"field-capabilities",f.capabilities()); append(canonical,"field-nullable",f.isNullable()); appendAliases(canonical,f.effectiveAliases());
                f.effectiveConstraints().stream().sorted(Comparator.comparing((SemanticConstraint c)->c.kind().ordinal()).thenComparing(c->Objects.toString(c.value(),"")).thenComparing(c->Objects.toString(c.minimum(),"")).thenComparing(c->Objects.toString(c.maximum(),""))).forEach(c -> append(canonical,"constraint",c.kind().ordinal(),Objects.toString(c.value(),""),decimal(c.minimum()),decimal(c.maximum())));
            });
            e.relationships().stream().sorted(Comparator.comparingLong(r -> r.id().value())).forEach(r -> { append(canonical,"relationship",r.id().value(),r.name(),r.target().value(),r.cardinality().ordinal()); appendAliases(canonical,r.effectiveAliases()); });
        });
        model.traversals().stream().sorted(Comparator.comparingLong((SemanticTraversal t)->t.source().value()).thenComparing(SemanticTraversal::name)).forEach(t -> { append(canonical,"traversal",t.source().value(),t.name(),t.target().value()); t.path().forEach(r -> append(canonical,"traversal-edge",r.value())); });
        try { var d=MessageDigest.getInstance("SHA-256"); var bytes=d.digest(canonical.toString().getBytes(StandardCharsets.UTF_8)); var out=new StringBuilder(64); for(byte b:bytes) out.append(String.format("%02x",b)); return out.toString(); } catch(Exception ex){ throw new IllegalStateException(ex); }
    }
    private static String decimal(java.math.BigDecimal v){ return v==null?"":v.stripTrailingZeros().toPlainString(); }
    private static void appendAliases(StringBuilder b,List<SemanticAlias> a){ a.stream().sorted(Comparator.comparing(SemanticAlias::name,String.CASE_INSENSITIVE_ORDER).thenComparing(SemanticAlias::name)).forEach(x->append(b,"alias",x.name(),x.weight()==null?"":x.weight())); }
    private static void append(StringBuilder b,String kind,Object... values){ b.append(kind); for(Object v:values){String s=String.valueOf(v); b.append('|').append(s.length()).append(':').append(s);} b.append('\n'); }
    private static String canonicalType(SemanticType t){ return switch(t){ case SemanticType.Scalar x -> "scalar:"+x.kind(); case SemanticType.EnumType x -> "enum:"+x.name(); case SemanticType.ObjectType x -> "object:"+x.name(); case SemanticType.CollectionType x -> "collection:"+canonicalType(x.elementType()); }; }
}
