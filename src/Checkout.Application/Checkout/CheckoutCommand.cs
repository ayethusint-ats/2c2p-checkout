namespace Checkout.Application.Checkout;

/// <summary>No amount or currency on purpose: those always come from the product.</summary>
public sealed record CheckoutCommand(int CustomerId, int ProductId, int? CustomerCardId, string IdempotencyKey);
