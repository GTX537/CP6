BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE TABLE [Space_AiBudgetReservation] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [ProviderRequestKey] char(64) NOT NULL,
        [PeriodDay] date NOT NULL,
        [PeriodMonth] int NOT NULL,
        [ReservedCostMinor] bigint NOT NULL,
        [ActualCostMinor] bigint NULL,
        [Currency] char(3) NULL,
        [Status] smallint NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_AiBudgetReservation] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_AiBudgetReservation_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_AiBudgetReservation_Cost] CHECK ([ReservedCostMinor] >= 0 AND ([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)),
        CONSTRAINT [CK_Space_AiBudgetReservation_Currency] CHECK ([ReservedCostMinor] = 0 OR [Currency] IS NOT NULL),
        CONSTRAINT [CK_Space_AiBudgetReservation_Period] CHECK ([PeriodMonth] = YEAR([PeriodDay]) * 100 + MONTH([PeriodDay])),
        CONSTRAINT [FK_Space_AiBudgetReservation_Run_Tenant] FOREIGN KEY ([TenantId], [RunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE TABLE [Space_TenantAiWorkSlot] (
        [TenantId] uniqueidentifier NOT NULL,
        [SlotNo] int NOT NULL,
        [RunId] uniqueidentifier NULL,
        [LeaseOwner] nvarchar(128) NULL,
        [LeaseExpiresAtUtc] datetime2 NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Space_TenantAiWorkSlot] PRIMARY KEY ([TenantId], [SlotNo]),
        CONSTRAINT [CK_Space_TenantAiWorkSlot_Lease] CHECK (([RunId] IS NULL AND [LeaseOwner] IS NULL AND [LeaseExpiresAtUtc] IS NULL) OR ([RunId] IS NOT NULL AND [LeaseOwner] IS NOT NULL AND [LeaseExpiresAtUtc] IS NOT NULL)),
        CONSTRAINT [CK_Space_TenantAiWorkSlot_SlotNo] CHECK ([SlotNo] >= 1 AND [SlotNo] <= 3),
        CONSTRAINT [FK_Space_TenantAiWorkSlot_Run_Tenant] FOREIGN KEY ([TenantId], [RunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE INDEX [IX_AiBudgetReservation_Tenant_Day] ON [Space_AiBudgetReservation] ([TenantId], [Currency], [PeriodDay], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE INDEX [IX_AiBudgetReservation_Tenant_Month] ON [Space_AiBudgetReservation] ([TenantId], [Currency], [PeriodMonth], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE INDEX [IX_Space_AiBudgetReservation_TenantId_RunId] ON [Space_AiBudgetReservation] ([TenantId], [RunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE UNIQUE INDEX [UX_AiBudgetReservation_Tenant_Request] ON [Space_AiBudgetReservation] ([TenantId], [ProviderRequestKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    CREATE INDEX [IX_TenantAiWorkSlot_Tenant_Expiry] ON [Space_TenantAiWorkSlot] ([TenantId], [LeaseExpiresAtUtc], [SlotNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_TenantAiWorkSlot_Tenant_Run] ON [Space_TenantAiWorkSlot] ([TenantId], [RunId]) WHERE [RunId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260730183757_SpaceE13S12AiCapacityLedger'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260730183757_SpaceE13S12AiCapacityLedger', N'8.0.12');
END;
GO

COMMIT;
GO
