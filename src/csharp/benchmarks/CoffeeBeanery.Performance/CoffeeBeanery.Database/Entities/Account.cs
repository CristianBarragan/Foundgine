using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeBeanery.Database;

public partial class Account : Process
{
    public Account()
    {
        Schema = CoffeeBeanery.Database.Schema.Accounting;
    }

    public int Id { get; set; }

    public Guid AccountKey { get; set; }

    public string? AccountNumber { get; set; }

    public string? AccountName { get; set; }

    public Contract? Contract { get; set; }

    public List<Transaction>? Transaction { get; set; }
}

public class AccountEntityConfiguration : IEntityTypeConfiguration<Account>
{
    private readonly string _schema;

    public AccountEntityConfiguration(string schema)
    {
        _schema = schema;
    }

    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable(nameof(Account), _schema);

        builder.HasKey(c => c.Id);

        builder.HasIndex(c => c.AccountKey).IsUnique();

        builder.HasOne(c => c.Contract).WithOne(c => c.Account).HasForeignKey<Contract>(c => c.AccountId);

        builder.HasMany(c => c.Transaction).WithOne(c => c.Account).HasForeignKey(c => c.AccountId);

        builder.Property(c => c.ProcessedDateTime).HasDefaultValueSql("(now() at time zone 'utc')");
    }
}