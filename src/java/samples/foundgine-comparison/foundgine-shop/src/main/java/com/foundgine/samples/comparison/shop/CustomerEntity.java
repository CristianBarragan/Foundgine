package com.foundgine.samples.comparison.shop;

import com.foundgine.providers.aot.FoundgineOrmEntity; // AOT discovery marker; semantic/persistence configuration lives in ShopModel.
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** JPA entity behind the semantic Customer. Attribute names equal the semantic field names. */
@FoundgineOrmEntity
@Entity
@Table(name = "tbl_customer")
public class CustomerEntity {
    @Id @Column(name = "cust_id") private Integer id;
    @Column(name = "first_nm") private String firstName;
    @Column(name = "last_nm") private String lastName;
    @Column(name = "email_addr") private String email;

    public CustomerEntity() {}

    public Integer getId() { return id; }
    public String getFirstName() { return firstName; }
    public String getLastName() { return lastName; }
    public String getEmail() { return email; }
}
