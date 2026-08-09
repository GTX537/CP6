namespace CP6.WebApi.BackgroundServices;

public static class SpaceJobWorkerRegistration
{
    public static IServiceCollection AddSpaceJobWorkers(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<SpacePublishJobWorker>();
        services.AddHostedService<SpaceProcessingJobWorker>();
        return services;
    }
}
