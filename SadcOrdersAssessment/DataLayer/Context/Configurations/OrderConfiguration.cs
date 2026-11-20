using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Context.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("Orders");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();

        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(o => o.CurrencyCode).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(o => o.TotalAmount).HasPrecision(18, 2);
        b.Property(o => o.CreatedAt).IsRequired();

        // Optimistic concurrency: SQL Server updates this on every write, and EF adds it to the
        // WHERE clause of UPDATEs. A stale write throws DbUpdateConcurrencyException.
        b.Property(o => o.RowVersion).IsRowVersion();

        // Restrict: deleting a customer must never silently delete their order history.
        b.HasOne(o => o.Customer)
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Line items belong to the order, so cascading the delete is safe.
        b.HasMany(o => o.LineItems)
            .WithOne()
            .HasForeignKey(li => li.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(o => o.LineItems).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Main listing index: equality on CustomerId and Status, range/sort on CreatedAt. The INCLUDE
        // columns make the list query covering, so there is no key lookup per row.
        b.HasIndex(o => new { o.CustomerId, o.Status, o.CreatedAt })
            .HasDatabaseName("IX_Orders_CustomerId_Status_CreatedAt")
            .IncludeProperties(o => new { o.CurrencyCode, o.TotalAmount });

        // Serves "all orders, optionally filtered by status, newest first" when no customer is given.
        b.HasIndex(o => new { o.Status, o.CreatedAt })
            .HasDatabaseName("IX_Orders_Status_CreatedAt");
    }
}
