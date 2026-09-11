package com.foundgine.core.security.penetration;

import com.foundgine.core.serialization.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** Port of transport-boundary authority and fan-out attacks. */
class JsonAndMcpBoundaryPenetrationParityTest {
    @Test void agentCannotSupplyAuthorityProviderOrSqlControls() {
        String[] forbidden={"tenantId","userId","authorization","provider","connectionString","sql"};
        for(String property:forbidden){
            String json="{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\""+property+"\":\"attacker\"}";
            var ex=assertThrows(IllegalStateException.class,()->new JsonReadIntentAdapter().parse(json));
            assertTrue(ex.getMessage().contains(property),ex.getMessage());
        }
    }
    @Test void relaxedUnknownPropertyModeStillDiscardsSecurityControls() {
        var a=new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions(32,256,32,256,16,false));
        var i=a.parse("""
          {"rootEntity":"Customer","selections":[{"field":"Id"}],
           "tenantId":"attacker","userId":"admin","authorization":"allow",
           "provider":"sqlserver","sql":"DROP TABLE Customer"}
          """);
        assertEquals("Customer",i.rootEntity()); assertEquals(1,i.selections().size()); assertEquals("Id",i.selections().get(0).field()); assertNull(i.security());
    }
    @Test void oversizedSelectionFanoutIsRejectedAtTransportBoundary() {
        StringBuilder b=new StringBuilder("{\"rootEntity\":\"Customer\",\"selections\":[");
        for(int i=1;i<=101;i++){if(i>1)b.append(',');b.append("{\"field\":\"F").append(i).append("\"}");} b.append("]}");
        assertThrows(IllegalStateException.class,()->new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions(32,100,32,256,16,true)).parse(b.toString()));
    }
}
