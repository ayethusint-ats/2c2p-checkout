namespace Checkout.Application.Common;

/// <summary>The key was already used for a different customer or product (mapped to HTTP 422).</summary>
public sealed class IdempotencyKeyReuseException(string idempotencyKey)
    : Exception($"Idempotency-Key {idempotencyKey} was already used for a different checkout. Use a new key.");
