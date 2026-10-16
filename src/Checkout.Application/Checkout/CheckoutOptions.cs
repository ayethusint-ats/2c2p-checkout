namespace Checkout.Application.Checkout;

/// <summary>Bound from the "Checkout" configuration section.</summary>
public sealed class CheckoutOptions
{
    public const string SectionName = "Checkout";

    /// <summary>The 2C2P HTTP client timeout (set in Infrastructure); the lock must outlive it.</summary>
    public const int ProviderTimeoutSeconds = 30;

    /// <summary>A saved-card checkout makes up to three sequential calls: Payment Token, Do Payment, Inquiry.</summary>
    public const int MaxProviderCallsPerCheckout = 3;

    /// <summary>
    /// How long the per-key lock lives if never released. Must exceed the worst-case checkout
    /// (DB reads and writes plus three 30 s provider calls), or a second request could start
    /// while the first is still running.
    /// </summary>
    public int LockTtlSeconds { get; set; } = 100;

    /// <summary>How long a request waits for another request's result before answering 409.</summary>
    public double WaitTimeoutSeconds { get; set; } = 5;

    /// <summary>How often the waiting request re-reads the transaction row.</summary>
    public int PollIntervalMilliseconds { get; set; } = 150;
}
