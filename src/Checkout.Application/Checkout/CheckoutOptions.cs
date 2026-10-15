namespace Checkout.Application.Checkout;

/// <summary>Bound from the "Checkout" configuration section.</summary>
public sealed class CheckoutOptions
{
    public const string SectionName = "Checkout";

    /// <summary>The 2C2P HTTP client timeout (set in Infrastructure); the lock must outlive it.</summary>
    public const int ProviderTimeoutSeconds = 30;

    /// <summary>
    /// How long the per-key lock lives if never released. Must exceed the worst-case checkout
    /// (DB reads and writes plus the 30 s provider call), or a second request could start
    /// while the first is still running.
    /// </summary>
    public int LockTtlSeconds { get; set; } = 45;

    /// <summary>How long a request waits for another request's result before answering 409.</summary>
    public double WaitTimeoutSeconds { get; set; } = 5;

    /// <summary>How often the waiting request re-reads the transaction row.</summary>
    public int PollIntervalMilliseconds { get; set; } = 150;
}
