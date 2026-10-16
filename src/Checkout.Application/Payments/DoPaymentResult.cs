namespace Checkout.Application.Payments;

/// <summary>What to do after a saved-card charge request, independent of the provider's codes.</summary>
public enum DoPaymentNextStep
{
    /// <summary>The customer must complete a challenge (e.g. 3-D Secure) at <see cref="DoPaymentResult.RedirectUrl"/>.</summary>
    RedirectBrowser,

    /// <summary>The provider has an outcome; fetch it with a Payment Inquiry.</summary>
    InquireResult,

    /// <summary>Still processing; the outcome arrives later (notification or a later inquiry).</summary>
    WaitForResult
}

/// <param name="RedirectUrl">An absolute https URL; set only for <see cref="DoPaymentNextStep.RedirectBrowser"/>.</param>
public sealed record DoPaymentResult(DoPaymentNextStep NextStep, string RespCode, string RespDesc, string? RedirectUrl);
