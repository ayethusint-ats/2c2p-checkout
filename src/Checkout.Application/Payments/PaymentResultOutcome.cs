namespace Checkout.Application.Payments;

public enum PaymentResultOutcome
{
    /// <summary>The Pending transaction was updated to Succeeded or Failed.</summary>
    Applied,

    /// <summary>The transaction was already Succeeded or Failed; nothing changed.</summary>
    AlreadyFinal,

    /// <summary>No transaction has this invoice number.</summary>
    UnknownInvoice,

    /// <summary>The result's amount or currency differs from what we stored (or a success omitted them); nothing changed.</summary>
    AmountOrCurrencyMismatch,

    /// <summary>The provider has no final outcome yet (in progress, not found, inquiry failed); nothing changed.</summary>
    NotFinal
}
