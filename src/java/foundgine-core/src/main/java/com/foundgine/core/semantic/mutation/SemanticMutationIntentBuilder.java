package com.foundgine.core.semantic.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.*;
import com.foundgine.core.semantic.query.*;
import java.util.*;

/**
 * Developer-friendly open mutation authoring surface. Names and aliases are resolved
 * against the semantic model; the result is the canonical mutation operation graph.
 */
public final class SemanticMutationIntentBuilder {
    private final SemanticModel model;
    private final List<SemanticMutationOperation> operations = new ArrayList<>();
    private final Map<String,Integer> aliases = new TreeMap<>(String.CASE_INSENSITIVE_ORDER);

    public SemanticMutationIntentBuilder(SemanticModel model) { this.model = Objects.requireNonNull(model, "model"); }

    public SemanticMutationOperationBuilder create(String entity) { return create(entity, null); }
    public SemanticMutationOperationBuilder create(String entity, String alias) { return begin(entity, SemanticMutationKind.CREATE, alias); }
    public SemanticMutationOperationBuilder update(String entity) { return update(entity, null); }
    public SemanticMutationOperationBuilder update(String entity, String alias) { return begin(entity, SemanticMutationKind.UPDATE, alias); }
    public SemanticMutationOperationBuilder delete(String entity) { return delete(entity, null); }
    public SemanticMutationOperationBuilder delete(String entity, String alias) { return begin(entity, SemanticMutationKind.DELETE, alias); }
    public SemanticMutationOperationBuilder upsert(String entity) { return upsert(entity, null); }
    public SemanticMutationOperationBuilder upsert(String entity, String alias) { return begin(entity, SemanticMutationKind.UPSERT, alias); }

    public SemanticMutationOperationGraph build() {
        if (operations.isEmpty()) throw new IllegalStateException("A semantic mutation intent must contain at least one operation.");
        for (var op : operations) {
            if ((op.kind() == SemanticMutationKind.UPDATE || op.kind() == SemanticMutationKind.DELETE) && op.filter() == null)
                throw new IllegalStateException(op.kind() + " mutations require a target filter.");
            if (op.kind() == SemanticMutationKind.UPSERT && op.conflictFields().isEmpty())
                throw new IllegalStateException("Upsert mutations require conflict fields.");
        }
        return new SemanticMutationOperationGraph(operations);
    }

    private SemanticMutationOperationBuilder begin(String entityName, SemanticMutationKind kind, String alias) {
        var entity = findEntity(entityName);
        if (alias != null && !alias.isBlank() && aliases.putIfAbsent(alias, operations.size()) != null)
            throw new IllegalStateException("Mutation operation alias '" + alias + "' is already registered.");

        SemanticMutationOperation operation;
        if (kind == SemanticMutationKind.DELETE) {
            operation = new SemanticMutationOperation(entity.id(), kind, List.of(), null, List.of(),
                    List.of(entity.identity().fieldId()), List.of(new SemanticMutationEffect(SemanticMutationEffectKind.DELETE_ENTITY, entity.id())), List.of());
        } else if (kind == SemanticMutationKind.UPSERT) {
            operation = SemanticMutationBuilder.upsert(entity.id(), List.of(), List.of(), List.of());
        } else if (kind == SemanticMutationKind.CREATE) {
            operation = SemanticMutationBuilder.create(entity.id(), List.of(), List.of());
        } else {
            operation = SemanticMutationBuilder.update(entity.id(), List.of(), null, List.of());
        }
        int index = operations.size();
        operations.add(operation);
        return new SemanticMutationOperationBuilder(this, index);
    }

    SemanticMutationIntentBuilder set(int index, String fieldName, Object value) {
        var op = get(index); var field = findField(op.entity(), fieldName);
        var fields = new ArrayList<>(op.fields()); fields.add(new SemanticMutationField(field.id(), value));
        replace(index, withFields(op, fields)); return this;
    }

    SemanticMutationIntentBuilder setFrom(int index, String fieldName, String sourceAlias, String sourceFieldName) {
        var sourceIndex = aliases.get(sourceAlias);
        if (sourceIndex == null) throw new IllegalStateException("Unknown mutation operation alias '" + sourceAlias + "'.");
        if (sourceIndex >= index) throw new IllegalStateException("Mutation value dependencies must reference an earlier operation.");
        var op = get(index); var targetField = findField(op.entity(), fieldName);
        var sourceOp = get(sourceIndex); var sourceField = findField(sourceOp.entity(), sourceFieldName);
        var fields = new ArrayList<>(op.fields());
        fields.add(new SemanticMutationField(targetField.id(), null, new SemanticMutationValueReference(sourceIndex, sourceField.id())));
        replace(index, withFields(op, fields)); return this;
    }

    SemanticMutationIntentBuilder returnFields(int index, String... names) {
        Objects.requireNonNull(names); var op = get(index);
        List<FieldId> result = names.length == 0 ? List.of(model.get(op.entity()).identity().fieldId()) : Arrays.stream(names).map(n -> findField(op.entity(), n).id()).distinct().toList();
        replace(index, new SemanticMutationOperation(op.entity(), op.kind(), op.fields(), op.filter(), op.conflictFields(), result, op.effects(), op.dependencies())); return this;
    }

    SemanticMutationIntentBuilder conflict(int index, String... names) {
        Objects.requireNonNull(names); var op = get(index);
        List<FieldId> result = names.length == 0 ? List.of(model.get(op.entity()).identity().fieldId()) : Arrays.stream(names).map(n -> findField(op.entity(), n).id()).distinct().toList();
        replace(index, new SemanticMutationOperation(op.entity(), op.kind(), op.fields(), op.filter(), result, op.returnFields(), op.effects(), op.dependencies())); return this;
    }

    SemanticMutationIntentBuilder where(int index, String fieldName, SemanticFilterOperator operator, Object value) {
        var op = get(index);
        if (op.kind() == SemanticMutationKind.CREATE || op.kind() == SemanticMutationKind.UPSERT)
            throw new IllegalStateException("Mutation kind '" + op.kind() + "' does not accept a target filter.");
        var field = findField(op.entity(), fieldName);
        replace(index, new SemanticMutationOperation(op.entity(), op.kind(), op.fields(), new SemanticFieldFilter(field.id(), operator, value), op.conflictFields(), op.returnFields(), op.effects(), op.dependencies())); return this;
    }

    SemanticMutationIntentBuilder connect(int index, String relationshipName) {
        var op = get(index); var relationship = findRelationship(op.entity(), relationshipName);
        var effects = new ArrayList<>(op.effects()); effects.add(new SemanticMutationEffect(SemanticMutationEffectKind.CONNECT_RELATIONSHIP, op.entity(), null, relationship.id()));
        replace(index, new SemanticMutationOperation(op.entity(), op.kind(), op.fields(), op.filter(), op.conflictFields(), op.returnFields(), effects, op.dependencies())); return this;
    }

    private SemanticMutationOperation withFields(SemanticMutationOperation op, List<SemanticMutationField> fields) {
        var effects = op.effects().stream().filter(e -> e.kind() != SemanticMutationEffectKind.SET_FIELD).collect(java.util.stream.Collectors.toCollection(ArrayList::new));
        fields.forEach(f -> effects.add(new SemanticMutationEffect(SemanticMutationEffectKind.SET_FIELD, op.entity(), f.field())));
        return new SemanticMutationOperation(op.entity(), op.kind(), fields, op.filter(), op.conflictFields(), op.returnFields(), effects, op.dependencies());
    }
    private SemanticMutationOperation get(int index) { if(index<0||index>=operations.size()) throw new IndexOutOfBoundsException("Invalid mutation operation index: "+index); return operations.get(index); }
    private void replace(int index, SemanticMutationOperation op) { operations.set(index, op); }

    private SemanticEntity findEntity(String name) {
        return model.findEntity(name).orElseThrow(() -> new IllegalStateException("Unknown semantic mutation entity '" + name + "'."));
    }
    private SemanticField findField(EntityId id, String name) {
        var entity = model.get(id);
        return entity.fields().stream().filter(f -> f.name().equalsIgnoreCase(name) || f.effectiveAliases().stream().anyMatch(a -> a.name().equalsIgnoreCase(name))).findFirst()
                .orElseGet(() -> {
                    if (entity.identity().name().equalsIgnoreCase(name))
                        return new SemanticField(entity.identity().fieldId(), entity.identity().name(), Object.class, null,
                                (byte)(SemanticFieldCapabilities.DEFAULT | SemanticFieldCapabilities.WRITABLE), List.of(), List.of(), null);
                    throw new IllegalStateException("Unknown semantic mutation field '" + entity.name() + "." + name + "'.");
                });
    }
    private SemanticRelationship findRelationship(EntityId id, String name) {
        var entity = model.get(id);
        return entity.relationships().stream().filter(r -> r.name().equalsIgnoreCase(name) || r.effectiveAliases().stream().anyMatch(a -> a.name().equalsIgnoreCase(name))).findFirst()
                .orElseThrow(() -> new IllegalStateException("Unknown semantic mutation relationship '" + entity.name() + "." + name + "'."));
    }

    public static final class SemanticMutationOperationBuilder {
        private final SemanticMutationIntentBuilder owner; private final int index;
        private SemanticMutationOperationBuilder(SemanticMutationIntentBuilder owner, int index) { this.owner=owner; this.index=index; }
        public SemanticMutationOperationBuilder set(String field, Object value) { owner.set(index,field,value); return this; }
        public SemanticMutationOperationBuilder setFrom(String field,String sourceAlias,String sourceField){owner.setFrom(index,field,sourceAlias,sourceField);return this;}
        public SemanticMutationOperationBuilder returns(String... fields){owner.returnFields(index,fields);return this;}
        public SemanticMutationOperationBuilder conflict(String... fields){owner.conflict(index,fields);return this;}
        public SemanticMutationOperationBuilder where(String field,SemanticFilterOperator op,Object value){owner.where(index,field,op,value);return this;}
        public SemanticMutationOperationBuilder connect(String relationship){owner.connect(index,relationship);return this;}
        public SemanticMutationOperationBuilder create(String entity){return owner.create(entity);}
        public SemanticMutationOperationBuilder create(String entity,String alias){return owner.create(entity,alias);}
        public SemanticMutationOperationBuilder update(String entity){return owner.update(entity);}
        public SemanticMutationOperationBuilder update(String entity,String alias){return owner.update(entity,alias);}
        public SemanticMutationOperationBuilder delete(String entity){return owner.delete(entity);}
        public SemanticMutationOperationBuilder delete(String entity,String alias){return owner.delete(entity,alias);}
        public SemanticMutationOperationBuilder upsert(String entity){return owner.upsert(entity);}
        public SemanticMutationOperationBuilder upsert(String entity,String alias){return owner.upsert(entity,alias);}
        public SemanticMutationOperationGraph build(){return owner.build();}
        public SemanticMutationIntentBuilder next(){return owner;}
    }
}
