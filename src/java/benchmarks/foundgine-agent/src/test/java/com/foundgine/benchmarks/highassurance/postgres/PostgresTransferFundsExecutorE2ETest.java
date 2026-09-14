package com.foundgine.benchmarks.highassurance.postgres;

import com.foundgine.benchmarks.highassurance.TransferFunds;
import org.junit.jupiter.api.*;

import java.math.BigDecimal;
import java.sql.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Opt-in real PostgreSQL coverage for the physical high-assurance adapter.
 * Set FOUNDGINE_POSTGRES_CONNECTION_STRING to run.
 */
@TestMethodOrder(MethodOrderer.OrderAnnotation.class)
class PostgresTransferFundsExecutorE2ETest {
    private static final UUID ACTOR=UUID.fromString("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static final int TENANT=42;
    private static final UUID SOURCE=UUID.fromString("00000000-0000-0000-0000-000000000100");
    private static final UUID DEST=UUID.fromString("00000000-0000-0000-0000-000000000101");
    private static final String FP="authorization-v1";
    private String url;

    @BeforeEach void setup() throws Exception {
        url=System.getenv("FOUNDGINE_POSTGRES_CONNECTION_STRING");
        Assumptions.assumeTrue(url!=null && !url.isBlank(), "FOUNDGINE_POSTGRES_CONNECTION_STRING is not configured");
        try(var c=DriverManager.getConnection(url); var st=c.createStatement()) {
            var schema=getClass().getResourceAsStream("/high-assurance-postgres-schema.sql");
            assertNotNull(schema);
            st.execute(new String(schema.readAllBytes(), java.nio.charset.StandardCharsets.UTF_8));
            st.executeUpdate("TRUNCATE banking.transfer_audit, banking.transfer_idempotency, banking.authorization_context, banking.bank_account CASCADE");
            st.executeUpdate("INSERT INTO banking.bank_account(id,tenant_id,owner_id,balance,daily_limit) VALUES ('"+SOURCE+"',42,'"+ACTOR+"',1000,5000),('"+DEST+"',42,'"+ACTOR+"',250,5000)");
            try(var ps=c.prepareStatement("INSERT INTO banking.authorization_context(actor_id,tenant_id,allowed,version,fingerprint,integrity_algorithm,integrity_key_id,integrity_tag) VALUES (?,?,?,?,?,'HMAC-SHA256/v1','test',repeat('0',64))")) {
                ps.setObject(1,ACTOR); ps.setInt(2,TENANT); ps.setBoolean(3,true); ps.setLong(4,1); ps.setString(5,FP); ps.executeUpdate();
            }
        }
    }

    private PostgresTransferFundsExecutor executor(AtomicBoolean allowed, PostgresTransferFundsExecutor.FaultPoint fault, AtomicBoolean inject) {
        return new PostgresTransferFundsExecutor(url,new Properties(),(actor,source,destination)->
                new PostgresAuthorizationDecision(allowed.get(),1,FP), (point,connection)->{
                    if(fault==point && inject.get()) throw new IllegalStateException("injected fault: "+point);
                });
    }

    @Test @Order(1) void transferIsAtomicAndPersistsIdempotencyAndAudit() throws Exception {
        var allowed=new AtomicBoolean(true); var inject=new AtomicBoolean(false);
        var ex=executor(allowed,null,inject);
        var r=ex.execute(ACTOR,TENANT,new TransferFunds.Command(SOURCE,DEST,new BigDecimal("400"),"e2e-1"));
        assertFalse(r.replay()); assertEquals(new BigDecimal("600.0000"),balance(SOURCE)); assertEquals(new BigDecimal("650.0000"),balance(DEST));
        assertEquals(1,count("transfer_idempotency")); assertEquals(1,count("transfer_audit"));
    }

    @Test @Order(2) void replayDoesNotDoubleDebit() throws Exception {
        var allowed=new AtomicBoolean(true); var inject=new AtomicBoolean(false); var ex=executor(allowed,null,inject);
        var c=new TransferFunds.Command(SOURCE,DEST,new BigDecimal("400"),"e2e-replay");
        var first=ex.execute(ACTOR,TENANT,c); var second=ex.execute(ACTOR,TENANT,c);
        assertFalse(first.replay()); assertTrue(second.replay()); assertEquals(first.transferId(),second.transferId());
        assertEquals(new BigDecimal("600.0000"),balance(SOURCE)); assertEquals(1,count("transfer_audit"));
    }

    @Test @Order(3) void faultAfterMutationRollsBackEverything() throws Exception {
        var allowed=new AtomicBoolean(true); var inject=new AtomicBoolean(true); var ex=executor(allowed,PostgresTransferFundsExecutor.FaultPoint.AFTER_MUTATION_BEFORE_COMMIT,inject);
        assertThrows(IllegalStateException.class,()->ex.execute(ACTOR,TENANT,new TransferFunds.Command(SOURCE,DEST,new BigDecimal("400"),"e2e-fault")));
        assertEquals(new BigDecimal("1000.0000"),balance(SOURCE)); assertEquals(new BigDecimal("250.0000"),balance(DEST));
        assertEquals(0,count("transfer_idempotency")); assertEquals(0,count("transfer_audit"));
    }

    @Test @Order(4) void authorizationRevocationAtCommitGateRollsBack() throws Exception {
        var allowed=new AtomicBoolean(true); var inject=new AtomicBoolean(false);
        var ex=new PostgresTransferFundsExecutor(url,new Properties(),(actor,source,destination)->
                new PostgresAuthorizationDecision(allowed.get(), allowed.get()?1:2, allowed.get()?FP:"authorization-v2"), (point,connection)->{
                    if(point==PostgresTransferFundsExecutor.FaultPoint.BEFORE_AUTHORIZATION_COMMIT_CHECK) allowed.set(false);
                });
        assertThrows(RuntimeException.class,()->ex.execute(ACTOR,TENANT,new TransferFunds.Command(SOURCE,DEST,new BigDecimal("400"),"e2e-revoke")));
        assertEquals(new BigDecimal("1000.0000"),balance(SOURCE)); assertEquals(new BigDecimal("250.0000"),balance(DEST));
        assertEquals(0,count("transfer_idempotency")); assertEquals(0,count("transfer_audit"));
    }

    private BigDecimal balance(UUID id) throws Exception { try(var c=DriverManager.getConnection(url);var ps=c.prepareStatement("SELECT balance FROM banking.bank_account WHERE id=?")){ps.setObject(1,id);try(var rs=ps.executeQuery()){assertTrue(rs.next());return rs.getBigDecimal(1);}} }
    private int count(String table) throws Exception { try(var c=DriverManager.getConnection(url);var st=c.createStatement();var rs=st.executeQuery("SELECT count(*) FROM banking."+table)){assertTrue(rs.next());return rs.getInt(1);} }
}
