package com.foundgine.providers.storage.sql.mutation;
import com.foundgine.core.abstractions.FieldId;
import com.foundgine.core.execution.CancellationToken;
import com.foundgine.core.execution.ExecutionContext;
import com.foundgine.core.execution.mutation.*;
import java.sql.*; import java.util.*;
/** JDBC execution boundary for provider-neutral SQL mutation plans. */
public class SqlMutationExecutionProvider implements IMutationExecutionProvider {
 private final Connection connection; private final boolean ownsTransaction;
 public SqlMutationExecutionProvider(Connection connection){this(connection,true);}
 public SqlMutationExecutionProvider(Connection connection, boolean ownsTransaction){this.connection=Objects.requireNonNull(connection);this.ownsTransaction=ownsTransaction;}
 @Override public MutationResult execute(ProviderMutationPlan plan, ExecutionContext context){ if(!(plan instanceof SqlMutationPlan p)) throw new IllegalArgumentException("Expected SqlMutationPlan"); return executeOne(p); }
 private MutationResult executeOne(SqlMutationPlan p){
  try(PreparedStatement s=connection.prepareStatement(p.commandText(),Statement.RETURN_GENERATED_KEYS)){
   bind(s,p.parameters()); int affected=s.executeUpdate(); Map<FieldId,Object> values=new LinkedHashMap<>();
   if(!p.returnedFields().isEmpty()) try(ResultSet rs=s.getGeneratedKeys()){ if(rs.next()) for(int i=0;i<p.returnedFields().size();i++) values.put(p.returnedFields().get(i).fieldId(),rs.getObject(i+1)); }
   return new MutationResult(affected,values);
  } catch(SQLException e){throw new IllegalStateException("SQL mutation execution failed",e);}
 }
 public MutationBatchResult executeBatch(ExecutionMutationIR ir,ExecutionContext context){throw new UnsupportedOperationException("Compile ExecutionMutationIR with SqlMutationCompiler before execution.");}
 public MutationBatchResult executeBatch(ProviderMutationBatchPlan plan,ExecutionContext context,CancellationToken token){
  token.throwIfCancellationRequested(); if(!(plan instanceof SqlMutationBatchPlan batch)) throw new IllegalArgumentException("Expected SqlMutationBatchPlan");
  boolean oldAuto; try{oldAuto=connection.getAutoCommit(); if(ownsTransaction) connection.setAutoCommit(false); List<MutationResult> r=new ArrayList<>(); for(SqlMutationPlan p:batch.operations()){token.throwIfCancellationRequested();r.add(executeOne(p));} if(ownsTransaction) connection.commit(); return new MutationBatchResult(r);}catch(Exception e){try{if(ownsTransaction)connection.rollback();}catch(SQLException ignored){} if(e instanceof RuntimeException re)throw re;throw new IllegalStateException(e);}finally{try{if(ownsTransaction)connection.setAutoCommit(true);}catch(SQLException ignored){}}
 }
 private static void bind(PreparedStatement s,List<com.foundgine.providers.storage.sql.query.SqlParameterBinding> ps)throws SQLException{for(int i=0;i<ps.size();i++)s.setObject(i+1,ps.get(i).value());}
}
