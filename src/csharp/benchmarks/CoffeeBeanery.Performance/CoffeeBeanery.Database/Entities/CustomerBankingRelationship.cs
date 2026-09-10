using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeBeanery.Database;

public partial class CustomerBankingRelationship : Process
{
    public CustomerBankingRelationship()
    {
        Schema = CoffeeBeanery.Database.Schema.Banking;
    }

    public int Id { get; set; }

    public Guid CustomerBankingRelationshipKey { get; set; }

    public int? CustomerId { get; set; }

    public Guid? CustomerKey { get; set; }

    public Customer? Customer { get; set; }

    public List<Contract>? Contract { get; set; } = [];
}

public class CustomerBankingRelationshipEntityConfiguration : IEntityTypeConfiguration<CustomerBankingRelationship>
{
    private readonly string _schema;

    public CustomerBankingRelationshipEntityConfiguration(string schema)
    {
        _schema = schema;
    }

    public void Configure(EntityTypeBuilder<CustomerBankingRelationship> builder)
    {
        builder.ToTable(nameof(CustomerBankingRelationship), _schema);

        builder.HasKey(c => c.Id);

        builder.HasOne(c => c.Customer)
            .WithMany(cu => cu.CustomerBankingRelationship)
            .HasForeignKey(c => c.CustomerId);

        builder.HasIndex(c => c.CustomerBankingRelationshipKey).IsUnique();
        builder.HasIndex(c => new { c.CustomerId, c.Id });

        builder.HasMany(c => c.Contract).WithOne(c => c.CustomerBankingRelationship)
            .HasForeignKey(c => c.CustomerBankingRelationshipId);

        builder.Property(c => c.ProcessedDateTime).HasDefaultValueSql("(now() at time zone 'utc')");
    }
}