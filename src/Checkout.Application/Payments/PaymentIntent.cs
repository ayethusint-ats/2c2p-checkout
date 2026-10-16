namespace Checkout.Application.Payments;

/// <summary>What the customer wants to do on the payment page, independent of any provider.</summary>
public enum PaymentIntent
{
    /// <summary>Pay with any method the provider offers; nothing is saved.</summary>
    NewPaymentMethod,

    /// <summary>Pay by card and save it for later.</summary>
    NewCardAndSave,

    /// <summary>Pay with a previously saved card.</summary>
    SavedCard
}
