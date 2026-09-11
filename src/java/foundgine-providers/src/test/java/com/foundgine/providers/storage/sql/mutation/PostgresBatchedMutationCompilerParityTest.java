package com.foundgine.providers.storage.sql.mutation;

import com.foundgine.core.abstractions.*;
import com.foundgine.core.semantic.metadata.*;
import com.foundgine.core.semantic.planning.mutation.*;
import org.junit.jupiter.api.Test;

import java.util.*;

import static org.junit.jupiter.api.Assertions.*;

/** Contract tests ported from the C# PostgreSQL correlation suite. */
public class PostgresBatchedMutationCompilerParityTest {

    @Test
    void exposesExecutionIrEntryPoint() throws Exception {
        assertNotNull(PostgresBatchedMutationCompiler.class.getMethod("compile", com.foundgine.core.execution.mutation.ExecutionMutationIR.class));
        assertNotNull(PostgresBatchedMutationCompiler.class.getMethod("tryCompile", com.foundgine.core.execution.mutation.ExecutionMutationIR.class));
    }

    @Test
    void batchedCreateUsesExplicitCorrelationCarrier() {
        EntityId entityId = new EntityId(1);
        ColumnId id = new ColumnId(1), name = new ColumnId(2);
        FieldId idField = new FieldId(1), nameField = new FieldId(2);
        MetadataRegistry metadata = new MetadataRegistry();
        metadata.register(new EntityMetadata(entityId, "Customer",
                List.of(new ColumnMetadata(id, "Id"), new ColumnMetadata(name, "Name")),
                null,
                List.of(new FieldMetadata(idField, "Id", Long.class, new ColumnReference(entityId, id)),
                        new FieldMetadata(nameField, "Name", String.class, new ColumnReference(entityId, name))),
                new ColumnReference(entityId, id), null, false, null, null));

        MutationEntitySchema entity = new MutationEntitySchema(entityId, "Customer",
                Set.of(id, name), Map.of(idField, id, nameField, name), id);
        MutationBatchPlan plan = new MutationBatchPlan(List.of(
                new MutationOperation(entity, MutationKind.CREATE,
                        List.of(new MutationFieldValue(id, 101L), new MutationFieldValue(name, "Alice")), null, null, List.of(idField, nameField)),
                new MutationOperation(entity, MutationKind.CREATE,
                        List.of(new MutationFieldValue(id, 102L), new MutationFieldValue(name, "Bob")), null, null, List.of(idField, nameField))), List.of());

        String sql = new PostgresBatchedMutationCompiler(metadata).compile(plan).commandText();
        assertTrue(sql.contains("__fg_corr"));
        assertTrue(sql.contains("MERGE INTO \"Customer\" t"));
        assertTrue(sql.contains("USING g0_input r ON FALSE"));
        assertTrue(sql.contains("RETURNING r.__fg_corr"));
        assertTrue(sql.contains("jsonb_build_object('__fg_corr', f.__fg_corr)"));
    }

    @Test
    void generatedIdentityDoesNotBorrowUserVisibleKeyForCorrelation() {
        EntityId entityId = new EntityId(7);
        ColumnId id = new ColumnId(1), name = new ColumnId(2);
        FieldId idField = new FieldId(1), nameField = new FieldId(2);
        MetadataRegistry metadata = new MetadataRegistry();
        metadata.register(new EntityMetadata(entityId, "Customer",
                List.of(new ColumnMetadata(id, "Id"), new ColumnMetadata(name, "Name")),
                null,
                List.of(new FieldMetadata(idField, "Id", Long.class, new ColumnReference(entityId, id)),
                        new FieldMetadata(nameField, "Name", String.class, new ColumnReference(entityId, name))),
                new ColumnReference(entityId, id), null, false, null, null));

        MutationEntitySchema entity = new MutationEntitySchema(entityId, "Customer",
                Set.of(id, name), Map.of(idField, id, nameField, name), id);
        MutationBatchPlan plan = new MutationBatchPlan(List.of(
                new MutationOperation(entity, MutationKind.CREATE, List.of(new MutationFieldValue(name, "same")), null, null, List.of(idField, nameField)),
                new MutationOperation(entity, MutationKind.CREATE, List.of(new MutationFieldValue(name, "same")), null, null, List.of(idField, nameField))), List.of());

        String sql = new PostgresBatchedMutationCompiler(metadata).compile(plan).commandText();
        assertTrue(sql.contains("RETURNING r.__fg_corr"));
        assertTrue(sql.contains("t.\"Id\" AS \"r_1\""));
        assertFalse(sql.contains("g0_keys"));
    }
    @Test
    void tryCompileFallsBackForDuplicateLiteralUpsertKeys() {
        EntityId entityId = new EntityId(9);
        ColumnId id = new ColumnId(1), name = new ColumnId(2);
        FieldId idField = new FieldId(1), nameField = new FieldId(2);
        MetadataRegistry metadata = new MetadataRegistry();
        metadata.register(new EntityMetadata(entityId, "Customer",
                List.of(new ColumnMetadata(id, "Id"), new ColumnMetadata(name, "Name")), null,
                List.of(new FieldMetadata(idField, "Id", Long.class, new ColumnReference(entityId, id)),
                        new FieldMetadata(nameField, "Name", String.class, new ColumnReference(entityId, name))),
                new ColumnReference(entityId, id), null, false, null, null));
        MutationEntitySchema entity = new MutationEntitySchema(entityId, "Customer",
                Set.of(id, name), Map.of(idField, id, nameField, name), id);
        MutationOperation a = new MutationOperation(entity, MutationKind.UPSERT,
                List.of(new MutationFieldValue(id, 7L), new MutationFieldValue(name, "a")), null, List.of(id), List.of(idField));
        MutationOperation b = new MutationOperation(entity, MutationKind.UPSERT,
                List.of(new MutationFieldValue(id, 7L), new MutationFieldValue(name, "b")), null, List.of(id), List.of(idField));
        assertNull(new PostgresBatchedMutationCompiler(metadata).tryCompile(new MutationBatchPlan(List.of(a, b), List.of())));
    }

    @Test
    void dependencyLevelsCreateDistinctPhysicalGroupsAndCorrelationMetadata() {
        EntityId customer = new EntityId(11), account = new EntityId(12);
        ColumnId cId = new ColumnId(1), cName = new ColumnId(2);
        ColumnId aId = new ColumnId(3), aCustomer = new ColumnId(4), aName = new ColumnId(5);
        FieldId cIdField = new FieldId(1), cNameField = new FieldId(2);
        FieldId aIdField = new FieldId(3), aCustomerField = new FieldId(4), aNameField = new FieldId(5);
        MetadataRegistry metadata = new MetadataRegistry();
        metadata.register(new EntityMetadata(customer, "Customer",
                List.of(new ColumnMetadata(cId, "Id"), new ColumnMetadata(cName, "Name")), null,
                List.of(new FieldMetadata(cIdField, "Id", Long.class, new ColumnReference(customer, cId)),
                        new FieldMetadata(cNameField, "Name", String.class, new ColumnReference(customer, cName))),
                new ColumnReference(customer, cId), null, false, null, null));
        metadata.register(new EntityMetadata(account, "Account",
                List.of(new ColumnMetadata(aId, "Id"), new ColumnMetadata(aCustomer, "CustomerId"), new ColumnMetadata(aName, "Name")), null,
                List.of(new FieldMetadata(aIdField, "Id", Long.class, new ColumnReference(account, aId)),
                        new FieldMetadata(aCustomerField, "CustomerId", Long.class, new ColumnReference(account, aCustomer)),
                        new FieldMetadata(aNameField, "Name", String.class, new ColumnReference(account, aName))),
                new ColumnReference(account, aId), null, false, null, null));
        MutationEntitySchema c = new MutationEntitySchema(customer, "Customer", Set.of(cId, cName), Map.of(cIdField, cId, cNameField, cName), cId);
        MutationEntitySchema a = new MutationEntitySchema(account, "Account", Set.of(aId, aCustomer, aName), Map.of(aIdField, aId, aCustomerField, aCustomer, aNameField, aName), aId);
        MutationOperation first = new MutationOperation(c, MutationKind.CREATE, List.of(new MutationFieldValue(cName, "Alice")), null, null, List.of(cIdField, cNameField));
        MutationOperation second = new MutationOperation(a, MutationKind.CREATE, List.of(MutationFieldValue.fromPrevious(aCustomer, 0, cIdField), new MutationFieldValue(aName, "Primary")), null, null, List.of(aIdField, aCustomerField, aNameField));
        MutationDependency dependency = new MutationDependency(0, 1, cIdField, aCustomer);
        SqlBatchedMutationPlan compiled = new PostgresBatchedMutationCompiler(metadata).compile(new MutationBatchPlan(List.of(first, second), List.of(dependency)));
        assertEquals(2, compiled.groups().size());
        assertEquals(2, compiled.rowKeys().size());
        assertTrue(compiled.commandText().contains("JOIN g0_ordmap"));
        assertTrue(compiled.commandText().contains("CustomerId__fg_corr"));
    }

    @Test
    void tryCompileFallsBackWhenOneEntityMixesInsertAndUpdate() {
        EntityId entityId = new EntityId(21);
        ColumnId id = new ColumnId(1), name = new ColumnId(2);
        FieldId idField = new FieldId(1), nameField = new FieldId(2);
        MetadataRegistry metadata = simpleMetadata(entityId, "Customer", id, name, idField, nameField);
        MutationEntitySchema entity = new MutationEntitySchema(entityId, "Customer",
                Set.of(id, name), Map.of(idField, id, nameField, name), id);

        MutationOperation create = new MutationOperation(entity, MutationKind.CREATE,
                List.of(new MutationFieldValue(id, 1L), new MutationFieldValue(name, "created")),
                null, null, List.of(idField, nameField));
        MutationOperation update = new MutationOperation(entity, MutationKind.UPDATE,
                List.of(new MutationFieldValue(name, "updated")),
                List.of(id), null, List.of(nameField));

        assertNull(new PostgresBatchedMutationCompiler(metadata).tryCompile(
                new MutationBatchPlan(List.of(create, update), List.of())));
    }

    @Test
    void tryCompileRejectsDeleteAsGeneratedIdentityDependencySource() {
        EntityId parentId = new EntityId(31), childId = new EntityId(32);
        ColumnId parentPk = new ColumnId(1), parentName = new ColumnId(2);
        ColumnId childPk = new ColumnId(3), childParent = new ColumnId(4);
        FieldId parentPkField = new FieldId(1), parentNameField = new FieldId(2), childPkField = new FieldId(3), childParentField = new FieldId(4);
        MetadataRegistry metadata = new MetadataRegistry();
        metadata.register(new EntityMetadata(parentId, "Parent",
                List.of(new ColumnMetadata(parentPk, "Id"), new ColumnMetadata(parentName, "Name")), null,
                List.of(new FieldMetadata(parentPkField, "Id", Long.class, new ColumnReference(parentId, parentPk)),
                        new FieldMetadata(parentNameField, "Name", String.class, new ColumnReference(parentId, parentName))),
                new ColumnReference(parentId, parentPk), null, false, null, null));
        metadata.register(new EntityMetadata(childId, "Child",
                List.of(new ColumnMetadata(childPk, "Id"), new ColumnMetadata(childParent, "ParentId")), null,
                List.of(new FieldMetadata(childPkField, "Id", Long.class, new ColumnReference(childId, childPk)),
                        new FieldMetadata(childParentField, "ParentId", Long.class, new ColumnReference(childId, childParent))),
                new ColumnReference(childId, childPk), null, false, null, null));

        MutationEntitySchema parent = new MutationEntitySchema(parentId, "Parent",
                Set.of(parentPk, parentName), Map.of(parentPkField, parentPk, parentNameField, parentName), parentPk);
        MutationEntitySchema child = new MutationEntitySchema(childId, "Child",
                Set.of(childPk, childParent), Map.of(childPkField, childPk, childParentField, childParent), childPk);
        MutationOperation delete = new MutationOperation(parent, MutationKind.DELETE,
                List.of(), List.of(parentPk), null, List.of(parentPkField));
        MutationOperation createChild = new MutationOperation(child, MutationKind.CREATE,
                List.of(MutationFieldValue.fromPrevious(childParent, 0, parentPkField)), null, null, List.of(childPkField, childParentField));
        MutationDependency dependency = new MutationDependency(0, 1, parentPkField, childParent);

        assertNull(new PostgresBatchedMutationCompiler(metadata).tryCompile(
                new MutationBatchPlan(List.of(delete, createChild), List.of(dependency))));
    }

    @Test
    void emptyBatchIsNotSilentlyAccepted() {
        MetadataRegistry metadata = new MetadataRegistry();
        assertThrows(IllegalStateException.class, () ->
                new PostgresBatchedMutationCompiler(metadata).compile(new MutationBatchPlan(List.of(), List.of())));
    }

    private static MetadataRegistry simpleMetadata(EntityId entityId, String name,
                                                     ColumnId id, ColumnId value,
                                                     FieldId idField, FieldId valueField) {
        MetadataRegistry metadata = new MetadataRegistry();
        metadata.register(new EntityMetadata(entityId, name,
                List.of(new ColumnMetadata(id, "Id"), new ColumnMetadata(value, "Name")), null,
                List.of(new FieldMetadata(idField, "Id", Long.class, new ColumnReference(entityId, id)),
                        new FieldMetadata(valueField, "Name", String.class, new ColumnReference(entityId, value))),
                new ColumnReference(entityId, id), null, false, null, null));
        return metadata;
    }

}

