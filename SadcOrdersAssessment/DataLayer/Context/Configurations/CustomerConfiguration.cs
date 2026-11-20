using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataLayer.Context.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();

        b.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength).IsRequired();
        b.Property(c => c.Email).HasMaxLength(Customer.EmailMaxLength).IsRequired();
        b.Property(c => c.CountryCode).HasMaxLength(2).IsUnicode(false).IsFixedLength().IsRequired();
        b.Property(c => c.CreatedAt).IsRequired();

        // One customer per email. Emails are lower-cased by the domain before saving.
        b.HasIndex(c => c.Email).IsUnique();
    }
}
