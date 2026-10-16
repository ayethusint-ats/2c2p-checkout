using Checkout.Application.Abstractions;
using Checkout.Application.Dtos;
using Checkout.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Infrastructure.Persistence.Repositories;

internal sealed class CustomerCardRepository(CheckoutDbContext db) : ICustomerCardRepository
{
    private const string CustomerTokenIndex = "ux_customer_card_customer_id_customer_token";

    // The projection selects only id and masked_card_no, so customer_token never leaves the database.
    public async Task<IReadOnlyList<SavedCardDto>> GetByCustomerIdAsync(int customerId, CancellationToken ct) =>
        await db.CustomerCards
            .AsNoTracking()
            .Where(cc => cc.CustomerId == customerId)
            .OrderByDescending(cc => cc.CreatedAt)
            .Select(cc => new SavedCardDto(cc.Id, cc.MaskedCardNo))
            .ToListAsync(ct);

    public async Task<bool> AddIfNotExistsAsync(int customerId, string customerToken, string maskedCardNo,
        DateTime createdAtUtc, CancellationToken ct)
    {
        // Cheap check for the common case (a retried notification for a card we already have).
        if (await db.CustomerCards.AnyAsync(cc => cc.CustomerId == customerId && cc.CustomerToken == customerToken, ct))
            return false;

        var card = new CustomerCard
        {
            CustomerId = customerId,
            CustomerToken = customerToken,
            MaskedCardNo = maskedCardNo,
            CreatedAt = createdAtUtc,
        };
        db.CustomerCards.Add(card);

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (MySqlErrors.IsDuplicateKey(ex, CustomerTokenIndex))
        {
            // A concurrent request inserted the same card between our check and our insert.
            db.Entry(card).State = EntityState.Detached;
            return false;
        }
    }
}
