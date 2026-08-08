SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedCore TABLE (
    [MigrationId] nvarchar(150) NOT NULL PRIMARY KEY
);

INSERT INTO @ExpectedCore ([MigrationId])
VALUES
    (N'20260723153450_OaP0FoundationExpand'),
    (N'20260724000423_OaP0DraftAccess'),
    (N'20260725144609_SpaceE00S04ObservabilityAudit'),
    (N'20260725174242_SpaceIntegrationEventRetryLeaseFence'),
    (N'20260725181400_SpaceRetryCompletionAndDeadLetterOutbox'),
    (N'20260725203000_SpaceIntegrationEventOccurredAtUtc'),
    (N'20260726080238_NativeClientsAndMobileTaskV1'),
    (N'20260726152637_ProductionMoveSerialLpnV2'),
    (N'20260726161123_ProductionDataIntegrityAndScanRetention'),
    (N'20260727141550_WmsRoleOperationalScopes'),
    (N'20260728153808_WmsFeatureFlagApproval'),
    (N'20260730161925_SpaceE07S02WmsAdapterLedger'),
    (N'20260802184419_SpaceE11S04DispatchApproval'),
    (N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation');

IF OBJECT_ID(N'[__EFMigrationsHistory]', N'U') IS NULL
   OR EXISTS (
       SELECT [MigrationId]
       FROM @ExpectedCore
       EXCEPT
       SELECT [MigrationId]
       FROM [__EFMigrationsHistory]
   )
    THROW 51084, 'One or more CP6Context main-sync migrations are missing.', 1;

DECLARE @ExpectedSpace TABLE (
    [MigrationId] nvarchar(150) NOT NULL PRIMARY KEY
);

INSERT INTO @ExpectedSpace ([MigrationId])
VALUES
    (N'20260726064940_SpaceE01S01ModelVersionBaseline'),
    (N'20260726072628_SpaceE01S02SourceFileLineage'),
    (N'20260726080918_SpaceE01S03JobLedger'),
    (N'20260726085852_SpaceE01S04PublishedClone'),
    (N'20260726092519_SpaceE01S05DesignApiIdempotency'),
    (N'20260730152005_SpaceE01S06FileSafetyRetention'),
    (N'20260730174231_SpaceE13S02GenerationDataModel'),
    (N'20260730183757_SpaceE13S12AiCapacityLedger'),
    (N'20260731001924_SpaceE05S02RackLevelSpecification'),
    (N'20260731010047_SpaceE05S04AssetLibrary'),
    (N'20260731032506_SpaceE04S02UnderlayCalibration'),
    (N'20260731035237_SpaceE04S03ElementCommands'),
    (N'20260731090933_SpaceE07S05WmsAdoption'),
    (N'20260801172135_SpaceE09S01ExternalOrganizations'),
    (N'20260801182535_SpaceE09S02ExternalGrants'),
    (N'20260801191107_SpaceE09S03ExternalPortal'),
    (N'20260802103430_SpaceE03S02ExcelMappingProfiles'),
    (N'20260802115537_SpaceE13S16AiPolicyManagement'),
    (N'20260802125928_SpaceE10S01PersonnelEvents'),
    (N'20260802141148_SpaceE10S03DeviceEvents'),
    (N'20260802144027_SpaceE10S04DeviceRuntime'),
    (N'20260802172258_SpaceE11S02PutawayRecommendations'),
    (N'20260802180049_SpaceE11S03DispatchRecommendations'),
    (N'20260802204901_SpaceE12S01PlanningScenarioBranches'),
    (N'20260802212845_SpaceE12S02HistoricalReplayDataset'),
    (N'20260802221548_SpaceE12S03PlanningSimulation'),
    (N'20260802224514_SpaceE12S04PlanningComparisonDecision'),
    (N'20260806054950_SpaceE13S09ProposalDecisions'),
    (N'20260806110504_SpaceE13S10AtomicApply'),
    (N'20260806160931_SpaceE13S17AiRetention'),
    (N'20260807105256_SpaceE06S01ValidationEngine'),
    (N'20260807135544_SpaceE06S03PublishOrchestration'),
    (N'20260807144532_SpaceE06S04PublishRecovery'),
    (N'20260807170204_SpaceE06S05HistoricalRepublish'),
    (N'20260808131619_SpaceE03S05ExcelDesignMetadata'),
    (N'20260808164544_SpaceE13RackGenerationProfiles');

IF OBJECT_ID(N'[__EFMigrationsHistory_Space]', N'U') IS NULL
   OR EXISTS (
       SELECT [MigrationId]
       FROM @ExpectedSpace
       EXCEPT
       SELECT [MigrationId]
       FROM [__EFMigrationsHistory_Space]
   )
    THROW 51085, 'One or more SpaceContext main-sync migrations are missing.', 1;

SELECT
    DB_NAME() AS [DatabaseName],
    (SELECT COUNT(*) FROM @ExpectedCore) AS [ExpectedCoreMigrations],
    (SELECT COUNT(*) FROM @ExpectedSpace) AS [ExpectedSpaceMigrations],
    N'PASS' AS [PostflightStatus];
