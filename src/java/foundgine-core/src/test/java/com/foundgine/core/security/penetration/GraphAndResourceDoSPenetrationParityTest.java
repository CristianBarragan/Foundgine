package com.foundgine.core.security.penetration;

import com.foundgine.core.serialization.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** Port of GraphAndResourceDoSPenetrationTests: hostile nested transport graphs are bounded before execution. */
class GraphAndResourceDoSPenetrationParityTest {
    @Test void deepRelationshipSelectionIsRejectedBeforeSemanticExecution() {
        String value = "{\"field\":\"Id\"}";
        for (int i=1;i<80;i++) value="{\"relationship\":\"Children\",\"children\":["+value+"]}";
        String json="{\"rootEntity\":\"Customer\",\"selections\":["+value+"]}";
        var adapter=new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions(32,256,32,256,16,true));
        assertThrows(IllegalStateException.class,()->adapter.parse(json));
    }
    @Test void deepFilterExpressionIsRejectedBeforePlanning() {
        String value="{\"kind\":\"field\",\"field\":\"Id\",\"operator\":\"EQ\",\"value\":1}";
        for(int i=1;i<80;i++) value="{\"kind\":\"and\",\"expressions\":["+value+"]}";
        String json="{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"filter\":"+value+"}";
        assertThrows(IllegalStateException.class,()->new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions()).parse(json));
    }
}
