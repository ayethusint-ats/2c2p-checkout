using Checkout.Application.Catalog;
using Checkout.Application.Checkout;
using Checkout.Application.Common;
using Checkout.Application.Payments;
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

        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .Validate(o => IsAbsoluteHttpUrl(o.FrontendBaseUrl), "App:FrontendBaseUrl must be an absolute http(s) URL.")
            .Validate(o => IsAbsoluteHttpUrl(o.PublicApiBaseUrl), "App:PublicApiBaseUrl must be an absolute http(s) URL.")
            .ValidateOnStart();

        services.AddOptions<CheckoutOptions>()
            .Bind(configuration.GetSection(CheckoutOptions.SectionName))
            .Validate(o => o.LockTtlSeconds > CheckoutOptions.ProviderTimeoutSeconds,
                $"Checkout:LockTtlSeconds must exceed the {CheckoutOptions.ProviderTimeoutSeconds} s provider timeout.")
            .Validate(o => o.WaitTimeoutSeconds > 0, "Checkout:WaitTimeoutSeconds must be positive.")
            .Validate(o => o.PollIntervalMilliseconds > 0, "Checkout:PollIntervalMilliseconds must be positive.")
            .ValidateOnStart();

        // Injected clock, so tests can fix "now" (invoice date, created_at).
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IPaymentResultProcessor, PaymentResultProcessor>();

        return services;
    }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
