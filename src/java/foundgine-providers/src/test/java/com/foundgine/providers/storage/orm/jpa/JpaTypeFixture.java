package com.foundgine.providers.storage.orm.jpa;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

import java.math.BigDecimal;
import java.math.BigInteger;
import java.time.Instant;
import java.time.LocalDate;
import java.time.OffsetDateTime;
import java.util.UUID;

@Entity
@Table(name = "jpa_type_fixture")
public class JpaTypeFixture {
    @Id private Long id;
    @Column(precision = 30, scale = 12) private BigDecimal amount;
    @Column(precision = 38) private BigInteger quantity;
    @Enumerated(EnumType.STRING) private JpaTypeFixtureStatus status;
    private UUID externalId;
    private LocalDate businessDate;
    private Instant createdAt;
    private OffsetDateTime occurredAt;

    protected JpaTypeFixture() {}

    JpaTypeFixture(Long id, BigDecimal amount, BigInteger quantity, JpaTypeFixtureStatus status,
                   UUID externalId, LocalDate businessDate, Instant createdAt, OffsetDateTime occurredAt) {
        this.id = id;
        this.amount = amount;
        this.quantity = quantity;
        this.status = status;
        this.externalId = externalId;
        this.businessDate = businessDate;
        this.createdAt = createdAt;
        this.occurredAt = occurredAt;
    }
}
