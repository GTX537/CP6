using CP6.Space.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CP6.Space.Infrastructure;

public static class SpaceInfrastructureRegistration
{
    public static IServiceCollection AddSpaceDesignV1Persistence(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The Space database connection string is required.");
        }

        services.AddDbContext<SpaceContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable)));
        services.AddSingleton<ISpaceClock, SystemSpaceClock>();
        services.TryAddSingleton(new SpaceFileUploadLimits());
        services.TryAddSingleton(new SpaceFileRetentionOptions());
        services.TryAddSingleton(
            SpaceWorkerSandboxPolicy.FileSafetyDefault);
        services.AddScoped<ISpaceFileCatalog, EfSpaceFileCatalog>();
        services.AddScoped<ISpaceSourceCatalog, EfSpaceSourceCatalog>();
        services.AddScoped<ISpaceJobQueue, EfSpaceJobQueue>();
        services.AddScoped<ISpaceJobLeaseStore, EfSpaceJobLeaseStore>();
        services.AddScoped<ISpaceJobProgressReader, EfSpaceJobProgressReader>();
        services.AddScoped<ISpaceFileScanStateStore, EfSpaceFileScanStateStore>();
        services.AddScoped<ISpaceFileRetentionStore, EfSpaceFileRetentionStore>();
        services.TryAddScoped<
            ISpaceMalwareScanner,
            UnavailableSpaceMalwareScanner>();
        services.TryAddScoped<
            IFileSafetyScanner,
            QuarantiningFileSafetyScanner>();
        services.AddScoped<ISpaceFileScanProcessor, SpaceFileScanProcessor>();
        services.AddScoped<ISpaceVersionCloneStore, EfSpaceVersionCloneStore>();
        services.AddScoped<SpaceVersionCloneCoordinator>();
        services.AddScoped<SpaceSourceCoordinator>();
        services.AddScoped<ISpaceDesignV1Service, SpaceDesignV1Service>();
        return services;
    }
}
