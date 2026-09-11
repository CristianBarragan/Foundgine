package com.foundgine.samples.supplychain.advanced.postgres;

import com.foundgine.samples.supplychain.advanced.authorization.Authorization;
import java.math.BigDecimal;
import java.sql.*;
import java.time.LocalDate;
import java.util.*;

/**
 * PostgreSQL high-assurance mutation boundary for the Advanced sample.
 *
 * <p>This deliberately owns the physical transaction, row locks, server-side
 * pricing and exact inventory-lot allocation. Semantic planning and
 * authorization remain above this class.
 */
public final class PostgresSupplyChainStore {
    private final Connection connection;

    public PostgresSupplyChainStore(Connection connection) {
        this.connection = Objects.requireNonNull(connection);
    }

    public record PlaceOrderResult(int orderId, boolean replay, BigDecimal total) {}
    public record CancelOrderResult(int orderId, boolean replay, int restoredQuantity) {}

    public PlaceOrderResult placeOrder(String actor, Authorization.Context auth, int customerId,
                                       int productId, int quantity, String idempotencyKey) {
        if (quantity <= 0) throw new IllegalArgumentException("Quantity must be positive.");
        if (idempotencyKey == null || idempotencyKey.isBlank()) throw new IllegalArgumentException("Idempotency key is required.");
        ensureWritable(auth);
        boolean oldAuto = true;
        try {
            oldAuto = connection.getAutoCommit();
            if (oldAuto) connection.setAutoCommit(false);
            try (PreparedStatement lock = connection.prepareStatement("SELECT pg_advisory_xact_lock(hashtext(?))")) {
                lock.setString(1, idempotencyKey); lock.execute();
            }
            Integer existing = scalarInt("SELECT order_id FROM supply_chain_idempotency WHERE idempotency_key=? FOR SHARE", idempotencyKey);
            if (existing != null) { connection.commit(); return new PlaceOrderResult(existing, true, orderTotal(existing)); }

            String tenant = scalarString("SELECT tenant_id FROM customers WHERE customer_id=?", customerId);
            if (tenant == null) throw new IllegalArgumentException("Customer not found.");
            if (!tenant.equals(auth.tenantId())) throw new SecurityException("Customer belongs to another tenant.");
            if (auth.role() == Authorization.Role.CUSTOMER && !ownsActor(actor, customerId)) throw new SecurityException("Customer ownership check failed.");

            BigDecimal price;
            try (PreparedStatement ps = connection.prepareStatement("SELECT unit_price FROM products WHERE product_id=?")) {
                ps.setInt(1, productId); try (ResultSet rs = ps.executeQuery()) { if (!rs.next()) throw new IllegalArgumentException("Product not found."); price = rs.getBigDecimal(1); }
            }
            int lotId; int warehouseId;
            try (PreparedStatement ps = connection.prepareStatement(
                    "SELECT l.lot_id,l.warehouse_id FROM inventory_lots l JOIN warehouses w ON w.warehouse_id=l.warehouse_id " +
                    "WHERE l.product_id=? AND w.tenant_id=? AND l.warehouse_id = ANY(?) " +
                    "AND (l.on_hand-l.reserved-l.quarantined)>=? ORDER BY l.on_hand DESC,l.warehouse_id,l.lot_id FOR UPDATE SKIP LOCKED LIMIT 1")) {
                ps.setInt(1, productId); ps.setString(2, auth.tenantId()); ps.setArray(3, connection.createArrayOf("integer", auth.allowedWarehouses().toArray())); ps.setInt(4, quantity);
                try (ResultSet rs = ps.executeQuery()) { if (!rs.next()) throw new IllegalStateException("Insufficient inventory for product " + productId + "."); lotId=rs.getInt(1); warehouseId=rs.getInt(2); }
            }
            int orderId;
            BigDecimal total = price.multiply(BigDecimal.valueOf(quantity));
            try (PreparedStatement ps = connection.prepareStatement("INSERT INTO orders(customer_id,status,total_amount,placed_on) VALUES(?,'Pending',?,CURRENT_DATE) RETURNING order_id")) {
                ps.setInt(1, customerId); ps.setBigDecimal(2,total); try(ResultSet rs=ps.executeQuery()){rs.next();orderId=rs.getInt(1);}
            }
            int itemId;
            try (PreparedStatement ps = connection.prepareStatement("INSERT INTO order_items(order_id,product_id,quantity,unit_price) VALUES(?,?,?,?) RETURNING order_item_id")) {
                ps.setInt(1,orderId);ps.setInt(2,productId);ps.setInt(3,quantity);ps.setBigDecimal(4,price);try(ResultSet rs=ps.executeQuery()){rs.next();itemId=rs.getInt(1);}
            }
            try (PreparedStatement ps=connection.prepareStatement("INSERT INTO order_allocations(order_item_id,lot_id,quantity) VALUES(?,?,?)")) {ps.setInt(1,itemId);ps.setInt(2,lotId);ps.setInt(3,quantity);ps.executeUpdate();}
            try (PreparedStatement ps=connection.prepareStatement("UPDATE inventory_lots SET on_hand=on_hand-? WHERE lot_id=? AND (on_hand-reserved-quarantined)>=?")) {ps.setInt(1,quantity);ps.setInt(2,lotId);ps.setInt(3,quantity);if(ps.executeUpdate()!=1)throw new IllegalStateException("Inventory changed before reservation could be committed.");}
            try (PreparedStatement ps=connection.prepareStatement("INSERT INTO supply_chain_idempotency(idempotency_key,actor,customer_id,order_id) VALUES(?,?,?,?)")) {ps.setString(1,idempotencyKey);ps.setString(2,actor);ps.setInt(3,customerId);ps.setInt(4,orderId);ps.executeUpdate();}
            connection.commit(); return new PlaceOrderResult(orderId,false,total);
        } catch (RuntimeException|SQLException e) { rollbackQuietly(); if(e instanceof RuntimeException r)throw r; throw new IllegalStateException("PostgreSQL place_order failed",e); }
        finally { restoreAutoCommit(oldAuto); }
    }

    public CancelOrderResult cancelOrder(String actor, Authorization.Context auth, int orderId, String idempotencyKey) {
        if (idempotencyKey == null || idempotencyKey.isBlank()) throw new IllegalArgumentException("Idempotency key is required.");
        ensureWritable(auth); boolean oldAuto=true;
        try {
            oldAuto=connection.getAutoCommit(); if(oldAuto)connection.setAutoCommit(false);
            Integer replay=scalarInt("SELECT order_id FROM supply_chain_cancellation_idempotency WHERE idempotency_key=? FOR SHARE",idempotencyKey);
            if(replay!=null){connection.commit();return new CancelOrderResult(replay,true,0);}
            Integer customerId=scalarInt("SELECT customer_id FROM orders WHERE order_id=? FOR UPDATE",orderId);
            String status=scalarString("SELECT status FROM orders WHERE order_id=?",orderId);
            if(customerId==null||status==null||!"Pending".equals(status))throw new SecurityException("Order is not owned by the customer or is not cancellable.");
            String tenant=scalarString("SELECT tenant_id FROM customers WHERE customer_id=?",customerId);
            if(!auth.tenantId().equals(tenant))throw new SecurityException("Order belongs to another tenant.");
            if(auth.role()==Authorization.Role.CUSTOMER&&!ownsActor(actor,customerId))throw new SecurityException("Customer ownership check failed.");
            int restored=0;
            try(PreparedStatement ps=connection.prepareStatement("SELECT a.lot_id,a.quantity FROM order_allocations a JOIN order_items i ON i.order_item_id=a.order_item_id WHERE i.order_id=?")){ps.setInt(1,orderId);try(ResultSet rs=ps.executeQuery()){while(rs.next()){int lot=rs.getInt(1),qty=rs.getInt(2);try(PreparedStatement up=connection.prepareStatement("UPDATE inventory_lots SET on_hand=on_hand+? WHERE lot_id=?")){up.setInt(1,qty);up.setInt(2,lot);up.executeUpdate();}restored+=qty;}}}
            try(PreparedStatement ps=connection.prepareStatement("UPDATE orders SET status='Cancelled' WHERE order_id=? AND status='Pending'")){ps.setInt(1,orderId);if(ps.executeUpdate()!=1)throw new IllegalStateException("Order changed before cancellation could be committed.");}
            try(PreparedStatement ps=connection.prepareStatement("INSERT INTO supply_chain_cancellation_idempotency(idempotency_key,actor,order_id,restored_quantity,cancelled_on) VALUES(?,?,?,?,CURRENT_DATE)")){ps.setString(1,idempotencyKey);ps.setString(2,actor);ps.setInt(3,orderId);ps.setInt(4,restored);ps.executeUpdate();}
            connection.commit();return new CancelOrderResult(orderId,false,restored);
        } catch(RuntimeException|SQLException e){rollbackQuietly();if(e instanceof RuntimeException r)throw r;throw new IllegalStateException("PostgreSQL cancel_order failed",e);} finally{restoreAutoCommit(oldAuto);}
    }

    private BigDecimal orderTotal(int id){try{try(PreparedStatement ps=connection.prepareStatement("SELECT total_amount FROM orders WHERE order_id=?")){ps.setInt(1,id);try(ResultSet rs=ps.executeQuery()){return rs.next()?rs.getBigDecimal(1):BigDecimal.ZERO;}}}catch(SQLException e){throw new IllegalStateException(e);}}
    private Integer scalarInt(String sql,Object arg){try(PreparedStatement ps=connection.prepareStatement(sql)){ps.setObject(1,arg);try(ResultSet rs=ps.executeQuery()){return rs.next()?rs.getInt(1):null;}}catch(SQLException e){throw new IllegalStateException(e);}}
    private String scalarString(String sql,Object arg){try(PreparedStatement ps=connection.prepareStatement(sql)){ps.setObject(1,arg);try(ResultSet rs=ps.executeQuery()){return rs.next()?rs.getString(1):null;}}catch(SQLException e){throw new IllegalStateException(e);}}
    private static boolean ownsActor(String actor,int id){return actor.equalsIgnoreCase("customer"+id)||actor.equalsIgnoreCase("customer-"+id)||(actor.equalsIgnoreCase("alice")&&id==1)||(actor.equalsIgnoreCase("bob")&&id==2);}
    private static void ensureWritable(Authorization.Context auth){if(auth.readOnly()||(auth.role()!=Authorization.Role.CUSTOMER&&auth.role()!=Authorization.Role.SUPPLY_CHAIN_MANAGER))throw new SecurityException("Caller is not authorized to mutate orders.");}
    private void rollbackQuietly(){try{connection.rollback();}catch(SQLException ignored){}}
    private void restoreAutoCommit(boolean old){try{if(connection.getAutoCommit()!=old)connection.setAutoCommit(old);}catch(SQLException ignored){}}
}
