using Microsoft.EntityFrameworkCore;

namespace CoffeeBeanery.Database;

public sealed class BankingEntityContext : DbContext
{
    public BankingEntityContext(DbContextOptions<BankingEntityContext> options) : base(options)
    {
    }

    public DbSet<CustomerCustomerRelationship> CustomerCustomerRelationship => Set<CustomerCustomerRelationship>();
    public DbSet<Customer> Customer => Set<Customer>();
    public DbSet<ContactPoint> ContactPoint => Set<ContactPoint>();
    public DbSet<CustomerBankingRelationship> CustomerBankingRelationship => Set<CustomerBankingRelationship>();
    public DbSet<Contract> Contract => Set<Contract>();
    public DbSet<Transaction> Transaction => Set<Transaction>();
    public DbSet<Account> Account => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new CustomerCustomerRelationshipEntityConfiguration(Schema.Banking.ToString()));
        modelBuilder.ApplyConfiguration(new CustomerEntityConfiguration(Schema.Banking.ToString()));
        modelBuilder.ApplyConfiguration(new ContactPointEntityConfiguration(Schema.Banking.ToString()));
        modelBuilder.ApplyConfiguration(new CustomerBankingRelationshipEntityConfiguration(Schema.Banking.ToString()));
        modelBuilder.ApplyConfiguration(new ContractEntityConfiguration(Schema.Lending.ToString()));
        modelBuilder.ApplyConfiguration(new TransactionEntityConfiguration(Schema.Lending.ToString()));
        modelBuilder.ApplyConfiguration(new AccountEntityConfiguration(Schema.Accounting.ToString()));
    }
}