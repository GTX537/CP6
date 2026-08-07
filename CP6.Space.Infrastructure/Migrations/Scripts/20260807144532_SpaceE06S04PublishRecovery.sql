BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    IF EXISTS (
        SELECT 1
        FROM [Space_PublishAttempt]
        WHERE [OwnsPublishSlot] = 1 AND [IsDeleted] = 0
    )
    BEGIN
        THROW 51020, 'Resolve every active E06-S03 publish attempt before applying E06-S04 recovery.', 1;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishBatch] ADD [BatchAttemptNo] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishBatch] ADD [RequestJson] nvarchar(max) NOT NULL DEFAULT N'{"items":[]}';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [JobId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [LastRetriedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [LastRetriedBy] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [ManualRetryCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [QueuedAtUtc] datetime2 NOT NULL DEFAULT '0001-01-01T00:00:00.0000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD [RequestJson] nvarchar(max) NOT NULL DEFAULT N'{}';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    UPDATE [Space_PublishAttempt]
    SET [QueuedAtUtc] = [StartedAtUtc]
    WHERE [QueuedAtUtc] = '0001-01-01T00:00:00.0000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    CREATE TABLE [Space_PublishAuditEvent] (
        [Id] uniqueidentifier NOT NULL,
        [AttemptId] uniqueidentifier NOT NULL,
        [JobId] uniqueidentifier NOT NULL,
        [BatchId] uniqueidentifier NULL,
        [EventNo] int NOT NULL,
        [EventType] smallint NOT NULL,
        [AttemptStatus] smallint NOT NULL,
        [Step] smallint NOT NULL,
        [ActorId] uniqueidentifier NOT NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [DeduplicationKey] nvarchar(300) NOT NULL,
        [Summary] nvarchar(2000) NOT NULL,
        [ErrorCode] nvarchar(100) NULL,
        [EvidenceJson] nvarchar(max) NOT NULL,
        [EvidenceHash] char(64) NOT NULL,
        [PreviousEventHash] char(64) NULL,
        [EventHash] char(64) NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_PublishAuditEvent] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Space_PublishAuditEvent_Invariants] CHECK ([EventNo] > 0 AND ISJSON([EvidenceJson]) = 1 AND LEN([EvidenceHash]) = 64 AND [EvidenceHash] NOT LIKE '%[^0-9a-f]%' AND LEN([EventHash]) = 64 AND [EventHash] NOT LIKE '%[^0-9a-f]%' AND ([PreviousEventHash] IS NULL OR (LEN([PreviousEventHash]) = 64 AND [PreviousEventHash] NOT LIKE '%[^0-9a-f]%'))),
        CONSTRAINT [FK_Space_PublishAuditEvent_Attempt_Tenant] FOREIGN KEY ([TenantId], [AttemptId]) REFERENCES [Space_PublishAttempt] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_PublishAuditEvent_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_PublishBatch] ADD CONSTRAINT [CK_Space_PublishBatch_Recovery] CHECK ([AttemptCount] >= 0 AND [BatchAttemptNo] >= 0 AND ISJSON([RequestJson]) = 1)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    CREATE INDEX [IX_Space_PublishAttempt_TenantId_JobId] ON [Space_PublishAttempt] ([TenantId], [JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_PublishAttempt] ADD CONSTRAINT [CK_Space_PublishAttempt_Recovery] CHECK ([ManualRetryCount] >= 0 AND ISJSON([RequestJson]) = 1 AND (([ManualRetryCount] = 0 AND [LastRetriedAtUtc] IS NULL AND [LastRetriedBy] IS NULL) OR ([ManualRetryCount] > 0 AND [LastRetriedAtUtc] IS NOT NULL AND [LastRetriedBy] IS NOT NULL)))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    CREATE INDEX [IX_Space_PublishAuditEvent_Tenant_Job_Occurred] ON [Space_PublishAuditEvent] ([TenantId], [JobId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PublishAuditEvent_Tenant_Attempt_Dedupe] ON [Space_PublishAuditEvent] ([TenantId], [AttemptId], [DeduplicationKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Space_PublishAuditEvent_Tenant_Attempt_EventNo] ON [Space_PublishAuditEvent] ([TenantId], [AttemptId], [EventNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    ALTER TABLE [Space_PublishAttempt] ADD CONSTRAINT [FK_Space_PublishAttempt_Job_Tenant] FOREIGN KEY ([TenantId], [JobId]) REFERENCES [Space_Job] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260807144532_SpaceE06S04PublishRecovery'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260807144532_SpaceE06S04PublishRecovery', N'8.0.12');
END;
GO

COMMIT;
GO
