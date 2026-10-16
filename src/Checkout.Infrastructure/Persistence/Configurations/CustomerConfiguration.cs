using Checkout.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Checkout.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customer");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.FirstName).HasColumnName("first_name").HasMaxLength(100);
        builder.Property(c => c.LastName).HasColumnName("last_name").HasMaxLength(100);
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(255);
    }
}
