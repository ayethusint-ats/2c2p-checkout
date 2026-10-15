using Checkout.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Checkout.Infrastructure.Persistence.Configurations;

internal sealed class CustomerCardConfiguration : IEntityTypeConfiguration<CustomerCard>
{
    public void Configure(EntityTypeBuilder<CustomerCard> builder)
    {
        builder.ToTable("customer_card");

        builder.HasKey(cc => cc.Id);
        builder.Property(cc => cc.Id).HasColumnName("id");

        builder.Property(cc => cc.CustomerId).HasColumnName("customer_id");
        builder.Property(cc => cc.CustomerToken).HasColumnName("customer_token").HasMaxLength(100);
        builder.Property(cc => cc.MaskedCardNo).HasColumnName("masked_card_no").HasMaxLength(30);
        builder.Property(cc => cc.CreatedAt).HasColumnName("created_at");

        // A saved card belongs to its customer; removing the customer removes their tokens.
        builder.HasOne(cc => cc.Customer)
            .WithMany()
            .HasForeignKey(cc => cc.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // The same 2C2P token can't be saved twice for one customer.
        builder.HasIndex(cc => new { cc.CustomerId, cc.CustomerToken })
            .IsUnique()
            .HasDatabaseName("ux_customer_card_customer_id_customer_token");
    }
}
