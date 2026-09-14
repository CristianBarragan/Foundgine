using Microsoft.EntityFrameworkCore;

namespace Foundgine.HighAssurance.EfCore;

/// <summary>
/// EF Core mapping onto the same `banking` schema used by
/// Foundgine.HighAssurance.Postgres (schema.sql). No shadow migrations are
/// generated from this context — the schema is owned by schema.sql and this
/// context is mapped onto it read/write.
/// </summary>
public sealed class BankingDbContext : DbContext
{
    public BankingDbContext(DbContextOptions<BankingDbContext> options) : base(options)
    {
    }

    public DbSet<BankAccountRow> BankAccounts => Set<BankAccountRow>();
    public DbSet<TransferIdempotencyRow> TransferIdempotency => Set<TransferIdempotencyRow>();
    public DbSet<TransferAuditRow> TransferAudit => Set<TransferAuditRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("banking");

        modelBuilder.Entity<BankAccountRow>(e =>
        {
            e.ToTable("bank_account", "banking");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.OwnerId).HasColumnName("owner_id");
            e.Property(x => x.Balance).HasColumnName("balance").HasColumnType("numeric(19,4)");
            e.Property(x => x.PendingTransactions).HasColumnName("pending_transactions").HasColumnType("numeric(19,4)");
            e.Property(x => x.RegulatoryHold).HasColumnName("regulatory_hold").HasColumnType("numeric(19,4)");
            e.Property(x => x.DailyTransferred).HasColumnName("daily_transferred").HasColumnType("numeric(19,4)");
            e.Property(x => x.DailyLimit).HasColumnName("daily_limit").HasColumnType("numeric(19,4)");
            e.Property(x => x.IsFrozen).HasColumnName("is_frozen");
        });

        modelBuilder.Entity<TransferIdempotencyRow>(e =>
        {
            e.ToTable("transfer_idempotency", "banking");
            e.HasKey(x => x.IdempotencyKey);
            e.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
            e.Property(x => x.ActorId).HasColumnName("actor_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.SourceAccountId).HasColumnName("source_account_id");
            e.Property(x => x.DestinationAccountId).HasColumnName("destination_account_id");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");
            e.Property(x => x.TransferId).HasColumnName("transfer_id");
            e.Property(x => x.SourceBalance).HasColumnName("source_balance").HasColumnType("numeric(19,4)");
            e.Property(x => x.DestinationBalance).HasColumnName("destination_balance").HasColumnType("numeric(19,4)");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<TransferAuditRow>(e =>
        {
            e.ToTable("transfer_audit", "banking");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.TransferId).HasColumnName("transfer_id");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.ActorId).HasColumnName("actor_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.SourceAccountId).HasColumnName("source_account_id");
            e.Property(x => x.DestinationAccountId).HasColumnName("destination_account_id");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(19,4)");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}

public sealed class BankAccountRow
{
    public Guid Id { get; set; }
    public int TenantId { get; set; }
    public Guid OwnerId { get; set; }
    public decimal Balance { get; set; }
    public decimal PendingTransactions { get; set; }
    public decimal RegulatoryHold { get; set; }
    public decimal DailyTransferred { get; set; }
    public decimal DailyLimit { get; set; }
    public bool IsFrozen { get; set; }

    /// <summary>Row version used only in-memory to detect a stale re-read; not a DB column.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool Touched { get; set; }
}

public sealed class TransferIdempotencyRow
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid ActorId { get; set; }
    public int TenantId { get; set; }
    public Guid SourceAccountId { get; set; }
    public Guid DestinationAccountId { get; set; }
    public decimal Amount { get; set; }
    public Guid TransferId { get; set; }
    public decimal SourceBalance { get; set; }
    public decimal DestinationBalance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TransferAuditRow
{
    public long Id { get; set; }
    public Guid TransferId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid ActorId { get; set; }
    public int TenantId { get; set; }
    public Guid SourceAccountId { get; set; }
    public Guid DestinationAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
