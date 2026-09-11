package com.foundgine.core.semantic.ir;
import java.util.*;
public final class SemanticReadNodeExtensions {
    private SemanticReadNodeExtensions() {}
    public static List<SemanticReadNode> traverseDepthFirst(SemanticReadNode root) {
        Objects.requireNonNull(root); var out = new ArrayList<SemanticReadNode>(); walk(root,out); return List.copyOf(out);
    }
    private static void walk(SemanticReadNode n,List<SemanticReadNode> out){out.add(n); n.children().forEach(c -> walk(c,out));}
}
