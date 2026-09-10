package com.foundgine.core.semantic;

import com.foundgine.core.abstractions.*;
import java.util.*;

/** Model-independent semantic entity builder. */
public final class SemanticEntityBuilder {
    private final EntityId id; private final String name;
    private final List<SemanticField> fields=new ArrayList<>(); private final List<SemanticRelationship> relationships=new ArrayList<>(); private final List<SemanticAlias> aliases=new ArrayList<>(); private SemanticFieldIdentity identity;
    public SemanticEntityBuilder(EntityId id,String name){this.id=Objects.requireNonNull(id);if(name==null||name.isBlank())throw new IllegalArgumentException("name cannot be blank");this.name=name;}
    public SemanticEntityBuilder identity(FieldId fieldId,String semanticName){identity=new SemanticFieldIdentity(fieldId,semanticName);return this;}
    public SemanticEntityBuilder identity(String semanticName){return identity(FieldId.create(name,semanticName),semanticName);}
    public SemanticEntityBuilder alias(String alias){return alias(alias,null);}
    public SemanticEntityBuilder alias(String alias,Integer weight){addAlias(aliases,alias,weight);return this;}
    public SemanticEntityBuilder aliases(String... values){for(var a:values)alias(a);return this;}
    public SemanticEntityBuilder field(FieldId id,String name,Class<?> clrType){return field(id,name,clrType,null,SemanticFieldCapabilities.DEFAULT);}
    public SemanticEntityBuilder field(FieldId id,String name,Class<?> clrType,SemanticType semanticType,byte capabilities){fields.add(new SemanticField(id,name,clrType,semanticType,capabilities,List.of(),List.of(),null));return this;}
    public SemanticEntityBuilder constraint(FieldId fieldId,SemanticConstraint constraint){var i=indexField(fieldId);var f=fields.get(i);var c=new ArrayList<>(f.effectiveConstraints());c.add(constraint);fields.set(i,new SemanticField(f.id(),f.name(),f.clrType(),f.semanticType(),f.capabilities(),f.aliases(),c,f.nullableOverride()));return this;}
    public SemanticEntityBuilder fieldAlias(FieldId fieldId,String alias){return fieldAlias(fieldId,alias,null);}
    public SemanticEntityBuilder fieldAlias(FieldId fieldId,String alias,Integer weight){var i=indexField(fieldId);var f=fields.get(i);var a=new ArrayList<>(f.effectiveAliases());a.add(new SemanticAlias(alias,weight));fields.set(i,new SemanticField(f.id(),f.name(),f.clrType(),f.semanticType(),f.capabilities(),a,f.constraints(),f.nullableOverride()));return this;}
    public SemanticEntityBuilder fieldAliases(FieldId fieldId,String... values){for(var a:values)fieldAlias(fieldId,a);return this;}
    public SemanticEntityBuilder relationship(RelationshipId id,String name,EntityId target,RelationshipCardinality cardinality){relationships.add(new SemanticRelationship(id,name,target,cardinality));return this;}
    public SemanticEntityBuilder relationship(String name,EntityId target,RelationshipCardinality cardinality){return relationship(RelationshipId.create(this.name,name),name,target,cardinality);}
    public SemanticEntityBuilder relationshipAlias(RelationshipId id,String alias){return relationshipAlias(id,alias,null);}
    public SemanticEntityBuilder relationshipAlias(RelationshipId id,String alias,Integer weight){var i=indexRelationship(id);var r=relationships.get(i);var a=new ArrayList<>(r.effectiveAliases());a.add(new SemanticAlias(alias,weight));relationships.set(i,new SemanticRelationship(r.id(),r.name(),r.target(),r.cardinality(),a));return this;}
    public SemanticEntityBuilder relationshipAliases(RelationshipId id,String... values){for(var a:values)relationshipAlias(id,a);return this;}
    SemanticEntity build(){if(identity==null)throw new IllegalStateException("Semantic entity '"+name+"' must declare an identity.");return new SemanticEntity(id,name,identity,fields,relationships,aliases,null);}
    private int indexField(FieldId id){for(int i=0;i<fields.size();i++)if(fields.get(i).id().equals(id))return i;throw new IllegalStateException("Field '"+id+"' is not declared on '"+name+"'.");}
    private int indexRelationship(RelationshipId id){for(int i=0;i<relationships.size();i++)if(relationships.get(i).id().equals(id))return i;throw new IllegalStateException("Relationship '"+id+"' is not declared on '"+name+"'.");}
    private static void addAlias(List<SemanticAlias> aliases,String alias,Integer weight){if(alias==null||alias.isBlank())throw new IllegalArgumentException("alias cannot be blank");if(aliases.stream().anyMatch(x->x.name().equalsIgnoreCase(alias)))throw new IllegalArgumentException("Duplicate semantic alias '"+alias+"'.");aliases.add(new SemanticAlias(alias,weight));}
}
