using Azure.Identity;
using AzureCost.Core.Abstractions;
using AzureCost.Core.Internal;
using AzureCost.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AzureCost.Core.DependencyInjection;

/// <summary>
/// One-line registration for the whole Azure cost stack. A console app and a WinUI 3 app
/// call the exact same method; only the host that builds the service provider differs.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureCost(
        this IServiceCollection services,
        Action<AzureCostOptions>? configure = null)
    {
        services.AddOptions<AzureCostOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Credential: use the one supplied in options, otherwise DefaultAzureCredential.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureCostOptions>>().Value;
            if (options.Credential is not null)
            {
                return options.Credential;
            }

            var credOptions = new DefaultAzureCredentialOptions();
            if (!string.IsNullOrWhiteSpace(options.TenantId))
            {
                credOptions.TenantId = options.TenantId;
            }

            return (Azure.Core.TokenCredential)new DefaultAzureCredential(credOptions);
        });

        services.AddTransient<ThrottleRetryHandler>();
        services.AddTransient<BearerTokenHandler>();

        services.AddHttpClient<ArmClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<AzureCostOptions>>().Value;
                client.BaseAddress = new Uri(options.ArmBaseUri);
                client.Timeout = TimeSpan.FromSeconds(100);
            })
            // Order matters: the retry handler wraps the auth handler, so every retried
            // attempt is re-stamped with a fresh bearer token before it goes out.
            .AddHttpMessageHandler<ThrottleRetryHandler>()
            .AddHttpMessageHandler<BearerTokenHandler>();

        services.AddSingleton<IAzureCostService, AzureCostService>();

        return services;
    }
}
