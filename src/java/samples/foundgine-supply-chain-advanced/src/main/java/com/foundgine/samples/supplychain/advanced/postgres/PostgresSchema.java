package com.foundgine.samples.supplychain.advanced.postgres;

import java.io.*; import java.nio.charset.StandardCharsets; import java.sql.*;
/** Small JDBC bootstrap used by the Java Advanced PostgreSQL sample. */
public final class PostgresSchema {
    private PostgresSchema() {}
    public static void apply(Connection connection) throws SQLException {
        executeResource(connection,"/database/schema.sql");
        executeResource(connection,"/database/seed.sql");
    }
    private static void executeResource(Connection c,String name) throws SQLException {
        try(InputStream in=PostgresSchema.class.getResourceAsStream(name)) {
            if(in==null)throw new IllegalStateException("Missing PostgreSQL resource: "+name);
            try(Statement s=c.createStatement()){for(String sql:new String(in.readAllBytes(),StandardCharsets.UTF_8).split(";")){if(!sql.isBlank())s.execute(sql);}}
        }catch(IOException e){throw new SQLException("Unable to read "+name,e);}
    }
}
