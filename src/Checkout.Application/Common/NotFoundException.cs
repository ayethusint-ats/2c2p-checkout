namespace Checkout.Application.Common;

/// <summary>A referenced resource does not exist (mapped to HTTP 404).</summary>
public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} {key} was not found.");
