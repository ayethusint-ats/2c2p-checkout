using Checkout.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Checkout.Infrastructure.Persistence.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transaction");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.CustomerId).HasColumnName("customer_id");
        builder.Property(t => t.ProductId).HasColumnName("product_id");
        builder.Property(t => t.CustomerCardId).HasColumnName("customer_card_id");

        builder.Property(t => t.InvoiceNo).HasColumnName("invoice_no").HasMaxLength(50);
        builder.Property(t => t.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        builder.Property(t => t.Amount).HasColumnName("amount").HasPrecision(12, 2);
        builder.Property(t => t.Currency).HasColumnName("currency").HasColumnType("char(3)");

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.PaymentToken).HasColumnName("payment_token").HasMaxLength(200);
        builder.Property(t => t.PaymentChannel).HasColumnName("payment_channel").HasMaxLength(20);
        builder.Property(t => t.MaskedCardNo).HasColumnName("masked_card_no").HasMaxLength(30);
        builder.Property(t => t.ProviderRef).HasColumnName("provider_ref").HasMaxLength(50);
        builder.Property(t => t.RespCode).HasColumnName("resp_code").HasMaxLength(10);
        builder.Property(t => t.RespDesc).HasColumnName("resp_desc").HasMaxLength(100);
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.PaidAt).HasColumnName("paid_at");

        // Payments are financial records: block deleting a customer or product that has any.
        builder.HasOne(t => t.Customer)
            .WithMany()
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Product)
            .WithMany()
            .HasForeignKey(t => t.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // If a saved card is removed, keep the transaction (it still has masked_card_no).
        builder.HasOne(t => t.CustomerCard)
            .WithMany()
            .HasForeignKey(t => t.CustomerCardId)
            .OnDelete(DeleteBehavior.SetNull);

        // invoice_no is our reference at 2C2P; a duplicate would make results ambiguous.
        builder.HasIndex(t => t.InvoiceNo)
            .IsUnique()
            .HasDatabaseName("ux_transaction_invoice_no");

        // The database, not application code, guarantees one transaction per idempotency key,
        // even when two identical requests arrive at the same moment.
        builder.HasIndex(t => t.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_transaction_idempotency_key");
    }
}
