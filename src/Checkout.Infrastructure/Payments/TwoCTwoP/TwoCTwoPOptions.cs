namespace Checkout.Infrastructure.Payments.TwoCTwoP;

/// <summary>Bound from the "TwoCTwoP" configuration section; validated at startup.</summary>
internal sealed class TwoCTwoPOptions
{
    public const string SectionName = "TwoCTwoP";

    public string MerchantId { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Must end with '/', otherwise "paymentToken" would replace the last path segment.</summary>
    public string BaseUrl { get; set; } = "https://sandbox-pgw.2c2p.com/payment/4.3/";
}
