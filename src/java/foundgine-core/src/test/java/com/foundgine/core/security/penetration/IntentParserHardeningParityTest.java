package com.foundgine.core.security.penetration;

import com.foundgine.core.serialization.JsonReadIntentAdapter;
import com.foundgine.core.serialization.JsonReadIntentAdapterOptions;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** Mirrors parser-hardening cases from Foundgine.Security.Tests/Penetration. */
class IntentParserHardeningParityTest {
    @Test void selectionCannotClaimFieldAndRelationshipAtOnce(){
        var a=new JsonReadIntentAdapter();
        assertThrows(IllegalArgumentException.class,()->a.parse("{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\",\"relationship\":\"Orders\"}]}"));
    }
    @Test void emptySelectionIsRejected(){
        var a=new JsonReadIntentAdapter();
        assertThrows(IllegalArgumentException.class,()->a.parse("{\"rootEntity\":\"Customer\",\"selections\":[{}]}"));
    }
    @Test void negativePaginationIsRejected(){
        var a=new JsonReadIntentAdapter();
        assertThrows(IllegalArgumentException.class,()->a.parse("{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"limit\":-1}"));
        assertThrows(IllegalArgumentException.class,()->a.parse("{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"offset\":-1}"));
    }
    @Test void rawSqlFilterKindIsRejected(){
        var a=new JsonReadIntentAdapter();
        assertThrows(IllegalArgumentException.class,()->a.parse("{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"filter\":{\"kind\":\"rawSql\",\"value\":\"1=1\"}}"));
    }
    @Test void relationshipFilterWithoutPredicateIsRejected(){
        var a=new JsonReadIntentAdapter();
        assertThrows(IllegalArgumentException.class,()->a.parse("{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"filter\":{\"kind\":\"relationship\",\"relationship\":\"Orders\",\"quantifier\":\"Any\"}}"));
    }
    @Test void deepJsonValueIsRejectedBeforeMaterializationCompletes(){
        var value="1"; for(int i=0;i<40;i++) value="{\"nested\":"+value+"}";
        var a=new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions(20,32,16,128,16,true));
        var json="{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"filter\":{\"kind\":\"field\",\"field\":\"Metadata\",\"operator\":\"Eq\",\"value\":"+value+"}}";
        assertThrows(IllegalArgumentException.class,()->a.parse(json));
    }
    @Test void emptyBooleanExpressionListIsRejected(){
        var a=new JsonReadIntentAdapter();
        assertThrows(IllegalArgumentException.class,()->a.parse("{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"filter\":{\"kind\":\"or\",\"expressions\":[]}}"));
    }
}
