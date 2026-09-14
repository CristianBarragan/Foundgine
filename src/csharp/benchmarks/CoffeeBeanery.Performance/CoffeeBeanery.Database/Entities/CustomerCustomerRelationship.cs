using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeBeanery.Database;

public partial class CustomerCustomerRelationship : Process
{
    public CustomerCustomerRelationship()
    {
        Schema = CoffeeBeanery.Database.Schema.Banking;
    }

    public int Id { get; set; }

    public Guid? CustomerCustomerRelationshipKey { get; set; }

    public int? OuterCustomerId { get; set; }
    public Customer? OuterCustomer { get; set; }

    public int? InnerCustomerId { get; set; }

    public Customer? InnerCustomer { get; set; }

    public CustomerCustomerRelationshipType? CustomerCustomerRelationshipType { get; set; }
}

public enum CustomerCustomerRelationshipType
{
    Family,
    Partner,
    Widow,
    Single,
    Divorced
}

public class CustomerCustomerRelationshipEntityConfiguration : IEntityTypeConfiguration<CustomerCustomerRelationship>
{
    private readonly string _schema;

    public CustomerCustomerRelationshipEntityConfiguration(string schema)
    {
        _schema = schema;
    }

    public void Configure(EntityTypeBuilder<CustomerCustomerRelationship> builder)
    {
        builder.ToTable(nameof(CustomerCustomerRelationship), _schema);
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.CustomerCustomerRelationshipKey }).IsUnique();
        builder.HasIndex(c => new { c.OuterCustomerId, c.InnerCustomerId }).IsUnique();
        builder.Property(c => c.ProcessedDateTime).HasDefaultValueSql("(now() at time zone 'utc')");

        builder.HasOne(c => c.InnerCustomer)
            .WithMany()
            .HasForeignKey(c => c.InnerCustomerId);

        builder.HasOne(c => c.OuterCustomer)
            .WithMany()
            .HasForeignKey(c => c.OuterCustomerId);
    }
}