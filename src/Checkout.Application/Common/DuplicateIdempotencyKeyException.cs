namespace Checkout.Application.Common;

/// <summary>
/// The database rejected a new transaction because its idempotency key already exists:
/// another request with the same key saved first.
/// </summary>
public sealed class DuplicateIdempotencyKeyException(string idempotencyKey, Exception? innerException = null)
    : Exception($"A transaction with idempotency key {idempotencyKey} already exists.", innerException);
