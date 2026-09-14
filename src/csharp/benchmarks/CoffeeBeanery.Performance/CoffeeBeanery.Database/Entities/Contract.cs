using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeBeanery.Database;

public partial class Contract : Process
{
    public Contract()
    {
        Schema = CoffeeBeanery.Database.Schema.Lending;
    }

    public int Id { get; set; }

    public Guid ContractKey { get; set; }

    public ContractType? ContractType { get; set; }

    public decimal? Amount { get; set; }

    public int? AccountId { get; set; }

    public Account? Account { get; set; }

    public CustomerBankingRelationship? CustomerBankingRelationship { get; set; }

    public int? CustomerBankingRelationshipId { get; set; }

    public List<Transaction>? Transaction { get; set; } = [];
}

public enum ContractType
{
    CreditCard,
    Mortgage,
    PersonalLoan
}

public class ContractEntityConfiguration : IEntityTypeConfiguration<Contract>
{
    private readonly string _schema;

    public ContractEntityConfiguration(string schema)
    {
        _schema = schema;
    }

    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable(nameof(Contract), _schema);

        builder.HasKey(c => c.Id);

        builder.HasIndex(c => c.ContractKey).IsUnique();
        builder.HasIndex(c => new { c.CustomerBankingRelationshipId, c.Id });

        builder.HasMany(c => c.Transaction).WithOne(c => c.Contract).HasForeignKey(c => c.ContractId);

        builder.Property(c => c.ProcessedDateTime).HasDefaultValueSql("(now() at time zone 'utc')");
    }
}