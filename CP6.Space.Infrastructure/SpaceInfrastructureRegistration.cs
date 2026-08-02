using CP6.Space.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CP6.Space.Infrastructure;

public static class SpaceInfrastructureRegistration
{
    public static IServiceCollection AddSpaceFileSystemStorage(
        this IServiceCollection services,
        string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException(
                "The Space file storage root is required.");
        }

        services.AddSingleton(
            new SpaceFileStorageOptions
            {
                RootPath = Path.GetFullPath(rootPath),
            });
        services.AddSingleton<FileSystemSpaceFileStore>();
        services.AddSingleton<ISpaceQuarantineStore>(
            provider =>
                provider.GetRequiredService<FileSystemSpaceFileStore>());
        services.AddSingleton<ISpaceFileStore>(
            provider =>
                provider.GetRequiredService<FileSystemSpaceFileStore>());
        return services;
    }

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
        services.TryAddSingleton(new SpaceUnderlayCalibrationOptions());
        services.TryAddSingleton(new SpacePersonnelRuntimeOptions());
        services.TryAddSingleton(new SpaceDeviceRuntimeOptions());
        services.TryAddSingleton<
            ISpaceModelingTemplateService,
            OpenXmlSpaceModelingTemplateService>();
        services.TryAddSingleton<
            ISpaceExcelWorkbookReader,
            OpenXmlSpaceExcelWorkbookReader>();
        services.TryAddSingleton<SpaceExcelPreflightValidator>();
        services.TryAddSingleton(
            SpaceWorkerSandboxPolicy.FileSafetyDefault);
        services.TryAddSingleton<
            ISpaceAiQuotaLeaseManager,
            ClosedSpaceAiQuotaLeaseManager>();
        services.TryAddSingleton<
            IWarehouseGenerationProviderRegistry,
            WarehouseGenerationProviderRegistry>();
        services.AddScoped<SpaceAiGenerationGateway>();
        services.AddScoped<SpaceAiAdministrationService>();
        services.AddScoped<ISpaceAiAdministrationService>(provider =>
            provider.GetRequiredService<SpaceAiAdministrationService>());
        services.AddScoped<ISpaceAiTenantPolicySource>(provider =>
            provider.GetRequiredService<SpaceAiAdministrationService>());
        services.AddScoped<ISpaceFileCatalog, EfSpaceFileCatalog>();
        services.AddScoped<ISpaceSourceCatalog, EfSpaceSourceCatalog>();
        services.AddScoped<SpaceFileUploadService>();
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
                SpaceExcelPreflightJobProcessor>());
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
        services.AddScoped<ISpaceExcelMappingService, SpaceExcelMappingService>();
        services.AddScoped<
            ISpaceExcelPreflightJobStepExecutor,
            SpaceExcelPreflightJobStepExecutor>();
        services.AddScoped<
            ISpaceExcelPreflightService,
            SpaceExcelPreflightService>();
        services.AddScoped<ISpacePublishedSceneReader, SpacePublishedSceneReader>();
        services.AddScoped<
            ISpaceExternalReferenceValidator,
            Cp6SpaceExternalReferenceValidator>();
        services.AddScoped<
            ISpaceExternalOrganizationService,
            SpaceExternalOrganizationService>();
        services.AddScoped<
            ISpaceExternalGrantService,
            SpaceExternalGrantService>();
        services.AddScoped<ISpaceFieldPolicyService, SpaceFieldPolicyService>();
        services.AddScoped<SpaceAccessEvaluator>();
        services.AddScoped<ISpaceAccessEvaluator>(provider =>
            provider.GetRequiredService<SpaceAccessEvaluator>());
        services.AddScoped<ISpaceUnderlayV1Service, SpaceUnderlayV1Service>();
        services.AddScoped<
            ISpaceWarehouseResolver,
            Cp6SpaceWarehouseResolver>();
        services.AddScoped<
            ISpaceWmsAdoptionService,
            SpaceWmsAdoptionService>();
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
        services.AddScoped<ISpaceWmsRuntimeService, SpaceWmsRuntimeService>();
        services.AddScoped<
            ISpacePersonnelEventService,
            SpacePersonnelEventService>();
        services.AddScoped<
            ISpacePersonnelRuntimeService,
            SpacePersonnelRuntimeService>();
        services.TryAddSingleton<SpaceOperationsDiagnosticEngine>();
        services.AddScoped<
            ISpaceOperationsDiagnosticService,
            SpaceOperationsDiagnosticService>();
        services.AddScoped<
            ISpaceDeviceEventService,
            SpaceDeviceEventService>();
        services.AddScoped<
            ISpaceDeviceRuntimeService,
            SpaceDeviceRuntimeService>();
        services.AddScoped<
            ISpaceExternalPortalService,
            SpaceExternalPortalService>();
        return services;
    }
}
