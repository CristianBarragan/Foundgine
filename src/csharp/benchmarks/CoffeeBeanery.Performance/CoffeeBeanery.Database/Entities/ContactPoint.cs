using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeBeanery.Database;

public partial class ContactPoint : Process
{
    public ContactPoint()
    {
        Schema = CoffeeBeanery.Database.Schema.Banking;
    }

    public int Id { get; set; }

    public Guid ContactPointKey { get; set; }

    public ContactPointType? ContactPointType { get; set; }

    public string? ContactPointValue { get; set; }

    public int? CustomerKey { get; set; }

    public int? CustomerId { get; set; }

    public Customer? Customer { get; set; }
}

public enum ContactPointType
{
    Mobile,
    Landline,
    Email
}

public class ContactPointEntityConfiguration : IEntityTypeConfiguration<ContactPoint>
{
    private readonly string _schema;

    public ContactPointEntityConfiguration(string schema)
    {
        _schema = schema;
    }

    public void Configure(EntityTypeBuilder<ContactPoint> builder)
    {
        builder.ToTable(nameof(ContactPoint), _schema);

        builder.HasKey(c => c.Id);

        builder.HasOne(c => c.Customer)
            .WithMany(cu => cu.ContactPoint)
            .HasForeignKey(c => c.CustomerId);

        builder.HasIndex(c => c.ContactPointKey).IsUnique();

        builder.Property(c => c.ProcessedDateTime).HasDefaultValueSql("(now() at time zone 'utc')");
    }
}