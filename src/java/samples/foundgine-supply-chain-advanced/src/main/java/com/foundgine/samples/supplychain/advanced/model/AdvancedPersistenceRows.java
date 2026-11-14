package com.foundgine.samples.supplychain.advanced.model;

import java.math.BigDecimal;
import java.time.LocalDate;
import java.time.OffsetDateTime;

/**
 * Physical row shapes for the transactional V1 tables. These are deliberately separate from
 * the semantic domain records: their names and columns follow the PostgreSQL contract exactly.
 */
final class AdvancedPersistenceRows {
    private AdvancedPersistenceRows() {}

    record CustomerRow(int customerId, String customerName, String tenantId) {}

    record OrderRow(int orderId, int customerId, String status, BigDecimal totalAmount, LocalDate placedOn) {}

    record OrderItemRow(int orderItemId, int orderId, int productId, int quantity, BigDecimal unitPrice) {}

    record OrderAllocationRow(int orderItemId, int lotId, int quantity) {}

    record IdempotencyRow(
            String idempotencyKey, String actor, int customerId, int orderId,
            String requestFingerprint, OffsetDateTime createdAt) {}

    record CancellationIdempotencyRow(
            String idempotencyKey, String actor, int orderId, String requestFingerprint,
            int restoredQuantity, LocalDate cancelledOn) {}

    /** Physical supplier row used by the PostgreSQL ambiguity-resolution query. */
    record SupplierRow(int supplierId, String supplierName, String state,
                       BigDecimal totalOrderValue, BigDecimal negotiatedCost) {}

    /** Physical purchase-order row used by the overdue-supplier query. */
    record PurchaseOrderRow(int purchaseOrderId, int supplierId, LocalDate expectedDate,
                            LocalDate receivedDate, String status) {}
}
