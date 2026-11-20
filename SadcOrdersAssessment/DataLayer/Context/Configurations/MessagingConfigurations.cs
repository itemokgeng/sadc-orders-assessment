using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Context.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("IdempotencyRecords");
        b.HasKey(r => r.Key);
        b.Property(r => r.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength);
        b.Property(r => r.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(r => r.ResponseBody).IsRequired();

        // Lets a cleanup job delete expired keys without a table scan.
        b.HasIndex(r => r.CreatedAt);
    }
}

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("OutboxMessages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.Type).HasMaxLength(200).IsRequired();
        b.Property(m => m.Payload).IsRequired();
        b.Property(m => m.CorrelationId).HasMaxLength(100);
        b.Property(m => m.LastError).HasMaxLength(2000);

        // Filtered index: the dispatcher only ever reads unprocessed rows, oldest first.
        b.HasIndex(m => m.OccurredAt).HasFilter("[ProcessedAt] IS NULL");
    }
}

public class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> b)
    {
        b.ToTable("ProcessedMessages");
        b.HasKey(m => m.MessageId);
        b.Property(m => m.MessageId).ValueGeneratedNever();
        b.Property(m => m.MessageType).HasMaxLength(200).IsRequired();
    }
}
