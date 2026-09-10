package com.foundgine.core.semantic.metadata;
import com.foundgine.core.semantic.*; import java.util.*;
/** Bridges structural metadata into the provider-neutral semantic model. */
public final class SemanticModelDiscovery {
 private SemanticModelDiscovery(){}
 public static SemanticModel discover(IMetadataCatalog metadata){Objects.requireNonNull(metadata);var entities=new LinkedHashMap<com.foundgine.core.abstractions.EntityId,SemanticEntity>();
  for(var item:metadata.entities()){
   var fields=new ArrayList<SemanticField>();for(var f:item.effectiveFields())fields.add(new SemanticField(f.id(),f.name(),f.clrType(),null,SemanticFieldCapabilities.DEFAULT,f.effectiveAliases().stream().map(a->new SemanticAlias(a.name(),a.weight())).toList(),List.of(),null));
   SemanticField primary=null;if(item.primaryKey()!=null)for(var f:item.effectiveFields())if(f.column()!=null&&f.column().columnId().equals(item.primaryKey().columnId())){primary=fields.stream().filter(x->x.id().equals(f.id())).findFirst().orElse(null);break;}
   if(primary==null)throw new IllegalStateException("Metadata entity '"+item.name()+"' has no field corresponding to its primary key.");
   entities.put(item.entityId(),new SemanticEntity(item.entityId(),item.name(),new SemanticFieldIdentity(primary.id(),primary.name()),fields,List.of(),item.aliases().stream().map(a->new SemanticAlias(a.name(),a.weight())).toList(),item.clrType()));
  }
  for(var r:metadata.relationships()){var s=entities.get(r.source());if(s==null)throw new IllegalStateException("Relationship '"+r.name()+"' references unknown source entity '"+r.source()+"'.");if(!entities.containsKey(r.target()))throw new IllegalStateException("Relationship '"+r.name()+"' references unknown target entity '"+r.target()+"'.");var rs=new ArrayList<>(s.relationships());rs.add(new SemanticRelationship(r.id(),r.name(),r.target(),r.collection()?RelationshipCardinality.MANY:RelationshipCardinality.ONE,r.aliases().stream().map(a->new SemanticAlias(a.name(),a.weight())).toList()));entities.put(r.source(),new SemanticEntity(s.id(),s.name(),s.identity(),s.fields(),rs,s.aliases(),s.modelType()));}
  return new SemanticModel(entities,List.of(),false);
 }
 public static SemanticModelBuilder fromMetadata(IMetadataCatalog metadata){return new SemanticModelBuilder().importModel(discover(metadata));}
}
