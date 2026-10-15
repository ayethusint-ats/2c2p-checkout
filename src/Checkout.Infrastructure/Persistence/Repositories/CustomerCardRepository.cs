using Checkout.Application.Abstractions;
using Checkout.Application.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Checkout.Infrastructure.Persistence.Repositories;

internal sealed class CustomerCardRepository(CheckoutDbContext db) : ICustomerCardRepository
{
    // The projection selects only id and masked_card_no, so customer_token never leaves the database.
    public async Task<IReadOnlyList<SavedCardDto>> GetByCustomerIdAsync(int customerId, CancellationToken ct) =>
        await db.CustomerCards
            .AsNoTracking()
            .Where(cc => cc.CustomerId == customerId)
            .OrderByDescending(cc => cc.CreatedAt)
            .Select(cc => new SavedCardDto(cc.Id, cc.MaskedCardNo))
            .ToListAsync(ct);
}
