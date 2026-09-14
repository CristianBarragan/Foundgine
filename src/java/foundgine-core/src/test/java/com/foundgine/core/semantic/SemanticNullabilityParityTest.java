package com.foundgine.core.semantic;

import static org.junit.jupiter.api.Assertions.*;

import com.foundgine.core.abstractions.EntityId;
import com.foundgine.core.abstractions.FieldId;

import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;

/**
 * Port of C# {@code Foundgine.Core.Semantic.Tests.SemanticNullabilityTests}.
 *
 * <p><b>Porting decisions - this is a partial port, not a 1:1 translation:</b>
 *
 * <ul>
 *   <li>The C# original's two "typed builder" cases rely on the C# compiler's
 *       nullable-reference-type (NRT) annotations: {@code string?} vs {@code string} compile to the
 *       same CLR type but differ in metadata that {@code SemanticModelBuilder.Entity<T>(...)} reads
 *       via reflection. Java has no equivalent language-level nullability metadata on a plain
 *       {@code String} field, so there is no faithful Java translation of "infer nullability from a
 *       typed member's reflection annotation" - those two cases are not portable as written.
 *   <li>The Java {@code SemanticField} record already carries a {@code nullableOverride} field and
 *       {@code isNullable()} correctly falls back to {@code !clrType.isPrimitive()} when it is
 *       {@code null} (see {@code SemanticField.isNullable()}), and {@code SemanticModelFingerprint}
 *       already folds {@code isNullable()} into the contract fingerprint - so the underlying
 *       feature this test exists to protect is present. What is ported below exercises that
 *       reachable surface directly.
 *   <li><b>Gap worth flagging separately (not a test gap - an API gap):</b> {@code
 *       SemanticEntityBuilder} has no public method to set {@code nullableOverride} on a field. The
 *       only way to construct a {@code SemanticField} with an explicit override is to bypass the
 *       builder entirely, as this test does. Until the builder exposes something like {@code
 *       .field(id, name, type, nullable)}, callers of the public API can never mark a
 *       reference-typed field as non-nullable or a primitive field as nullable - the
 *       fingerprint-sensitivity contract is real but currently unreachable from {@code
 *       SemanticModelBuilder}.
 * </ul>
 */
class SemanticNullabilityParityTest {

    private static final EntityId PRODUCT = new EntityId(1);
    private static final FieldId ID = new FieldId(1);
    private static final FieldId NAME = new FieldId(2);

    @Test
    void primitiveFieldsDefaultToNonNullableAndReferenceFieldsDefaultToNullable() {
        var idField = new SemanticField(ID, "Id", int.class);
        var nameField = new SemanticField(NAME, "Name", String.class);

        assertFalse(idField.isNullable());
        assertTrue(nameField.isNullable());
    }

    @Test
    void explicitNullableOverrideIsRespectedRegardlessOfClrType() {
        // Bypasses SemanticEntityBuilder - see class javadoc: there is currently no
        // public builder method that sets this.
        var forcedNonNullable =
                new SemanticField(
                        NAME,
                        "Name",
                        String.class,
                        null,
                        SemanticFieldCapabilities.DEFAULT,
                        List.of(),
                        List.of(),
                        false);
        var forcedNullable =
                new SemanticField(
                        ID,
                        "Id",
                        int.class,
                        null,
                        SemanticFieldCapabilities.DEFAULT,
                        List.of(),
                        List.of(),
                        true);

        assertFalse(forcedNonNullable.isNullable());
        assertTrue(forcedNullable.isNullable());
    }

    @Test
    void nullabilityIsPartOfTheContractFingerprint() {
        var nullableName =
                new SemanticField(
                        NAME,
                        "Name",
                        String.class,
                        null,
                        SemanticFieldCapabilities.DEFAULT,
                        List.of(),
                        List.of(),
                        true);
        var nonNullableName =
                new SemanticField(
                        NAME,
                        "Name",
                        String.class,
                        null,
                        SemanticFieldCapabilities.DEFAULT,
                        List.of(),
                        List.of(),
                        false);

        var nullableModel = modelWith(nullableName);
        var nonNullableModel = modelWith(nonNullableName);

        assertNotEquals(
                nullableModel.contractFingerprint(), nonNullableModel.contractFingerprint());
    }

    private static SemanticModel modelWith(SemanticField nameField) {
        var identity = new SemanticFieldIdentity(ID, "Id");
        var entity =
                new SemanticEntity(
                        PRODUCT,
                        "Product",
                        identity,
                        List.of(new SemanticField(ID, "Id", int.class), nameField),
                        List.of());
        return new SemanticModel(Map.of(PRODUCT, entity), List.of());
    }
}
