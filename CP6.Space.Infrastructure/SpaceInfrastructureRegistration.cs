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
        services.TryAddSingleton(new SpaceJobProcessorOptions());
        services.TryAddSingleton(new SpaceAiCapacityOptions());
        services.TryAddSingleton(
            SpaceWorkerSandboxPolicy.FileSafetyDefault);
        services.TryAddSingleton<
            ISpaceAiTenantPolicySource,
            DisabledSpaceAiTenantPolicySource>();
        services.TryAddSingleton<
            ISpaceAiQuotaLeaseManager,
            ClosedSpaceAiQuotaLeaseManager>();
        services.TryAddSingleton<
            IWarehouseGenerationProviderRegistry,
            WarehouseGenerationProviderRegistry>();
        services.AddScoped<SpaceAiGenerationGateway>();
        services.AddScoped<ISpaceFileCatalog, EfSpaceFileCatalog>();
        services.AddScoped<ISpaceSourceCatalog, EfSpaceSourceCatalog>();
        services.AddScoped<ISpaceJobQueue, EfSpaceJobQueue>();
        services.AddScoped<ISpaceJobLeaseStore, EfSpaceJobLeaseStore>();
        services.AddScoped<ISpaceJobProgressReader, EfSpaceJobProgressReader>();
        services.AddScoped<
            ISpaceAiCapacityLedger,
            EfSpaceAiCapacityLedger>();
        services.AddScoped<SpaceAiCapacityCoordinator>();
        services.TryAddScoped<
            ISpaceImportJobStepExecutor,
            UnavailableSpaceImportJobStepExecutor>();
        services.TryAddScoped<
            ISpaceBuildSceneJobStepExecutor,
            UnavailableSpaceBuildSceneJobStepExecutor>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceImportJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceBuildSceneJobProcessor>());
        services.AddScoped<
            ISpaceJobProcessorRunner,
            SpaceJobProcessorRunner>();
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
        services.TryAddSingleton<StandardSpaceWmsSimulator>();
        services.TryAddSingleton<ISpaceWmsSimulatorControl>(
            provider =>
                provider.GetRequiredService<StandardSpaceWmsSimulator>());
        services.TryAddSingleton<
            ISpaceStandardWarehouseDatasetLoader,
            StandardSpaceWarehouseDatasetLoader>();
        services.AddScoped<ISpaceWmsAdapter, Cp6SpaceWmsAdapter>();
        services.AddScoped<ISpaceWmsRuntimeSource>(
            provider => provider.GetRequiredService<ISpaceWmsAdapter>());
        return services;
    }
}
