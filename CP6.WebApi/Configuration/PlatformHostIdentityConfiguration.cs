using CP6.WebApi.Observability;

namespace CP6.WebApi.Configuration;

public static class PlatformHostIdentityConfiguration
{
    public static IServiceCollection AddPlatformHostIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        // The operator's trusted expectation is fixed for this process, not reloadable request input.
        var manifest = PlatformHostIdentityManifest.Read(configuration);
        services.AddSingleton(new PlatformHostIdentityObserver(manifest));
        return services;
    }
}
