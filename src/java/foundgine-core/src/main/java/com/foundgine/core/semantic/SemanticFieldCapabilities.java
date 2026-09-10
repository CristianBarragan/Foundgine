package com.foundgine.core.semantic;
/** Bit flags matching the C# SemanticFieldCapabilities enum. */
public final class SemanticFieldCapabilities {
    private SemanticFieldCapabilities() {}
    public static final byte NONE = 0;
    public static final byte FILTERABLE = 1 << 0;
    public static final byte SORTABLE = 1 << 1;
    public static final byte SELECTABLE = 1 << 2;
    public static final byte AGGREGATABLE = 1 << 3;
    public static final byte WRITABLE = 1 << 4;
    public static final byte COMPUTED = 1 << 5;
    public static final byte SENSITIVE = 1 << 6;
    public static final byte DEPRECATED = (byte)(1 << 7);
    public static final byte DEFAULT = FILTERABLE | SORTABLE | SELECTABLE | AGGREGATABLE;
}
