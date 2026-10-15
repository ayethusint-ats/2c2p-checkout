namespace Checkout.Application.Common;

/// <summary>Bound from the "App" configuration section.</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>Where 2C2P sends the customer's browser after the payment page.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>Publicly reachable base URL of this API, for 2C2P's server-to-server notification.</summary>
    public string PublicApiBaseUrl { get; set; } = "http://localhost:5099";
}
