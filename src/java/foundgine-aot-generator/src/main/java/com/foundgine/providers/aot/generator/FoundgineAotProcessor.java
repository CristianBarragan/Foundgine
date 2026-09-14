package com.foundgine.providers.aot.generator;

import com.foundgine.providers.aot.FoundgineAlias;
import com.foundgine.providers.aot.FoundgineEntity;
import com.foundgine.providers.aot.FoundgineEvent;
import com.foundgine.providers.aot.FoundgineField;
import com.foundgine.providers.aot.FoundgineRelationship;
import com.foundgine.core.semantic.metadata.AliasDeclaration;
import com.foundgine.core.semantic.metadata.ColumnMetadata;
import com.foundgine.core.semantic.metadata.ColumnReference;
import com.foundgine.core.semantic.metadata.EntityMetadata;
import com.foundgine.core.semantic.metadata.FieldMetadata;
import com.foundgine.core.semantic.metadata.MetadataRegistry;
import com.foundgine.core.semantic.metadata.RelationshipMetadata;
import com.foundgine.core.abstractions.ColumnId;
import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.abstractions.RelationshipId;

import javax.annotation.processing.AbstractProcessor;
import javax.annotation.processing.RoundEnvironment;
import javax.annotation.processing.SupportedAnnotationTypes;
import javax.annotation.processing.SupportedOptions;
import javax.lang.model.SourceVersion;
import javax.lang.model.element.Element;
import javax.lang.model.element.ElementKind;
import javax.lang.model.element.Modifier;
import javax.lang.model.element.TypeElement;
import javax.lang.model.element.VariableElement;
import javax.lang.model.type.TypeKind;
import javax.lang.model.type.TypeMirror;
import javax.lang.model.util.Elements;
import javax.lang.model.util.Types;
import javax.tools.Diagnostic;
import javax.tools.JavaFileObject;

import java.io.IOException;
import java.io.Writer;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Compile-time Foundgine metadata generator.
 *
 * <p>
 * The generated metadata is ordinary Java source. Runtime reflection is not
 * required to construct the metadata registry.
 * </p>
 *
 * <p>
 * Column identity is deliberately centralized in {@link #columnId(TypeElement,
 * VariableElement)} so that column metadata, field metadata, primary-key
 * references, temporal references, and relationship references all use the
 * same identity calculation.
 * </p>
 */
@SupportedAnnotationTypes(
        "com.foundgine.providers.aot.FoundgineEntity")
@SupportedOptions({
        "foundgine.generated.package",
        "foundgine.generated.name"
})
public final class FoundgineAotProcessor extends AbstractProcessor {

    private static final String DEFAULT_PACKAGE =
            "com.foundgine.generated";

    private static final String DEFAULT_NAME =
            "GeneratedFoundgineMetadata";

    private Types typeUtils() {
        return processingEnv.getTypeUtils();
    }

    private Elements elementUtils() {
        return processingEnv.getElementUtils();
    }

    @Override
    public SourceVersion getSupportedSourceVersion() {
        return SourceVersion.latestSupported();
    }

    @Override
    public boolean process(
            Set<? extends TypeElement> annotations,
            RoundEnvironment roundEnv) {

        if (roundEnv.processingOver()) {
            return false;
        }

        Set<? extends Element> annotated =
                roundEnv.getElementsAnnotatedWith(
                        FoundgineEntity.class);

        if (annotated.isEmpty()) {
            return false;
        }

        List<TypeElement> entities = new ArrayList<>();

        for (Element element : annotated) {
            if (element instanceof TypeElement type) {
                entities.add(type);
            }
        }

        entities.sort(
                Comparator
                        .comparing(this::entityName)
                        .thenComparing(
                                type -> type.getQualifiedName().toString()));

        try {
            validateEntities(entities);
            generate(entities);
        } catch (IOException | RuntimeException ex) {
            processingEnv.getMessager().printMessage(
                    Diagnostic.Kind.ERROR,
                    "Foundgine AOT generation failed: "
                            + ex.getMessage());
        }

        return true;
    }

    /*
     * -------------------------------------------------------------------------
     * Validation
     * -------------------------------------------------------------------------
     */

    private void validateEntities(
            List<TypeElement> entities) {

        for (TypeElement entity : entities) {
            validatePrimaryKeyShape(entity);
            validateRelationships(entity);
            validateTemporalField(entity);
        }
    }

    /**
     * The Java metadata model currently exposes one scalar
     * ColumnReference for the primary key.
     *
     * <p>
     * An entity is allowed to have no primary key. In that case the generated
     * EntityMetadata receives null for its primary key.
     * </p>
     */
    private void validatePrimaryKeyShape(
            TypeElement entity) {

        List<VariableElement> primaryKeys =
                primaryKeyFields(
                        allFields(entity));

        if (primaryKeys.size() <= 1) {
            return;
        }

        throw new IllegalStateException(
                "Entity '"
                        + entityName(entity)
                        + "' declares "
                        + primaryKeys.size()
                        + " primary-key fields, but the current "
                        + "Java EntityMetadata API supports only a "
                        + "single primary-key ColumnReference.");
    }

    private void validateRelationships(
            TypeElement entity) {

        for (VariableElement field :
                allFields(entity)) {

            FoundgineRelationship relationship =
                    field.getAnnotation(
                            FoundgineRelationship.class);

            if (relationship == null) {
                continue;
            }

            TypeElement target =
                    relationshipTarget(field);

            if (target == null) {
                throw new IllegalStateException(
                        "Relationship '"
                                + field.getSimpleName()
                                + "' on "
                                + entityName(entity)
                                + " has no resolvable target type.");
            }

            columnId(
                    entity,
                    relationship.principalKey());

            columnId(
                    target,
                    relationship.foreignKey());
        }
    }

    private void validateTemporalField(
            TypeElement entity) {

        FoundgineEvent event =
                entity.getAnnotation(
                        FoundgineEvent.class);

        if (event == null) {
            return;
        }

        String requested =
                event.occurredAtField();

        /*
         * No explicit temporal field is a valid state.
         *
         * Do not assume an OccurredAt field.
         * The generated temporal metadata will be null.
         */
        if (requested == null || requested.isBlank()) {
            return;
        }

        VariableElement field =
                findField(entity, requested);

        if (field == null) {
            throw new IllegalStateException(
                    "Event entity '"
                            + entityName(entity)
                            + "' has no field corresponding to "
                            + "occurredAtField '"
                            + requested
                            + "'.");
        }
    }

    /*
     * -------------------------------------------------------------------------
     * Generation
     * -------------------------------------------------------------------------
     */

    private void generate(
            List<TypeElement> entities) throws IOException {

        String packageName =
                processingEnv.getOptions().getOrDefault(
                        "foundgine.generated.package",
                        DEFAULT_PACKAGE);

        String className =
                processingEnv.getOptions().getOrDefault(
                        "foundgine.generated.name",
                        DEFAULT_NAME);

        JavaFileObject file =
                processingEnv.getFiler().createSourceFile(
                        packageName + "." + className);

        try (Writer writer = file.openWriter()) {

            writer.write(
                    "package "
                            + packageName
                            + ";\n\n");

            writer.write(
                    "// Generated by FoundgineAotProcessor. "
                            + "DO NOT EDIT.\n\n");

            writer.write(
                    "import java.util.List;\n");

            writer.write(
                    "import com.foundgine.core.abstractions.*;\n");

            writer.write(
                    "import com.foundgine.core.semantic.metadata.*;\n\n");

            writer.write(
                    "public final class "
                            + className
                            + " {\n\n");

            writer.write(
                    "    private "
                            + className
                            + "() {}\n\n");

            writer.write(
                    "    public static MetadataRegistry build() {\n");

            writer.write(
                    "        MetadataRegistry registry = "
                            + "new MetadataRegistry();\n\n");

            for (TypeElement entity : entities) {
                emitEntity(writer, entity);
            }

            for (TypeElement entity : entities) {
                emitRelationships(writer, entity);
            }

            writer.write(
                    "        return registry;\n");

            writer.write(
                    "    }\n");

            writer.write(
                    "}\n");
        }
    }

    private void emitEntity(
            Writer writer,
            TypeElement entity) throws IOException {

        String name =
                entityName(entity);

        long id =
                entityId(entity);

        List<VariableElement> fields =
                allFields(entity);

        writer.write(
                "        registry.register(new EntityMetadata(\n");

        writer.write(
                "                new EntityId("
                        + idLiteral(id)
                        + "L),\n");

        writer.write(
                "                \""
                        + escape(name)
                        + "\",\n");

        emitColumns(
                writer,
                entity,
                fields);

        writer.write(
                "                \""
                        + escape(name)
                        + "\",\n");

        emitFields(
                writer,
                entity,
                fields);

        writer.write(
                "                "
                        + primaryKeyReference(
                                entity,
                                fields)
                        + ",\n");

        writer.write(
                "                "
                        + entity.getQualifiedName()
                        + ".class,\n");

        boolean event =
                entity.getAnnotation(
                        FoundgineEvent.class) != null;

        writer.write(
                "                "
                        + event
                        + ",\n");

        writer.write(
                "                "
                        + temporalColumnReference(
                                entity,
                                fields)
                        + ",\n");

        writer.write(
                "                "
                        + aliasList(entity)
                        + "\n");

        writer.write(
                "        ));\n\n");
    }

    private void emitColumns(
            Writer writer,
            TypeElement entity,
            List<VariableElement> fields)
            throws IOException {

        writer.write(
                "                List.of(\n");

        for (int i = 0; i < fields.size(); i++) {

            VariableElement field =
                    fields.get(i);

            if (i > 0) {
                writer.write(",\n");
            }

            long columnId =
                    columnId(entity, field);

            writer.write(
                    "                        new ColumnMetadata(\n"
                            + "                                new ColumnId("
                            + idLiteral(columnId)
                            + "L),\n"
                            + "                                \""
                            + escape(fieldName(field))
                            + "\",\n"
                            + "                                \""
                            + escape(storageColumnName(field))
                            + "\"\n"
                            + "                        )");
        }

        writer.write(
                "\n"
                        + "                ),\n");
    }

    private void emitFields(
            Writer writer,
            TypeElement entity,
            List<VariableElement> fields)
            throws IOException {

        writer.write(
                "                List.of(\n");

        for (int i = 0; i < fields.size(); i++) {

            VariableElement field =
                    fields.get(i);

            if (i > 0) {
                writer.write(",\n");
            }

            long fieldId =
                    fieldId(
                            entity,
                            field);

            long columnId =
                    columnId(entity, field);

            writer.write(
                    "                        new FieldMetadata(\n"
                            + "                                new FieldId("
                            + idLiteral(fieldId)
                            + "L),\n"
                            + "                                \""
                            + escape(semanticFieldName(field))
                            + "\",\n"
                            + "                                "
                            + typeLiteral(field.asType())
                            + ",\n"
                            + "                                new ColumnReference(\n"
                            + "                                        new EntityId("
                            + idLiteral(entityId(entity))
                            + "L),\n"
                            + "                                        new ColumnId("
                            + idLiteral(columnId)
                            + "L)\n"
                            + "                                ),\n"
                            + "                                "
                            + stringOrNull(
                                    dimension(field))
                            + ",\n"
                            + "                                "
                            + isIndexed(field)
                            + ",\n"
                            + "                                "
                            + aliasList(field)
                            + "\n"
                            + "                        )");
        }

        writer.write(
                "\n"
                        + "                ),\n");
    }

    /*
     * -------------------------------------------------------------------------
     * Primary key
     * -------------------------------------------------------------------------
     */

    private String primaryKeyReference(
            TypeElement entity,
            List<VariableElement> fields) {

        List<VariableElement> primaryKeys =
                primaryKeyFields(fields);

        if (primaryKeys.isEmpty()) {
            /*
             * No PK is a valid metadata state.
             */
            return "null";
        }

        if (primaryKeys.size() > 1) {

            throw new IllegalStateException(
                    "Entity '"
                            + entityName(entity)
                            + "' declares a composite primary key, "
                            + "but EntityMetadata currently supports "
                            + "only one ColumnReference.");
        }

        VariableElement primaryKey =
                primaryKeys.get(0);

        long entityId =
                entityId(entity);

        long columnId =
                columnId(entity, primaryKey);

        return
                "new ColumnReference(\n"
                        + "                        new EntityId("
                        + idLiteral(entityId)
                        + "L),\n"
                        + "                        new ColumnId("
                        + idLiteral(columnId)
                        + "L)\n"
                        + "                )";
    }

    private List<VariableElement> primaryKeyFields(
            List<VariableElement> fields) {

        List<VariableElement> explicit =
                new ArrayList<>();

        for (VariableElement field : fields) {
            FoundgineField annotation =
                    field.getAnnotation(
                            FoundgineField.class);

            if (annotation != null
                    && annotation.isPrimaryKey()) {

                explicit.add(field);
            }
        }

        /*
         * Explicit primary-key declarations always win.
         *
         * If none is declared, preserve the conventional
         * Foundgine entity identity rule: a field named
         * "id" is the implicit primary key.
         */
        if (!explicit.isEmpty()) {
            return explicit;
        }

        for (VariableElement field : fields) {
            if (fieldName(field).equalsIgnoreCase("id")) {
                return List.of(field);
            }
        }

        return List.of();
    }

    /*
     * -------------------------------------------------------------------------
     * Temporal metadata
     * -------------------------------------------------------------------------
     */

    private String temporalColumnReference(
            TypeElement entity,
            List<VariableElement> fields) {

        FoundgineEvent event =
                entity.getAnnotation(
                        FoundgineEvent.class);

        if (event == null) {
            return "null";
        }

        String requested =
                event.occurredAtField();

        /*
         * No explicit temporal field means that temporal metadata is
         * unspecified. Do not invent an OccurredAt field.
         */
        if (requested == null || requested.isBlank()) {
            return "null";
        }

        VariableElement field =
                findField(entity, requested);

        if (field == null) {

            throw new IllegalStateException(
                    "Event entity '"
                            + entityName(entity)
                            + "' has no field corresponding to "
                            + "occurredAtField '"
                            + requested
                            + "'.");
        }

        long columnId =
                columnId(entity, field);

        return
                "new ColumnReference(\n"
                        + "                        new EntityId("
                        + idLiteral(entityId(entity))
                        + "L),\n"
                        + "                        new ColumnId("
                        + idLiteral(columnId)
                        + "L)\n"
                        + "                )";
    }

    /*
     * -------------------------------------------------------------------------
     * Relationships
     * -------------------------------------------------------------------------
     */

    private void emitRelationships(
            Writer writer,
            TypeElement entity)
            throws IOException {

        for (VariableElement field :
                allFields(entity)) {

            FoundgineRelationship relationship =
                    field.getAnnotation(
                            FoundgineRelationship.class);

            if (relationship == null) {
                continue;
            }

            TypeElement target =
                    relationshipTarget(field);

            if (target == null) {

                throw new IllegalStateException(
                        "Relationship '"
                                + field.getSimpleName()
                                + "' on "
                                + entityName(entity)
                                + " has no resolvable target.");
            }

            String relationshipName =
                    relationship.name() == null
                            || relationship.name().isBlank()
                            ? field.getSimpleName().toString()
                            : relationship.name();

            long relationshipId =
                    relationship.id() != 0
                            ? GeneratorSemanticIdentity
                                    .validateExplicitId(
                                            relationship.id(),
                                            "relationship")
                            : GeneratorSemanticIdentity.hash(
                                    GeneratorSemanticIdentity
                                            .relationshipKey(
                                                    entityName(entity),
                                                    relationshipName));

            long sourceColumn =
                    columnId(
                            entity,
                            relationship.principalKey());

            long targetColumn =
                    columnId(
                            target,
                            relationship.foreignKey());

            boolean isCollection =
                    isEntityOwnPrimaryKey(
                            entity,
                            relationship.principalKey());

            writer.write(
                    "        registry.register(\n"
                            + "                new RelationshipMetadata(\n"
                            + "                        new RelationshipId("
                            + idLiteral(relationshipId)
                            + "L),\n"
                            + "                        new EntityId("
                            + idLiteral(entityId(entity))
                            + "L),\n"
                            + "                        new EntityId("
                            + idLiteral(entityId(target))
                            + "L),\n"
                            + "                        \""
                            + escape(relationshipName)
                            + "\",\n"
                            + "                        new ColumnReference(\n"
                            + "                                new EntityId("
                            + idLiteral(entityId(entity))
                            + "L),\n"
                            + "                                new ColumnId("
                            + idLiteral(sourceColumn)
                            + "L)\n"
                            + "                        ),\n"
                            + "                        new ColumnReference(\n"
                            + "                                new EntityId("
                            + idLiteral(entityId(target))
                            + "L),\n"
                            + "                                new ColumnId("
                            + idLiteral(targetColumn)
                            + "L)\n"
                            + "                        ),\n"
                            + "                        "
                            + isCollection
                            + ",\n"
                            + "                        "
                            + aliasList(field)
                            + "\n"
                            + "                )\n"
                            + "        );\n\n");
        }
    }

    /**
     * Determines whether a relationship is to-many (MANY) or to-one (ONE).
     *
     * <p>
     * Java has no equivalent of the C# generator's ability to inspect
     * whether the declared property type is a collection expression, so an
     * equivalent structural signal is used instead: a relationship is MANY
     * if and only if its principal-key column (the source-side column the
     * relationship is declared on) is the source entity's own primary key.
     * Otherwise the relationship is ONE. This mirrors standard FK semantics:
     * the "one" side of a one-to-many relationship is keyed by its own
     * primary key, while the "many" side merely holds a foreign key.
     * </p>
     */
    private boolean isEntityOwnPrimaryKey(
            TypeElement entity,
            String principalKeyFieldName) {

        VariableElement principalKeyField =
                findField(entity, principalKeyFieldName);

        if (principalKeyField == null) {
            return true;
        }

        List<VariableElement> primaryKeys =
                primaryKeyFields(allFields(entity));

        for (VariableElement primaryKey : primaryKeys) {
            if (primaryKey.equals(principalKeyField)) {
                return true;
            }
        }

        return false;
    }

    /*
     * -------------------------------------------------------------------------
     * Relationship target resolution
     * -------------------------------------------------------------------------
     */

    private TypeElement relationshipTarget(
            VariableElement field) {

        for (var mirror :
                field.getAnnotationMirrors()) {

            Element annotationElement =
                    mirror.getAnnotationType().asElement();

            if (!(annotationElement instanceof TypeElement annotationType)) {
                continue;
            }

            if (!annotationType.getQualifiedName()
                    .contentEquals(
                            FoundgineRelationship.class.getName())) {
                continue;
            }

            for (Map.Entry<
                    ? extends javax.lang.model.element.ExecutableElement,
                    ? extends javax.lang.model.element.AnnotationValue>
                    entry :
                    mirror.getElementValues().entrySet()) {

                if (!entry.getKey()
                        .getSimpleName()
                        .contentEquals("target")) {
                    continue;
                }

                Object value =
                        entry.getValue().getValue();

                if (value instanceof TypeMirror mirrorType
                        && mirrorType.getKind()
                        == TypeKind.DECLARED) {

                    Element target =
                            typeUtils().asElement(mirrorType);

                    if (target instanceof TypeElement targetType) {
                        return targetType;
                    }
                }
            }
        }

        return null;
    }

    /*
     * -------------------------------------------------------------------------
     * Field discovery
     * -------------------------------------------------------------------------
     */

    /**
     * Returns instance fields from the entity hierarchy.
     *
     * <p>
     * Derived declarations win when a field name is hidden by a subclass.
     * The resulting collection is deterministic.
     * </p>
     */
    private List<VariableElement> allFields(
            TypeElement entity) {

        Map<String, VariableElement> fields =
                new LinkedHashMap<>();

        TypeElement current =
                entity;

        while (current != null) {

            for (Element member :
                    current.getEnclosedElements()) {

                if (member.getKind()
                        != ElementKind.FIELD) {
                    continue;
                }

                if (member.getModifiers()
                        .contains(Modifier.STATIC)) {
                    continue;
                }

                VariableElement field =
                        (VariableElement) member;

                fields.putIfAbsent(
                        field.getSimpleName().toString(),
                        field);
            }

            TypeMirror superclass =
                    current.getSuperclass();

            if (superclass == null
                    || superclass.getKind()
                    == TypeKind.NONE) {
                break;
            }

            Element parentElement =
                    typeUtils().asElement(superclass);

            if (!(parentElement instanceof TypeElement parent)) {
                break;
            }

            current =
                    parent;
        }

        List<VariableElement> result =
                new ArrayList<>(fields.values());

        result.sort(
                Comparator
                        .comparing(
                                this::fieldName)
                        .thenComparing(
                                field ->
                                        field.getSimpleName()
                                                .toString()));

        return result;
    }

    private VariableElement findField(
            TypeElement entity,
            String requested) {

        for (VariableElement field :
                allFields(entity)) {

            if (fieldName(field)
                    .equalsIgnoreCase(requested)
                    || field.getSimpleName()
                    .toString()
                    .equalsIgnoreCase(requested)) {

                return field;
            }
        }

        return null;
    }

    /*
     * -------------------------------------------------------------------------
     * Canonical column identity
     * -------------------------------------------------------------------------
     */

    private long columnId(
            TypeElement entity,
            VariableElement field) {

        FoundgineField annotation =
                field.getAnnotation(
                        FoundgineField.class);

        if (annotation != null
                && annotation.columnId() != 0) {

            return GeneratorSemanticIdentity
                    .validateExplicitId(
                            annotation.columnId(),
                            "column");
        }

        return GeneratorSemanticIdentity.hash(
                GeneratorSemanticIdentity.columnKey(
                        entityName(entity),
                        storageColumnName(field)));
    }

    private long columnId(
            TypeElement entity,
            String requestedFieldName) {

        if (requestedFieldName == null
                || requestedFieldName.isBlank()) {

            throw new IllegalArgumentException(
                    "Relationship column name cannot be null "
                            + "or blank on "
                            + entityName(entity));
        }

        VariableElement field =
                findField(
                        entity,
                        requestedFieldName);

        if (field == null) {

            throw new IllegalArgumentException(
                    "Unknown relationship column '"
                            + requestedFieldName
                            + "' on "
                            + entityName(entity));
        }

        return columnId(entity, field);
    }

    private String storageColumnName(
            VariableElement field) {

        FoundgineField annotation =
                field.getAnnotation(
                        FoundgineField.class);

        if (annotation == null
                || annotation.storageName() == null
                || annotation.storageName().isBlank()) {

            return fieldName(field);
        }

        return annotation.storageName();
    }

    /*
     * -------------------------------------------------------------------------
     * Semantic identities
     * -------------------------------------------------------------------------
     */

    private long entityId(
            TypeElement entity) {

        FoundgineEntity annotation =
                entity.getAnnotation(
                        FoundgineEntity.class);

        if (annotation != null
                && annotation.id() != 0) {

            return GeneratorSemanticIdentity
                    .validateExplicitId(
                            annotation.id(),
                            "entity");
        }

        return GeneratorSemanticIdentity.hash(
                GeneratorSemanticIdentity.entityKey(
                        entityName(entity)));
    }

    private long fieldId(
            TypeElement entity,
            VariableElement field) {

        FoundgineField annotation =
                field.getAnnotation(
                        FoundgineField.class);

        if (annotation != null
                && annotation.id() != 0) {

            return GeneratorSemanticIdentity
                    .validateExplicitId(
                            annotation.id(),
                            "field");
        }

        return GeneratorSemanticIdentity.hash(
                GeneratorSemanticIdentity.fieldKey(
                        entityName(entity),
                        semanticFieldName(field)));
    }

    /*
     * -------------------------------------------------------------------------
     * Annotation helpers
     * -------------------------------------------------------------------------
     */

    private String entityName(
            TypeElement entity) {

        FoundgineEntity annotation =
                entity.getAnnotation(
                        FoundgineEntity.class);

        if (annotation == null
                || annotation.name() == null
                || annotation.name().isBlank()) {

            return entity.getSimpleName().toString();
        }

        return annotation.name();
    }

    private String fieldName(
            VariableElement field) {

        FoundgineField annotation =
                field.getAnnotation(
                        FoundgineField.class);

        if (annotation == null
                || annotation.name() == null
                || annotation.name().isBlank()) {

            return field.getSimpleName().toString();
        }

        return annotation.name();
    }

    /*
     * The semantic field name exposed on FieldMetadata (and used to derive
     * the field's stable identity hash) must match the PascalCase convention
     * that C#'s reflection-based property names produce by default, and that
     * hand-authored overlays (e.g. ManualSupplyChainSemanticModel) already
     * assume via FieldId.create(entity, "PascalCaseName"). Java record
     * component names are idiomatically camelCase, so - unless an explicit
     * @FoundgineField(name=...) override is given - capitalize the first
     * letter here. This is intentionally distinct from fieldName(), which
     * continues to back storageColumnName()'s default and therefore leaves
     * physical/storage naming untouched.
     */
    private String semanticFieldName(
            VariableElement field) {

        FoundgineField annotation =
                field.getAnnotation(
                        FoundgineField.class);

        if (annotation != null
                && annotation.name() != null
                && !annotation.name().isBlank()) {

            return annotation.name();
        }

        return capitalize(field.getSimpleName().toString());
    }

    private String capitalize(String value) {

        if (value == null || value.isEmpty()) {
            return value;
        }

        return Character.toUpperCase(value.charAt(0)) + value.substring(1);
    }

    private boolean isIndexed(
            VariableElement field) {

        FoundgineField annotation =
                field.getAnnotation(
                        FoundgineField.class);

        return annotation != null
                && annotation.index();
    }

    private String dimension(
            VariableElement field) {

        /*
         * Resolve the annotation through the element rather than attempting
         * runtime reflection. The actual enum/string representation is
         * preserved by the annotation value.
         */

        var annotation =
                field.getAnnotation(
                        com.foundgine.providers.aot
                                .FoundgineSemanticDimension.class);

        if (annotation == null) {
            return null;
        }

        return annotation.value();
    }

    private String aliasList(
            Element element) {

        FoundgineAlias[] aliases =
                element.getAnnotationsByType(
                        FoundgineAlias.class);

        if (aliases == null
                || aliases.length == 0) {

            return "List.of()";
        }

        StringBuilder result =
                new StringBuilder("List.of(");

        for (int i = 0; i < aliases.length; i++) {

            if (i > 0) {
                result.append(", ");
            }

            FoundgineAlias alias =
                    aliases[i];

            result.append(
                    "new AliasDeclaration(\"")
                    .append(escape(alias.value()))
                    .append("\", ")
                    .append(alias.weight())
                    .append(")");
        }

        result.append(")");

        return result.toString();
    }

    /*
     * -------------------------------------------------------------------------
     * Java source helpers
     * -------------------------------------------------------------------------
     */

    private String typeLiteral(
            TypeMirror type) {

        TypeMirror erased =
                typeUtils().erasure(type);

        return erased.toString() + ".class";
    }

    private String stringOrNull(
            String value) {

        if (value == null) {
            return "null";
        }

        return "\""
                + escape(value)
                + "\"";
    }

    private String idLiteral(
            long value) {

        return Long.toString(value);
    }

    private String escape(
            String value) {

        return value
                .replace("\\", "\\\\")
                .replace("\"", "\\\"")
                .replace("\n", "\\n")
                .replace("\r", "\\r");
    }
}