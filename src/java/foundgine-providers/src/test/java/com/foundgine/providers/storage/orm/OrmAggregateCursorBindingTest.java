package com.foundgine.providers.storage.orm;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import java.math.BigDecimal;
import java.util.Arrays;
import java.util.List;
import org.junit.jupiter.api.Test;

class OrmAggregateCursorBindingTest {
    @Test
    void bindsCursorToCanonicalPlanIdentityAndDecodesMappedTypes() {
        String plan = OrmAggregateCursorBinding.fingerprint(
                "v1|entity=Customer|auth=tenant:7|filter=active|order=count(lines):asc,id:asc");
        String cursor = OrmAggregateCursorBinding.encode(plan,
                Arrays.asList(12L, null, new BigDecimal("10.250")));

        assertEquals(Arrays.asList(12L, null, new BigDecimal("10.250")),
                OrmAggregateCursorBinding.decode(cursor, plan,
                        List.of(Long.class, String.class, BigDecimal.class)));
    }

    @Test
    void rejectsReplayAgainstDifferentAuthorizationOrSortPlan() {
        String original = OrmAggregateCursorBinding.fingerprint("entity=Customer|auth=tenant:7|order=count:asc");
        String cursor = OrmAggregateCursorBinding.encode(original, List.of(4L));
        String changed = OrmAggregateCursorBinding.fingerprint("entity=Customer|auth=tenant:8|order=count:asc");
        assertThrows(IllegalArgumentException.class,
                () -> OrmAggregateCursorBinding.decode(cursor, changed, List.of(Long.class)));
    }

    @Test
    void rejectsMalformedEnvelopeAndWrongArity() {
        String plan = OrmAggregateCursorBinding.fingerprint("entity=Customer|auth=tenant:7");
        String cursor = OrmAggregateCursorBinding.encode(plan, List.of(1L));
        assertThrows(IllegalArgumentException.class,
                () -> OrmAggregateCursorBinding.decode(cursor, plan, List.of(Long.class, Long.class)));
        assertThrows(IllegalArgumentException.class,
                () -> OrmAggregateCursorBinding.decode("not-base64!", plan, List.of(Long.class)));
    }

    @Test
    void invalidatesCursorWhenAnyCanonicalPlanDimensionChanges() {
        String base = canonical("tenant=7", "filter=status:open", "order=count:asc",
                "relationship=customer-lines", "schema=1");
        String cursor = OrmAggregateCursorBinding.encode(
                OrmAggregateCursorBinding.fingerprint(base), List.of(9L));

        List<String> changedPlans = List.of(
                canonical("tenant=8", "filter=status:open", "order=count:asc", "relationship=customer-lines", "schema=1"),
                canonical("tenant=7", "filter=status:closed", "order=count:asc", "relationship=customer-lines", "schema=1"),
                canonical("tenant=7", "filter=status:open", "order=count:desc", "relationship=customer-lines", "schema=1"),
                canonical("tenant=7", "filter=status:open", "order=count:asc", "relationship=customer-invoices", "schema=1"),
                canonical("tenant=7", "filter=status:open", "order=count:asc", "relationship=customer-lines", "schema=2"));
        for (String changed : changedPlans) {
            assertThrows(IllegalArgumentException.class,
                    () -> OrmAggregateCursorBinding.decode(cursor,
                            OrmAggregateCursorBinding.fingerprint(changed), List.of(Long.class)));
        }
    }

    @Test
    void lengthPrefixedCanonicalAtomsCannotCollideOnEmbeddedDelimiters() {
        String first = canonical("filter=a|b", "order=count:asc");
        String second = canonical("filter=a", "b|order=count:asc");
        assertThrows(IllegalArgumentException.class,
                () -> OrmAggregateCursorBinding.decode(
                        OrmAggregateCursorBinding.encode(OrmAggregateCursorBinding.fingerprint(first), List.of(1L)),
                        OrmAggregateCursorBinding.fingerprint(second), List.of(Long.class)));
    }

    private static String canonical(String... atoms) {
        var result = new StringBuilder();
        for (String atom : atoms) result.append(atom.length()).append(':').append(atom);
        return result.toString();
    }

}
