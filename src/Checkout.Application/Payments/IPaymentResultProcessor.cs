namespace Checkout.Application.Payments;

public interface IPaymentResultProcessor
{
    /// <summary>Applies a verified payment result to its transaction. Calls no external service.</summary>
    Task<PaymentResultOutcome> ApplyAsync(PaymentResultInfo result, CancellationToken ct);
}
