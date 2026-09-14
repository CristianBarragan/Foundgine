package com.foundgine.samples.supplychain.openintent;

import java.util.*;

/** Open natural-language intent boundary: no mandatory root entity. */
public final class OpenIntent {
    private OpenIntent() {}
    public record Request(String text, Map<String,Object> constraints) {
        public Request { Objects.requireNonNull(text); constraints = constraints == null ? Map.of() : Map.copyOf(constraints); }
    }
    public record Selection(String path, List<String> fields) { public Selection { fields=List.copyOf(fields); } }
    public record GroundedIntent(String text, List<Selection> selections, Map<String,Object> filters,
                                 List<String> orderBy, Integer limit, Integer offset) {
        public GroundedIntent { selections=List.copyOf(selections); filters=Map.copyOf(filters); orderBy=List.copyOf(orderBy); }
    }
    public static GroundedIntent validate(GroundedIntent intent) {
        Objects.requireNonNull(intent);
        if (intent.selections().isEmpty()) throw new IllegalArgumentException("Open intent must contain at least one selection.");
        if (intent.limit()!=null && (intent.limit()<1 || intent.limit()>1000)) throw new IllegalArgumentException("limit must be 1..1000");
        if (intent.offset()!=null && intent.offset()<0) throw new IllegalArgumentException("offset cannot be negative");
        return intent;
    }
}
