using Checkout.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Checkout.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("product");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(150);
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(p => p.Price).HasColumnName("price").HasPrecision(12, 2);
        builder.Property(p => p.Currency).HasColumnName("currency").HasColumnType("char(3)");

        builder.Property(p => p.PaymentType)
            .HasColumnName("payment_type")
            .HasConversion<string>()
            .HasMaxLength(20);
    }
}
