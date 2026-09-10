package com.foundgine.core.semantic.query;

import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.semantic.SemanticType;
import com.foundgine.core.semantic.expressions.SemanticExpression;
import com.foundgine.core.abstractions.RelationshipId;
import java.util.*;

/** Compatibility request ordering shape retained for adapters. */
public record SemanticOrderTerm(FieldId field, SemanticSortDirection direction, List<RelationshipId> path, SemanticOrderAggregate aggregate) {
    public SemanticOrderTerm { path=path==null?List.of():List.copyOf(path); aggregate=aggregate==null?SemanticOrderAggregate.NONE:aggregate; }
    public SemanticOrderTerm(FieldId field, SemanticSortDirection direction){this(field,direction,List.of(),SemanticOrderAggregate.NONE);}
    public boolean isRootField(){return path.isEmpty();}
    public boolean isAggregate(){return aggregate!=SemanticOrderAggregate.NONE;}
    public List<RelationshipId> effectivePath(){return path;}
    public SemanticExpression toExpression(SemanticExpression source, SemanticType fieldType){
        return switch(aggregate){
            case NONE -> path.isEmpty()?new SemanticExpression.FieldReference(field,fieldType):new SemanticExpression.Path(source,path,fieldType);
            case COUNT -> new SemanticExpression.Aggregate(SemanticExpression.AggregateExpressionKind.COUNT,new SemanticExpression.Path(source,path,new SemanticType.CollectionType(new SemanticType.ObjectType("Target"))),null);
            case MIN -> new SemanticExpression.Aggregate(SemanticExpression.AggregateExpressionKind.MIN,new SemanticExpression.Path(source,path,new SemanticType.CollectionType(new SemanticType.ObjectType("Target"))),new SemanticExpression.FieldReference(field,fieldType));
            case MAX -> new SemanticExpression.Aggregate(SemanticExpression.AggregateExpressionKind.MAX,new SemanticExpression.Path(source,path,new SemanticType.CollectionType(new SemanticType.ObjectType("Target"))),new SemanticExpression.FieldReference(field,fieldType));
        };
    }
}
