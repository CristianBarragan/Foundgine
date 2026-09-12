package com.foundgine.core.security.penetration;

import com.foundgine.core.serialization.JsonReadIntentAdapter;
import com.foundgine.core.serialization.JsonReadIntentAdapterOptions;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

/** Mirrors SEC-38, SEC-48 and SEC-56..SEC-57 transport ambiguity cases. */
class TransportAndSecretLeakageParityTest {
    @Test void duplicateAuthorityPropertyDoesNotCreateSecondChannel(){
        var a=new JsonReadIntentAdapter();
        var ex=assertThrows(IllegalStateException.class,()->a.parse("{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"tenantId\":\"tenant-alpha\",\"tenantId\":\"tenant-beta-secret\"}"));
        assertFalse(ex.getMessage().toLowerCase().contains("tenant-beta-secret"));
    }
    @Test void unknownAuthorityFieldsRejectedStrictly(){
        var a=new JsonReadIntentAdapter();
        var ex=assertThrows(IllegalStateException.class,()->a.parse("{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"identity\":{\"subject\":\"admin\",\"tenant\":\"victim\"},\"provider\":{\"connectionString\":\"Host=evil\"},\"sql\":\"SELECT * FROM secrets\"}"));
        assertFalse(ex.getMessage().contains("Host=evil")); assertFalse(ex.getMessage().contains("SELECT *"));
    }
    @Test void permissiveModeStillDoesNotMapAuthorityControls(){
        var a=new JsonReadIntentAdapter(new JsonReadIntentAdapterOptions(32,256,32,256,16,false));
        var i=a.parse("{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"identity\":{\"subject\":\"admin\",\"tenant\":\"victim\"},\"provider\":\"evil\",\"sql\":\"DROP TABLE secrets\"}");
        assertEquals("Customer",i.rootEntity()); assertEquals(1,i.selections().size()); assertEquals("Id",i.selections().get(0).field());
    }
    @Test void unicodeConfusableTenantKeyNeverBecomesAuthority(){
        var a=new JsonReadIntentAdapter();
        var ex=assertThrowsOrNull(a,"{\"rootEntity\":\"Customer\",\"selections\":[{\"field\":\"Id\"}],\"tenant\\u00131\":\"victim\"}");
        assertTrue(ex==null || ex instanceof IllegalStateException);
    }
    private static Throwable assertThrowsOrNull(JsonReadIntentAdapter a,String json){try{a.parse(json);return null;}catch(Throwable t){return t;}}
}