namespace Checkout.Application.Checkout;

public interface ICheckoutService
{
    /// <exception cref="Common.NotFoundException">The customer or product does not exist.</exception>
    /// <exception cref="Common.FeatureNotImplementedException">A saved card was requested.</exception>
    Task<CheckoutResult> CheckoutAsync(CheckoutCommand command, CancellationToken ct);
}
