using Checkout.Application.Dtos;
using Checkout.Domain.Entities;

namespace Checkout.Application.Abstractions;

public interface ICustomerCardRepository
{
    Task<IReadOnlyList<SavedCardDto>> GetByCustomerIdAsync(int customerId, CancellationToken ct);

    /// <summary>
    /// The card, including its customer token, only if it belongs to <paramref name="customerId"/>;
    /// otherwise null (whether it is missing or another customer's). Untracked.
    /// </summary>
    Task<CustomerCard?> FindForCustomerAsync(int customerId, int cardId, CancellationToken ct);

    /// <summary>
    /// Saves the card unless this customer already has this token. Safe under concurrency: a
    /// duplicate insert rejected by the unique index counts as "already exists".
    /// </summary>
    /// <returns>True if a row was inserted.</returns>
    Task<bool> AddIfNotExistsAsync(int customerId, string customerToken, string maskedCardNo,
        DateTime createdAtUtc, CancellationToken ct);
}
