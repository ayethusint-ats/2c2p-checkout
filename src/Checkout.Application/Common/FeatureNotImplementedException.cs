namespace Checkout.Application.Common;

/// <summary>A valid request for a feature that is not built yet (mapped to HTTP 501).</summary>
public sealed class FeatureNotImplementedException(string message) : Exception(message);
