namespace Checkout.Application.Payments;

/// <summary>
/// A payment result from the provider, already signature-verified. Provider-neutral, so the same
/// processing applies to a backend notification and a Payment Inquiry response.
/// </summary>
/// <param name="Amount">Null when the provider omitted it (e.g. an inquiry for a payment still in progress).</param>
/// <param name="CurrencyCode">Null when the provider omitted it.</param>
/// <param name="AccountNo">The masked card number, e.g. 411111XXXXXX1111.</param>
/// <param name="TranRef">The provider's own reference for the payment.</param>
/// <param name="CustomerToken">Present when the card was tokenized; never log it.</param>
/// <param name="FxAmount">
/// The amount in the currency we asked for, when the provider converted it (e.g. the 2C2P demo
/// merchant settles in THB: Amount/CurrencyCode are then THB, FxAmount/FxCurrencyCode our SGD).
/// </param>
/// <param name="FxCurrencyCode">The currency of <paramref name="FxAmount"/>.</param>
public sealed record PaymentResultInfo(
    string MerchantId,
    string InvoiceNo,
    decimal? Amount,
    string? CurrencyCode,
    string RespCode,
    string RespDesc,
    string? ChannelCode,
    string? AccountNo,
    string? TranRef,
    string? CustomerToken,
    decimal? FxAmount = null,
    string? FxCurrencyCode = null);
