using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeBeanery.Database;

public partial class Transaction : Process
{
    public Transaction()
    {
        Schema = CoffeeBeanery.Database.Schema.Lending;
    }

    public int Id { get; set; }

    public Guid TransactionKey { get; set; }

    public decimal? Amount { get; set; }

    public decimal? Balance { get; set; }

    public Contract? Contract { get; set; }

    public int? ContractId { get; set; }

    public Guid? ContractKey { get; set; }

    public Account? Account { get; set; }

    public int? AccountId { get; set; }

    public Guid? AccountKey { get; set; }
}

public class TransactionEntityConfiguration : IEntityTypeConfiguration<Transaction>
{
    private readonly string _schema;

    public TransactionEntityConfiguration(string schema)
    {
        _schema = schema;
    }

    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable(nameof(Transaction), _schema);

        builder.HasKey(c => c.Id);

        builder.HasIndex(c => c.TransactionKey).IsUnique();
        builder.HasIndex(c => new { c.ContractId, c.Id });

        builder.Property(c => c.ProcessedDateTime).HasDefaultValueSql("(now() at time zone 'utc')");
    }
}