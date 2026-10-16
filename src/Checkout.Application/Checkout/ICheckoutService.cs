namespace Checkout.Application.Checkout;

public interface ICheckoutService
{
    /// <exception cref="Common.NotFoundException">
    /// The customer or product does not exist, or the saved card does not belong to the customer.
    /// </exception>
    Task<CheckoutResult> CheckoutAsync(CheckoutCommand command, CancellationToken ct);
}
