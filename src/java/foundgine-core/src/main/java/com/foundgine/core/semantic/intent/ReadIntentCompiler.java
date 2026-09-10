package com.foundgine.core.semantic.intent;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.ir.*;
import com.foundgine.core.semantic.ir.graph.SemanticOperationGraph;
import com.foundgine.core.semantic.query.*;
import com.foundgine.core.semantic.resolution.SemanticRequestResolver;
import java.util.*;

/** Compiles external structured read intent into canonical provider-neutral semantic IR. */
public final class ReadIntentCompiler {
    private final SemanticModel model;
    private final SemanticContractSnapshot contract;

    public ReadIntentCompiler(SemanticModel model) { this.model=Objects.requireNonNull(model); this.contract=null; }
    public ReadIntentCompiler(SemanticContractSnapshot contract) { this.contract=Objects.requireNonNull(contract); this.model=null; }

    public String contractFingerprint() {
        if (contract != null) return contract.contractFingerprint();
        return model.freeze().contractFingerprint();
    }

    private SemanticContractSnapshot snapshot() {
        if (contract != null) return contract;
        return new SemanticContractSnapshot(model.freeze());
    }

    public SemanticRequest compile(ReadIntent intent) {
        Objects.requireNonNull(intent);
        var c=snapshot();
        var root=findEntity(c,intent.rootEntity());
        var selections=intent.selections().stream().map(s->compileSelection(c,root,s)).toList();
        var filter=intent.filter()==null?null:compileFilter(c,root,intent.filter());
        var order=intent.order().stream().map(o->compileOrder(c,root,o)).toList();
        return new SemanticRequest(root.id(),selections,new SemanticQueryOptions(filter,order,intent.limit(),intent.offset(),intent.after()),intent.security());
    }

    public SemanticOperationGraph compileOperationGraph(ReadIntent intent) {
        var request=compile(intent);
        var graph=new SemanticRequestResolver(snapshot()).resolve(request);
        return SemanticOperationGraph.create(SemanticOperationCompiler.compile(graph));
    }

    public SemanticIntentDocument createDocument(ReadIntent intent) { return new SemanticIntentDocument(contractFingerprint(),intent).validate(); }

    public SemanticIntentResolution resolveDocument(SemanticIntentDocument document) {
        Objects.requireNonNull(document).validate();
        if (!document.contractFingerprint().equals(contractFingerprint()))
            throw new IllegalArgumentException("Semantic intent document is bound to contract '"+document.contractFingerprint()+"', but the resolver uses '"+contractFingerprint()+"'.");
        return new SemanticIntentResolution(document,compile(document.intent()),contractFingerprint());
    }

    public SemanticOperationGraph resolveDocumentGraph(SemanticIntentDocument document) { return compileOperationGraph(resolveDocument(document).document().intent()); }

    private SemanticSelection compileSelection(SemanticContractSnapshot c,SemanticEntity entity,ReadSelection selection) {
        if ((selection.field()==null)==(selection.relationship()==null)) throw invalid("A selection must specify exactly one field or relationship.");
        if (selection.field()!=null) {
            var f=findField(entity,selection.field());
            if (!selection.effectiveChildren().isEmpty()) throw invalid("Field '"+entity.name()+"."+f.name()+"' cannot have children.");
            return new SemanticSelection(f.id(),null,List.of());
        }
        if (selection.effectiveChildren().isEmpty()) throw invalid("Relationship '"+entity.name()+"."+selection.relationship()+"' requires child selections.");
        var path=resolveRelationshipPath(c,entity,selection.relationship());
        var children=selection.effectiveChildren().stream().map(x->compileSelection(c,c.get(path.get(path.size()-1).target()),x)).toList();
        for(int i=path.size()-1;i>=0;i--) children=List.of(new SemanticSelection(null,path.get(i).id(),children));
        return children.get(0);
    }

    private SemanticFilterExpression compileFilter(SemanticContractSnapshot c,SemanticEntity entity,ReadFilter filter) {
        if(filter instanceof ReadFieldFilter f) return new SemanticFieldFilter(findField(entity,f.field()).id(),f.operator(),f.value());
        if(filter instanceof ReadRelationshipFilter r) {
            var path=resolveRelationshipPath(c,entity,r.relationship());
            if(path.size()>1 && r.quantifier()!=SemanticRelationshipQuantifier.SOME) throw invalid("Logical traversal filter currently supports only SOME quantification across multi-hop paths.");
            SemanticFilterExpression p=compileFilter(c,c.get(path.get(path.size()-1).target()),r.predicate());
            for(int i=path.size()-1;i>=0;i--) p=new SemanticRelationshipFilter(path.get(i).id(),i==path.size()-1?r.quantifier():SemanticRelationshipQuantifier.SOME,p);
            return p;
        }
        if(filter instanceof ReadAndFilter a) { if(a.expressions().isEmpty()) throw invalid("AND filter cannot be empty."); return new SemanticAndFilter(a.expressions().stream().map(x->compileFilter(c,entity,x)).toList()); }
        if(filter instanceof ReadOrFilter o) { if(o.expressions().isEmpty()) throw invalid("OR filter cannot be empty."); return new SemanticOrFilter(o.expressions().stream().map(x->compileFilter(c,entity,x)).toList()); }
        throw invalid("Unsupported read filter '"+filter.getClass().getSimpleName()+"'.");
    }

    private SemanticOrderTerm compileOrder(SemanticContractSnapshot c,SemanticEntity root,ReadOrder order) {
        var entity=root; var path=new ArrayList<RelationshipId>();
        for(var name:order.effectivePath()) for(var r:resolveRelationshipPath(c,entity,name)){path.add(r.id());entity=c.get(r.target());}
        var field=order.aggregate()==SemanticOrderAggregate.COUNT?entity.identity().fieldId():findField(entity,order.field()).id();
        return new SemanticOrderTerm(field,order.direction(),path,order.aggregate());
    }

    private SemanticEntity findEntity(SemanticContractSnapshot c,String name){ try{return c.resolveEntity(name);}catch(Exception e){throw invalid("Unknown entity '"+name+"'.");} }
    private static SemanticField findField(SemanticEntity e,String name){return e.fields().stream().filter(f->f.name().equalsIgnoreCase(name)||f.effectiveAliases().stream().anyMatch(a->a.name().equalsIgnoreCase(name))).findFirst().orElseGet(()->{if(e.identity().name().equalsIgnoreCase(name))return new SemanticField(e.identity().fieldId(),e.identity().name(),Object.class);throw invalid("Unknown field '"+e.name()+"."+name+"'.");});}
    private List<SemanticRelationship> resolveRelationshipPath(SemanticContractSnapshot c,SemanticEntity entity,String name){
        var direct=entity.relationships().stream().filter(r->r.name().equalsIgnoreCase(name)||r.effectiveAliases().stream().anyMatch(a->a.name().equalsIgnoreCase(name))).findFirst();
        if(direct.isPresent()) return List.of(direct.get());
        var t=c.traversals().stream().filter(x->x.source().equals(entity.id())&&x.name().equalsIgnoreCase(name)).findFirst().orElseThrow(()->invalid("Unknown relationship or semantic traversal '"+entity.name()+"."+name+"'."));
        var current=entity; var result=new ArrayList<SemanticRelationship>();
        for(var id:t.path()){final var currentEntity=current;var r=current.relationships().stream().filter(x->x.id().equals(id)).findFirst().orElseThrow(()->invalid("Traversal '"+name+"' contains relationship '"+id+"' that is not defined on '"+currentEntity.name()+"'."));result.add(r);current=c.get(r.target());}
        return result;
    }
    private static IllegalArgumentException invalid(String message){return new IllegalArgumentException("Invalid read intent: "+message);}
}
