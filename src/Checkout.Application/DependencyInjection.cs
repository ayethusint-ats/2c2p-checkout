using Checkout.Application.Catalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Checkout.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CacheOptions>().Bind(configuration.GetSection(CacheOptions.SectionName));

        // Scoped because it depends on repositories, which share the request's DbContext.
        services.AddScoped<CatalogService>();

        return services;
    }
}
