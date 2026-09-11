package com.foundgine.core.semantic.intent;
import java.util.*;
public record ReadOrFilter(List<ReadFilter> expressions) implements ReadFilter { public ReadOrFilter { expressions=List.copyOf(expressions==null?List.of():expressions); } }
