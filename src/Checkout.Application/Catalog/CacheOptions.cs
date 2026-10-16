namespace Checkout.Application.Catalog;

/// <summary>Bound from the "Cache" configuration section.</summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    public TimeSpan CatalogTtl { get; set; } = TimeSpan.FromMinutes(10);
}
