using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Context.Configurations;

public class OrderLineItemConfiguration : IEntityTypeConfiguration<OrderLineItem>
{
    public void Configure(EntityTypeBuilder<OrderLineItem> b)
    {
        b.ToTable("OrderLineItems", t =>
        {
            t.HasCheckConstraint("CK_OrderLineItems_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_OrderLineItems_UnitPrice_NonNegative", "[UnitPrice] >= 0");
        });
        b.HasKey(li => li.Id);
        b.Property(li => li.Id).ValueGeneratedNever();
        b.Property(li => li.ProductSku).HasMaxLength(OrderLineItem.ProductSkuMaxLength).IsUnicode(false).IsRequired();
        b.Property(li => li.UnitPrice).HasPrecision(18, 2);
        b.Ignore(li => li.LineTotal);
    }
}
