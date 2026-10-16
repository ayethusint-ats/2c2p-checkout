namespace Checkout.Application.Common;

/// <summary>Another request with the same key has not finished in time (mapped to HTTP 409).</summary>
public sealed class CheckoutInProgressException(string idempotencyKey)
    : Exception($"A checkout with Idempotency-Key {idempotencyKey} is still in progress. " +
                "Retry shortly; if it never completes, start a new checkout with a new key.");
