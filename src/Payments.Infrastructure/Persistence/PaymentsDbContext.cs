using Microsoft.EntityFrameworkCore;
using Payments.Application.Processing;
using Payments.Domain;

namespace Payments.Infrastructure.Persistence;

// Details: docs/code/PaymentsDbContext.md
public sealed class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
        : base(options)
    {
    }

    public DbSet<InboxEvent> InboxEvents => this.Set<InboxEvent>();

    public DbSet<Payment> Payments => this.Set<Payment>();

    public DbSet<PaymentHistoryEntry> PaymentHistory => this.Set<PaymentHistoryEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>()
            .HaveColumnType("datetime2");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InboxEvent>(entity =>
        {
            entity.ToTable("InboxEvents");
            entity.HasKey(inboxEvent => inboxEvent.EventId);
            entity.Property(inboxEvent => inboxEvent.EventId).HasMaxLength(64).IsUnicode(false);
            entity.Property(inboxEvent => inboxEvent.EventType).HasMaxLength(64).IsUnicode(false);
            entity.Property(inboxEvent => inboxEvent.PaymentIntentId).HasMaxLength(64).IsUnicode(false);
            entity.Property(inboxEvent => inboxEvent.TraceId).HasMaxLength(32).IsUnicode(false).IsFixedLength();
            entity.HasIndex(inboxEvent => inboxEvent.PaymentIntentId);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments");
            entity.HasKey(payment => payment.PaymentIntentId);
            entity.Property(payment => payment.PaymentIntentId).HasMaxLength(64).IsUnicode(false);
            entity.Property(payment => payment.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength();
            entity.Property(payment => payment.LastEventId).HasMaxLength(64).IsUnicode(false);
            entity.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
            entity.HasIndex(payment => new { payment.UpdatedAt, payment.PaymentIntentId });
        });

        modelBuilder.Entity<PaymentHistoryEntry>(entity =>
        {
            entity.ToTable("PaymentHistory");
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.PaymentIntentId).HasMaxLength(64).IsUnicode(false);
            entity.Property(entry => entry.EventId).HasMaxLength(64).IsUnicode(false);
            entity.Property(entry => entry.Reason).HasMaxLength(200).IsUnicode(false);
            entity.HasIndex(entry => entry.PaymentIntentId);
        });
    }
}
