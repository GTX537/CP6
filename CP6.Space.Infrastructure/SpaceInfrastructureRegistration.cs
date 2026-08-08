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
        services.TryAddSingleton(new SpaceAiRetentionOptions());
        services.TryAddSingleton(new SpaceAiProposalReviewOptions());
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
        services.TryAddSingleton(
            new WarehouseGenerationOutputValidationLimits());
        services.TryAddSingleton<
            ISpaceAiApplyFaultInjector,
            NoOpSpaceAiApplyFaultInjector>();
        services.TryAddSingleton<
            ISpaceAiRetentionAuthorization,
            ClosedSpaceAiRetentionAuthorization>();
        services.TryAddSingleton<
            IWarehouseGenerationOutputValidator,
            WarehouseGenerationOutputValidator>();
        services.TryAddSingleton<
            IWarehouseDraftSynthesizer,
            WarehouseDraftSynthesizer>();
        services.AddScoped<SpaceAiGenerationGateway>();
        services.AddScoped<
            ISpaceAiProposalDecisionService,
            SpaceAiProposalDecisionService>();
        services.AddScoped<
            ISpaceAiLockedFactService,
            SpaceAiLockedFactService>();
        services.AddScoped<
            ISpaceAiAtomicApplyService,
            SpaceAiAtomicApplyService>();
        services.AddScoped<
            ISpaceAiRunRecoveryService,
            SpaceAiRunRecoveryService>();
        services.AddScoped<SpaceAiAdministrationService>();
        services.AddScoped<ISpaceAiAdministrationService>(provider =>
            provider.GetRequiredService<SpaceAiAdministrationService>());
        services.AddScoped<ISpaceAiTenantPolicySource>(provider =>
            provider.GetRequiredService<SpaceAiAdministrationService>());
        services.AddScoped<ISpaceFileCatalog, EfSpaceFileCatalog>();
        services.AddScoped<ISpaceSourceCatalog, EfSpaceSourceCatalog>();
        services.AddScoped<SpaceFileUploadService>();
        services.AddScoped<ISpaceJobQueue, EfSpaceJobQueue>();
        services.AddScoped<SpaceJobCoordinator>();
        services.AddScoped<SpaceValidationEngine>();
        services.AddScoped<SpacePublishPlanEngine>();
        services.AddScoped<
            ISpaceValidationProfileProvider,
            DefaultSpaceValidationProfileProvider>();
        services.AddScoped<
            ISpaceValidationService,
            SpaceValidationService>();
        services.AddScoped<
            ISpacePublishPreviewService,
            SpacePublishPreviewService>();
        services.AddScoped<
            ISpaceRuntimeMaterializer,
            Cp6SpaceRuntimeMaterializer>();
        services.AddScoped<SpacePublishOrchestrator>();
        services.AddScoped<ISpacePublishOrchestrator>(provider =>
            provider.GetRequiredService<SpacePublishOrchestrator>());
        services.AddScoped<
            ISpacePublishActivityService,
            SpacePublishActivityService>();
        services.AddScoped<ISpaceHistoricalRepublishPublishStarter>(provider =>
            provider.GetRequiredService<SpacePublishOrchestrator>());
        services.AddScoped<ISpacePublishJobExecutor>(provider =>
            provider.GetRequiredService<SpacePublishOrchestrator>());
        services.AddScoped<
            ISpaceHistoricalRepublishService,
            SpaceHistoricalRepublishService>();
        services.AddScoped<
            ISpaceHistoricalRepublishJobExecutor,
            SpaceHistoricalRepublishJobExecutor>();
        services.AddScoped<EfSpaceVersionCloneProcessor>();
        services.AddScoped<ISpaceVersionCloneProcessor>(provider =>
            provider.GetRequiredService<EfSpaceVersionCloneProcessor>());
        services.AddScoped<ISpaceVersionSnapshotCloner>(provider =>
            provider.GetRequiredService<EfSpaceVersionCloneProcessor>());
        // Lease heartbeats run concurrently with processor work. Give the
        // ledger its own DbContext so a long-running processor never performs
        // concurrent EF operations on the processor's scoped context.
        services.AddScoped<ISpaceJobLeaseStore>(provider =>
        {
            var clock = provider.GetRequiredService<ISpaceClock>();
            var ledgerContext = new SpaceContext(
                provider.GetRequiredService<DbContextOptions<SpaceContext>>(),
                provider.GetRequiredService<ISpaceExecutionContext>(),
                clock);
            return new EfSpaceJobLeaseStore(
                ledgerContext,
                clock,
                ownsContext: true);
        });
        services.AddScoped<ISpaceJobProgressReader, EfSpaceJobProgressReader>();
        services.AddScoped<
            ISpaceAiCapacityLedger,
            EfSpaceAiCapacityLedger>();
        services.AddScoped<
            ISpaceAiRetentionStore,
            EfSpaceAiRetentionStore>();
        services.AddScoped<
            ISpaceAiRetentionJobStepExecutor,
            SpaceAiRetentionJobStepExecutor>();
        services.AddScoped<SpaceAiRetentionCoordinator>();
        services.AddScoped<SpaceAiCapacityCoordinator>();
        services.TryAddScoped<
            ISpaceImportJobStepExecutor,
            UnavailableSpaceImportJobStepExecutor>();
        services.TryAddScoped<
            ISpaceBuildSceneJobStepExecutor,
            SpaceBuildSceneJobStepExecutor>();
        services.AddScoped<
            ISpaceGenerationApplyStepExecutor,
            SpaceGenerationApplyStepExecutor>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceImportJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceCadParseJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceExcelPreflightJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceExcelCadMatchJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceExcelCadApplyJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceBuildSceneJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceGenerationApplyJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceAiRetentionJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceValidationJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpacePublishJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpacePublishReconciliationJobProcessor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                ISpaceJobProcessor,
                SpaceHistoricalRepublishJobProcessor>());
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
        services.AddScoped<
            ISpacePlanningScenarioService,
            SpacePlanningScenarioService>();
        services.AddScoped<
            ISpacePlanningDatasetService,
            SpacePlanningDatasetService>();
        services.AddScoped<
            ISpacePlanningSimulationService,
            SpacePlanningSimulationService>();
        services.AddScoped<
            ISpacePlanningComparisonService,
            SpacePlanningComparisonService>();
        services.AddScoped<
            ISpacePlanningExchangeService,
            SpacePlanningExchangeService>();
        services.AddScoped<SpaceSourceCoordinator>();
        services.AddScoped<ISpaceDesignV1Service, SpaceDesignV1Service>();
        services.TryAddScoped<
            ISpaceCadParseProvider,
            UnavailableSpaceCadParseProvider>();
        services.AddScoped<
            ISpaceCadParseJobStepExecutor,
            SpaceCadParseJobStepExecutor>();
        services.AddScoped<ISpaceCadParseService, SpaceCadParseService>();
        services.AddScoped<ISpaceExcelMappingService, SpaceExcelMappingService>();
        services.AddScoped<
            ISpaceExcelPreflightJobStepExecutor,
            SpaceExcelPreflightJobStepExecutor>();
        services.AddScoped<
            ISpaceExcelPreflightService,
            SpaceExcelPreflightService>();
        services.AddScoped<
            ISpaceExcelCadMatchJobStepExecutor,
            SpaceExcelCadMatchJobStepExecutor>();
        services.AddScoped<
            ISpaceExcelCadMatchService,
            SpaceExcelCadMatchService>();
        services.AddScoped<
            ISpaceExcelCadApplyJobStepExecutor,
            SpaceExcelCadApplyJobStepExecutor>();
        services.AddScoped<
            ISpaceExcelCadApplyService,
            SpaceExcelCadApplyService>();
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
        services.AddScoped<
            ISpaceExcelBindingAuthorityResolver,
            SpaceExcelBindingAuthorityResolver>();
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
        services.TryAddSingleton<SpacePutawayRecommendationEngine>();
        services.AddScoped<
            ISpacePutawayRecommendationService,
            SpacePutawayRecommendationService>();
        services.TryAddSingleton<SpaceDispatchRecommendationEngine>();
        services.AddScoped<
            ISpaceDispatchRecommendationService,
            SpaceDispatchRecommendationService>();
        services.AddScoped<
            ISpaceDispatchTaskAdapter,
            Cp6SpaceDispatchTaskAdapter>();
        services.AddScoped<SpaceDispatchApprovalService>();
        services.AddScoped<ISpaceDispatchApprovalService>(provider =>
            provider.GetRequiredService<SpaceDispatchApprovalService>());
        services.AddScoped<ISpaceDispatchExecutionService>(provider =>
            provider.GetRequiredService<SpaceDispatchApprovalService>());
        services.TryAddSingleton<SpaceDispatchOutcomeEvaluationEngine>();
        services.AddScoped<
            ISpaceDispatchOutcomeEvaluationService,
            SpaceDispatchOutcomeEvaluationService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                CP6.Core.Services.Wf.IApprovalCallback,
                SpaceDispatchApprovalCallback>());
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
