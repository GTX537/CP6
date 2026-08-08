BEGIN TRANSACTION;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    ALTER TABLE [Wf_FormData] ADD [FormDefVersionId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    ALTER TABLE [Wf_FormData] ADD [RequestHash] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    ALTER TABLE [Wf_FormData] ADD [RowVersion] rowversion NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    ALTER TABLE [Wf_FormData] ADD [SubmissionKey] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    ALTER TABLE [Wf_FormData] ADD [SubmittedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    ALTER TABLE [Wf_FormData] ADD [SubmittedBy] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    ALTER TABLE [Wf_FlowInstance] ADD [FlowDefVersionId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    ALTER TABLE [Wf_FlowInstance] ADD [FormDataId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    ALTER TABLE [Wf_FlowInstance] ADD [FormDefVersionId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Wf_FlowDef]') AND [c].[name] = N'FormKey');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Wf_FlowDef] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Wf_FlowDef] ALTER COLUMN [FormKey] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    ALTER TABLE [Wf_ApprovalBinding] ADD [DetailRoute] nvarchar(300) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE TABLE [Wf_FlowDefVersion] (
        [Id] uniqueidentifier NOT NULL,
        [FlowDefId] uniqueidentifier NOT NULL,
        [Version] int NOT NULL,
        [Status] int NOT NULL,
        [FlowNameSnapshot] nvarchar(200) NOT NULL,
        [SchemaJson] nvarchar(max) NOT NULL,
        [PublishedAtUtc] datetime2 NULL,
        [PublishedBy] uniqueidentifier NULL,
        [RowVersion] rowversion NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Wf_FlowDefVersion] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Wf_FlowDefVersion_Wf_FlowDef_FlowDefId] FOREIGN KEY ([FlowDefId]) REFERENCES [Wf_FlowDef] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE TABLE [Wf_FormDefVersion] (
        [Id] uniqueidentifier NOT NULL,
        [FormDefId] uniqueidentifier NOT NULL,
        [Version] int NOT NULL,
        [Status] int NOT NULL,
        [FormNameSnapshot] nvarchar(200) NOT NULL,
        [SchemaJson] nvarchar(max) NOT NULL,
        [PublishedAtUtc] datetime2 NULL,
        [PublishedBy] uniqueidentifier NULL,
        [RowVersion] rowversion NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Wf_FormDefVersion] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Wf_FormDefVersion_Wf_FormDef_FormDefId] FOREIGN KEY ([FormDefId]) REFERENCES [Wf_FormDef] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE TABLE [Wf_FormFlowBinding] (
        [Id] uniqueidentifier NOT NULL,
        [FormDefId] uniqueidentifier NOT NULL,
        [FlowDefId] uniqueidentifier NOT NULL,
        [Enable] bit NOT NULL,
        [RowVersion] rowversion NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Wf_FormFlowBinding] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Wf_FormFlowBinding_Wf_FlowDef_FlowDefId] FOREIGN KEY ([FlowDefId]) REFERENCES [Wf_FlowDef] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Wf_FormFlowBinding_Wf_FormDef_FormDefId] FOREIGN KEY ([FormDefId]) REFERENCES [Wf_FormDef] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE TABLE [Wf_FlowDefVersionDependency] (
        [Id] uniqueidentifier NOT NULL,
        [FlowDefVersionId] uniqueidentifier NOT NULL,
        [NodeId] nvarchar(100) NOT NULL,
        [DependencyType] nvarchar(30) NOT NULL,
        [TargetFlowDefVersionId] uniqueidentifier NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Wf_FlowDefVersionDependency] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Wf_FlowDefVersionDependency_Wf_FlowDefVersion_FlowDefVersionId] FOREIGN KEY ([FlowDefVersionId]) REFERENCES [Wf_FlowDefVersion] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Wf_FlowDefVersionDependency_Wf_FlowDefVersion_TargetFlowDefVersionId] FOREIGN KEY ([TargetFlowDefVersionId]) REFERENCES [Wf_FlowDefVersion] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE TABLE [Wf_FormDraft] (
        [Id] uniqueidentifier NOT NULL,
        [OwnerUserId] uniqueidentifier NOT NULL,
        [FormDefId] uniqueidentifier NOT NULL,
        [FormDefVersionId] uniqueidentifier NOT NULL,
        [DataJson] nvarchar(max) NOT NULL,
        [Title] nvarchar(200) NULL,
        [Status] int NOT NULL,
        [SubmittedFormDataId] uniqueidentifier NULL,
        [SubmittedAtUtc] datetime2 NULL,
        [LegacyFlowInstanceId] uniqueidentifier NULL,
        [RebasedFromVersionId] uniqueidentifier NULL,
        [RowVersion] rowversion NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Wf_FormDraft] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Wf_FormDraft_Wf_FormDefVersion_FormDefVersionId] FOREIGN KEY ([FormDefVersionId]) REFERENCES [Wf_FormDefVersion] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Wf_FormDraft_Wf_FormDef_FormDefId] FOREIGN KEY ([FormDefId]) REFERENCES [Wf_FormDef] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FormData_VersionSubmitted] ON [Wf_FormData] ([TenantId], [FormDefVersionId], [SubmittedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Wf_FormData_SubmissionKey] ON [Wf_FormData] ([TenantId], [SubmissionKey]) WHERE [SubmissionKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Wf_FlowInstance_ActiveBusiness] ON [Wf_FlowInstance] ([TenantId], [BizType], [BizId]) WHERE [BizType] IS NOT NULL AND [BizId] IS NOT NULL AND [Status] IN (0, 4)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FlowFormTo_ActualParticipant] ON [Wf_FlowFormTo] ([TenantId], [ActualHandlerId], [InstanceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FlowFormTo_ExpectedParticipant] ON [Wf_FlowFormTo] ([TenantId], [ExpectedHandlerId], [InstanceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FlowFormTo_OnBehalfParticipant] ON [Wf_FlowFormTo] ([TenantId], [OnBehalfOfId], [InstanceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FlowCc_Participant] ON [Wf_FlowCc] ([TenantId], [RecipientId], [InstanceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FlowDefVersion_FlowDefId] ON [Wf_FlowDefVersion] ([FlowDefId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Wf_FlowDefVersion] ON [Wf_FlowDefVersion] ([TenantId], [FlowDefId], [Version]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Wf_FlowDefVersion_OneDraft] ON [Wf_FlowDefVersion] ([TenantId], [FlowDefId], [Status]) WHERE [Status] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FlowDefVersionDependency_FlowDefVersionId] ON [Wf_FlowDefVersionDependency] ([FlowDefVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FlowDefVersionDependency_Target] ON [Wf_FlowDefVersionDependency] ([TenantId], [TargetFlowDefVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FlowDefVersionDependency_TargetFlowDefVersionId] ON [Wf_FlowDefVersionDependency] ([TargetFlowDefVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Wf_FlowDefVersionDependency] ON [Wf_FlowDefVersionDependency] ([TenantId], [FlowDefVersionId], [NodeId], [DependencyType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FormDefVersion_FormDefId] ON [Wf_FormDefVersion] ([FormDefId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Wf_FormDefVersion] ON [Wf_FormDefVersion] ([TenantId], [FormDefId], [Version]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Wf_FormDefVersion_OneDraft] ON [Wf_FormDefVersion] ([TenantId], [FormDefId], [Status]) WHERE [Status] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FormDraft_FormDefId] ON [Wf_FormDraft] ([FormDefId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FormDraft_FormDefVersionId] ON [Wf_FormDraft] ([FormDefVersionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FormDraft_Owner] ON [Wf_FormDraft] ([TenantId], [OwnerUserId], [Status], [ModifyDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Wf_FormDraft_Legacy] ON [Wf_FormDraft] ([TenantId], [LegacyFlowInstanceId]) WHERE [LegacyFlowInstanceId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FormFlowBinding_FlowDefId] ON [Wf_FormFlowBinding] ([FlowDefId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    CREATE INDEX [IX_Wf_FormFlowBinding_FormDefId] ON [Wf_FormFlowBinding] ([FormDefId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Wf_FormFlowBinding_Active] ON [Wf_FormFlowBinding] ([TenantId], [FormDefId]) WHERE [Enable] = 1');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260723153450_OaP0FoundationExpand'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260723153450_OaP0FoundationExpand', N'8.0.12');
END;
GO

COMMIT;
GO
BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    ALTER TABLE [Wf_Notification] ADD [DispatchAttempts] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    ALTER TABLE [Wf_Notification] ADD [DispatchStatus] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    ALTER TABLE [Wf_Notification] ADD [DispatchedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    ALTER TABLE [Wf_Notification] ADD [EmailRequested] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    ALTER TABLE [Wf_Notification] ADD [EventKey] nvarchar(300) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    ALTER TABLE [Wf_Notification] ADD [InAppRequested] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    ALTER TABLE [Wf_Notification] ADD [LastDispatchError] nvarchar(2000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    ALTER TABLE [Wf_Notification] ADD [NextAttemptAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    CREATE INDEX [IX_Wf_Notification_Dispatch] ON [Wf_Notification] ([TenantId], [DispatchStatus], [NextAttemptAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Wf_Notification_Event] ON [Wf_Notification] ([TenantId], [EventKey]) WHERE [EventKey] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    CREATE INDEX [IX_Wf_FlowTask_PendingPage] ON [Wf_FlowTask] ([TenantId], [AssigneeId], [Status], [InstanceId], [CreateDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260724000423_OaP0DraftAccess'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260724000423_OaP0DraftAccess', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    ALTER TABLE [T_IntegrationEvent] ADD [JobId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    ALTER TABLE [T_IntegrationEvent] ADD [PublishAttemptId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    CREATE TABLE [Space_AuditEvent] (
        [Id] uniqueidentifier NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [ActorType] nvarchar(16) NOT NULL,
        [ActorId] nvarchar(100) NOT NULL,
        [ActorName] nvarchar(100) NULL,
        [OrganizationContextId] nvarchar(100) NULL,
        [Action] nvarchar(100) NOT NULL,
        [ResourceType] nvarchar(64) NOT NULL,
        [ResourceId] nvarchar(128) NULL,
        [SiteId] uniqueidentifier NULL,
        [VersionId] uniqueidentifier NULL,
        [FloorId] uniqueidentifier NULL,
        [Outcome] nvarchar(16) NOT NULL,
        [ReasonCode] nvarchar(100) NULL,
        [AuthorizationEvidenceJson] nvarchar(max) NULL,
        [BeforeHash] char(64) NULL,
        [AfterHash] char(64) NULL,
        [CorrelationId] uniqueidentifier NOT NULL,
        [TraceId] varchar(64) NOT NULL,
        [JobId] uniqueidentifier NULL,
        [RunId] uniqueidentifier NULL,
        [PublishAttemptId] uniqueidentifier NULL,
        [AttemptNo] int NULL,
        [ClientType] nvarchar(32) NULL,
        [IpAddress] nvarchar(64) NULL,
        [UserAgent] nvarchar(256) NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Space_AuditEvent] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Space_AuditEvent_ActorType] CHECK ([ActorType] IN ('User','System')),
        CONSTRAINT [CK_Space_AuditEvent_Correlation] CHECK ([CorrelationId] <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT [CK_Space_AuditEvent_Outcome] CHECK ([Outcome] IN ('Started','Succeeded','Failed','Denied')),
        CONSTRAINT [CK_Space_AuditEvent_Tenant] CHECK ([TenantId] <> '00000000-0000-0000-0000-000000000000')
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    CREATE INDEX [IX_T_IntegrationEvent_TenantId_CorrelationId] ON [T_IntegrationEvent] ([TenantId], [CorrelationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    CREATE INDEX [IX_T_IntegrationEvent_TenantId_JobId] ON [T_IntegrationEvent] ([TenantId], [JobId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    CREATE INDEX [IX_T_IntegrationEvent_TenantId_PublishAttemptId] ON [T_IntegrationEvent] ([TenantId], [PublishAttemptId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    CREATE INDEX [IX_Space_AuditEvent_TenantId_CorrelationId_OccurredAtUtc] ON [Space_AuditEvent] ([TenantId], [CorrelationId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    CREATE INDEX [IX_Space_AuditEvent_TenantId_JobId_RunId] ON [Space_AuditEvent] ([TenantId], [JobId], [RunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    CREATE INDEX [IX_Space_AuditEvent_TenantId_OccurredAtUtc] ON [Space_AuditEvent] ([TenantId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    CREATE INDEX [IX_Space_AuditEvent_TenantId_PublishAttemptId_OccurredAtUtc] ON [Space_AuditEvent] ([TenantId], [PublishAttemptId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725144609_SpaceE00S04ObservabilityAudit'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260725144609_SpaceE00S04ObservabilityAudit', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725174242_SpaceIntegrationEventRetryLeaseFence'
)
BEGIN
    ALTER TABLE [T_IntegrationEvent] ADD [RetryLeaseId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725174242_SpaceIntegrationEventRetryLeaseFence'
)
BEGIN
    CREATE INDEX [IX_T_IntegrationEvent_TenantId_RetryLeaseId] ON [T_IntegrationEvent] ([TenantId], [RetryLeaseId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725174242_SpaceIntegrationEventRetryLeaseFence'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260725174242_SpaceIntegrationEventRetryLeaseFence', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725181400_SpaceRetryCompletionAndDeadLetterOutbox'
)
BEGIN
    ALTER TABLE [T_IntegrationEvent] ADD [DeadLetterNotificationLeaseId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725181400_SpaceRetryCompletionAndDeadLetterOutbox'
)
BEGIN
    ALTER TABLE [T_IntegrationEvent] ADD [DeadLetterNotificationLeaseUntilUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725181400_SpaceRetryCompletionAndDeadLetterOutbox'
)
BEGIN
    ALTER TABLE [T_IntegrationEvent] ADD [DeadLetterNotifiedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725181400_SpaceRetryCompletionAndDeadLetterOutbox'
)
BEGIN
    ALTER TABLE [T_IntegrationEvent] ADD [RetryCompletionLeaseId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725181400_SpaceRetryCompletionAndDeadLetterOutbox'
)
BEGIN
    ALTER TABLE [T_IntegrationEvent] ADD [RetryCompletionSucceeded] bit NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725181400_SpaceRetryCompletionAndDeadLetterOutbox'
)
BEGIN
    UPDATE [T_IntegrationEvent]
    SET [DeadLetterNotifiedAtUtc] = SYSUTCDATETIME()
    WHERE [SourceModule] = N'SPACE'
      AND [Status] = N'DEAD'
      AND [DeadLetterNotifiedAtUtc] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725181400_SpaceRetryCompletionAndDeadLetterOutbox'
)
BEGIN
    CREATE INDEX [IX_T_IntegrationEvent_TenantId_Status_DeadLetterNotifiedAtUtc_DeadLetterNotificationLeaseUntilUtc] ON [T_IntegrationEvent] ([TenantId], [Status], [DeadLetterNotifiedAtUtc], [DeadLetterNotificationLeaseUntilUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725181400_SpaceRetryCompletionAndDeadLetterOutbox'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260725181400_SpaceRetryCompletionAndDeadLetterOutbox', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725203000_SpaceIntegrationEventOccurredAtUtc'
)
BEGIN
    ALTER TABLE [T_IntegrationEvent] ADD [OccurredAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725203000_SpaceIntegrationEventOccurredAtUtc'
)
BEGIN
    UPDATE [T_IntegrationEvent]
    SET [OccurredAtUtc] = [CreateDate]
    WHERE [SourceModule] = N'SPACE'
      AND [OccurredAtUtc] IS NULL
      AND [JobId] IS NOT NULL
      AND [JobId] <> '00000000-0000-0000-0000-000000000000'
      AND [JobId] <> [Id];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725203000_SpaceIntegrationEventOccurredAtUtc'
)
BEGIN
    CREATE INDEX [IX_T_IntegrationEvent_TenantId_SourceModule_CorrelationId_OccurredAtUtc_Id] ON [T_IntegrationEvent] ([TenantId], [SourceModule], [CorrelationId], [OccurredAtUtc] DESC, [Id] DESC);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725203000_SpaceIntegrationEventOccurredAtUtc'
)
BEGIN
    CREATE INDEX [IX_T_IntegrationEvent_TenantId_SourceModule_OccurredAtUtc_Id] ON [T_IntegrationEvent] ([TenantId], [SourceModule], [OccurredAtUtc] DESC, [Id] DESC);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725203000_SpaceIntegrationEventOccurredAtUtc'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260725203000_SpaceIntegrationEventOccurredAtUtc', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726080238_NativeClientsAndMobileTaskV1'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [CompletionOperationId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726080238_NativeClientsAndMobileTaskV1'
)
BEGIN
    ALTER TABLE [Sys_RefreshTokens] ADD [AppVersion] nvarchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726080238_NativeClientsAndMobileTaskV1'
)
BEGIN
    ALTER TABLE [Sys_RefreshTokens] ADD [ClientKind] nvarchar(20) NOT NULL DEFAULT N'Web';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726080238_NativeClientsAndMobileTaskV1'
)
BEGIN
    ALTER TABLE [Sys_RefreshTokens] ADD [DeviceId] nvarchar(128) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726080238_NativeClientsAndMobileTaskV1'
)
BEGIN
    ALTER TABLE [Sys_RefreshTokens] ADD [RowVersion] rowversion NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726080238_NativeClientsAndMobileTaskV1'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_T_MobileTask_CompletionOperationId] ON [T_MobileTask] ([TenantId], [CompletionOperationId]) WHERE [CompletionOperationId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726080238_NativeClientsAndMobileTaskV1'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726080238_NativeClientsAndMobileTaskV1', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_ProductMaster] ADD [SerialTrackingLockedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_ProductMaster] ADD [TrackingMode] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [AreaCd] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [ContractVersion] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [DueAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [ExceptionDescription] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [ExceptionReasonCd] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [ExecutionId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [ExecutionVersion] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [LastDeviceId] nvarchar(128) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [ParentTaskNo] nvarchar(25) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [PartialReason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [PauseReason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [PlannedStartAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [RemainderTaskNo] nvarchar(25) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [ReservedSourceQty] decimal(21,8) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_MobileTask] ADD [ReservedTargetCapacityQty] decimal(21,8) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_Location] ADD [AreaCd] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [T_Location] ADD [ReservedCapacityQty] decimal(21,8) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [Sys_Users] ADD [BadgeNo] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    ALTER TABLE [Sys_Users] ADD [QuickPinHash] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_BarcodeAlias] (
        [Id] uniqueidentifier NOT NULL,
        [Barcode] nvarchar(256) NOT NULL,
        [BarcodeType] nvarchar(20) NOT NULL,
        [TargetKey] nvarchar(128) NOT NULL,
        [ProductCd] nvarchar(20) NULL,
        [LotNo] nvarchar(30) NULL,
        [LocationCd] nvarchar(30) NULL,
        [PackageUnitCd] nvarchar(10) NULL,
        [ConversionRate] decimal(21,8) NOT NULL,
        [ValidFrom] datetime2 NULL,
        [ValidUntil] datetime2 NULL,
        [IsEnabled] bit NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_BarcodeAlias] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_BarcodeProfile] (
        [Id] uniqueidentifier NOT NULL,
        [ProfileName] nvarchar(100) NOT NULL,
        [Format] nvarchar(20) NOT NULL,
        [Pattern] nvarchar(1000) NOT NULL,
        [MappingJson] nvarchar(max) NOT NULL,
        [Priority] int NOT NULL,
        [IsEnabled] bit NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_BarcodeProfile] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_ClientDevice] (
        [Id] uniqueidentifier NOT NULL,
        [DeviceId] nvarchar(128) NOT NULL,
        [DeviceMode] nvarchar(20) NOT NULL,
        [Platform] nvarchar(20) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [PublicKey] nvarchar(2048) NOT NULL,
        [WarehouseCd] nvarchar(10) NULL,
        [AreaCd] nvarchar(20) NULL,
        [AppVersion] nvarchar(32) NULL,
        [PlatformVersion] nvarchar(64) NULL,
        [ActivatedAt] datetime2 NOT NULL,
        [ActivatedBy] nvarchar(100) NULL,
        [LastSeenAt] datetime2 NULL,
        [BatteryPercent] int NULL,
        [NetworkType] nvarchar(32) NULL,
        [CurrentUser] nvarchar(100) NULL,
        [CurrentTaskNo] nvarchar(25) NULL,
        [FullAuthExpiresAt] datetime2 NULL,
        [QuickSwitchFailureCount] int NOT NULL,
        [DisabledAt] datetime2 NULL,
        [DisabledBy] nvarchar(100) NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_ClientDevice] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_DeviceActivation] (
        [Id] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [Platform] nvarchar(20) NOT NULL,
        [DeviceMode] nvarchar(20) NOT NULL,
        [WarehouseCd] nvarchar(10) NULL,
        [AreaCd] nvarchar(20) NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [ConsumedAt] datetime2 NULL,
        [ConsumedByDeviceId] nvarchar(128) NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_DeviceActivation] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_LabelJob] (
        [Id] uniqueidentifier NOT NULL,
        [JobNo] nvarchar(25) NOT NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [TemplateName] nvarchar(100) NOT NULL,
        [WarehouseCd] nvarchar(10) NOT NULL,
        [PrinterName] nvarchar(128) NULL,
        [PayloadJson] nvarchar(max) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [RequestedDeviceId] nvarchar(128) NULL,
        [RequestedBy] nvarchar(100) NULL,
        [RequestedAt] datetime2 NOT NULL,
        [CompletedAt] datetime2 NULL,
        [AttemptCount] int NOT NULL,
        [ResultMessage] nvarchar(1000) NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_LabelJob] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_LabelTemplate] (
        [Id] uniqueidentifier NOT NULL,
        [TemplateName] nvarchar(100) NOT NULL,
        [Format] nvarchar(20) NOT NULL,
        [TemplateBody] nvarchar(max) NOT NULL,
        [Language] nvarchar(10) NULL,
        [IsEnabled] bit NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_LabelTemplate] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_LogisticsUnit] (
        [Id] uniqueidentifier NOT NULL,
        [LpnNo] nvarchar(64) NOT NULL,
        [ContainerType] nvarchar(30) NOT NULL,
        [WarehouseCd] nvarchar(10) NOT NULL,
        [LocationCd] nvarchar(30) NOT NULL,
        [ParentLpnNo] nvarchar(64) NULL,
        [Status] nvarchar(20) NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_LogisticsUnit] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_LpnClosure] (
        [Id] uniqueidentifier NOT NULL,
        [AncestorLpnNo] nvarchar(64) NOT NULL,
        [DescendantLpnNo] nvarchar(64) NOT NULL,
        [Depth] int NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_T_LpnClosure] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_LpnContent] (
        [Id] uniqueidentifier NOT NULL,
        [LpnNo] nvarchar(64) NOT NULL,
        [ProductCd] nvarchar(20) NOT NULL,
        [LotNo] nvarchar(30) NOT NULL,
        [SerialNo] nvarchar(128) NULL,
        [Qty] decimal(21,8) NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_LpnContent] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_LpnEvent] (
        [Id] uniqueidentifier NOT NULL,
        [LpnNo] nvarchar(64) NOT NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(30) NOT NULL,
        [UserName] nvarchar(100) NULL,
        [DeviceId] nvarchar(128) NULL,
        [OccurredAt] datetime2 NOT NULL,
        [DataJson] nvarchar(max) NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_T_LpnEvent] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_LpnPolicy] (
        [Id] uniqueidentifier NOT NULL,
        [WarehouseCd] nvarchar(10) NOT NULL,
        [ContainerType] nvarchar(30) NOT NULL,
        [AllowMixedProducts] bit NOT NULL,
        [AllowMixedLots] bit NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_LpnPolicy] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_MobileTaskEvent] (
        [Id] uniqueidentifier NOT NULL,
        [TaskNo] nvarchar(25) NOT NULL,
        [EventType] nvarchar(40) NOT NULL,
        [OperationId] uniqueidentifier NULL,
        [ExecutionVersion] int NOT NULL,
        [UserName] nvarchar(100) NULL,
        [DeviceId] nvarchar(128) NULL,
        [OccurredAt] datetime2 NOT NULL,
        [DataJson] nvarchar(max) NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_T_MobileTaskEvent] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_MobileTaskReservation] (
        [Id] uniqueidentifier NOT NULL,
        [TaskNo] nvarchar(25) NOT NULL,
        [WarehouseCd] nvarchar(10) NOT NULL,
        [FromLocationCd] nvarchar(30) NOT NULL,
        [ToLocationCd] nvarchar(30) NOT NULL,
        [ProductCd] nvarchar(20) NOT NULL,
        [LotNo] nvarchar(30) NOT NULL,
        [ReservedQty] decimal(21,8) NOT NULL,
        [ConsumedQty] decimal(21,8) NOT NULL,
        [ReleasedQty] decimal(21,8) NOT NULL,
        [IsActive] bit NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_MobileTaskReservation] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_MobileTaskScanLog] (
        [Id] uniqueidentifier NOT NULL,
        [TaskNo] nvarchar(25) NOT NULL,
        [ClientScanNo] nvarchar(64) NOT NULL,
        [ExecutionVersion] int NOT NULL,
        [Step] nvarchar(30) NOT NULL,
        [RawBarcode] nvarchar(512) NOT NULL,
        [DeviceId] nvarchar(128) NOT NULL,
        [UserName] nvarchar(100) NULL,
        [ScannedAt] datetime2 NOT NULL,
        [ParsedKind] nvarchar(20) NULL,
        [ParsedValue] nvarchar(256) NULL,
        [Matched] bit NOT NULL,
        [FailureCode] nvarchar(64) NULL,
        [RetainUntil] datetime2 NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_T_MobileTaskScanLog] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_StockSerial] (
        [Id] uniqueidentifier NOT NULL,
        [ProductCd] nvarchar(20) NOT NULL,
        [SerialNo] nvarchar(128) NOT NULL,
        [WarehouseCd] nvarchar(10) NOT NULL,
        [LocationCd] nvarchar(30) NOT NULL,
        [LotNo] nvarchar(30) NOT NULL,
        [LpnNo] nvarchar(64) NULL,
        [Status] nvarchar(20) NOT NULL,
        [LastTxnNo] nvarchar(25) NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_StockSerial] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_StockSerialTransaction] (
        [Id] uniqueidentifier NOT NULL,
        [TxnNo] nvarchar(25) NOT NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [TxnType] nvarchar(20) NOT NULL,
        [ProductCd] nvarchar(20) NOT NULL,
        [SerialNo] nvarchar(128) NOT NULL,
        [WarehouseCd] nvarchar(10) NOT NULL,
        [LotNo] nvarchar(30) NOT NULL,
        [FromLocationCd] nvarchar(30) NULL,
        [ToLocationCd] nvarchar(30) NULL,
        [LpnNo] nvarchar(64) NULL,
        [OperatorCd] nvarchar(100) NULL,
        [DeviceId] nvarchar(128) NULL,
        [OccurredAt] datetime2 NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_T_StockSerialTransaction] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_TaskCommandReceipt] (
        [Id] uniqueidentifier NOT NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [TaskNo] nvarchar(128) NOT NULL,
        [CommandName] nvarchar(30) NOT NULL,
        [ResultJson] nvarchar(max) NOT NULL,
        [CompletedAt] datetime2 NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_T_TaskCommandReceipt] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE TABLE [T_WmsFeatureFlag] (
        [Id] uniqueidentifier NOT NULL,
        [WarehouseCd] nvarchar(10) NOT NULL,
        [ProductionMoveEnabled] bit NOT NULL,
        [SerialLpnEnabled] bit NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_WmsFeatureFlag] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_MobileTask_ContractVersion_WarehouseCd_AreaCd_Status] ON [T_MobileTask] ([ContractVersion], [WarehouseCd], [AreaCd], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_MobileTask_DueAt] ON [T_MobileTask] ([DueAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_MobileTask_ParentTaskNo] ON [T_MobileTask] ([ParentTaskNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Sys_Users_BadgeNo] ON [Sys_Users] ([TenantId], [BadgeNo]) WHERE [BadgeNo] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_BarcodeAlias_Barcode] ON [T_BarcodeAlias] ([TenantId], [Barcode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_BarcodeAlias_BarcodeType_TargetKey] ON [T_BarcodeAlias] ([BarcodeType], [TargetKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_BarcodeAlias_ProductCd_LotNo] ON [T_BarcodeAlias] ([ProductCd], [LotNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_BarcodeProfile_IsEnabled_Priority] ON [T_BarcodeProfile] ([IsEnabled], [Priority]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_BarcodeProfile_ProfileName] ON [T_BarcodeProfile] ([TenantId], [ProfileName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_ClientDevice_DeviceId] ON [T_ClientDevice] ([TenantId], [DeviceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_ClientDevice_LastSeenAt] ON [T_ClientDevice] ([LastSeenAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_ClientDevice_Status_WarehouseCd_AreaCd] ON [T_ClientDevice] ([Status], [WarehouseCd], [AreaCd]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_DeviceActivation_ExpiresAt_ConsumedAt] ON [T_DeviceActivation] ([ExpiresAt], [ConsumedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_DeviceActivation_TokenHash] ON [T_DeviceActivation] ([TenantId], [TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_LabelJob_JobNo] ON [T_LabelJob] ([TenantId], [JobNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_LabelJob_OperationId] ON [T_LabelJob] ([TenantId], [OperationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_LabelJob_WarehouseCd_Status_RequestedAt] ON [T_LabelJob] ([WarehouseCd], [Status], [RequestedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_LabelTemplate_TemplateName] ON [T_LabelTemplate] ([TenantId], [TemplateName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_LogisticsUnit_LpnNo] ON [T_LogisticsUnit] ([TenantId], [LpnNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_LogisticsUnit_ParentLpnNo] ON [T_LogisticsUnit] ([ParentLpnNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_LogisticsUnit_WarehouseCd_LocationCd_Status] ON [T_LogisticsUnit] ([WarehouseCd], [LocationCd], [Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_LpnClosure_AncestorLpnNo_DescendantLpnNo] ON [T_LpnClosure] ([TenantId], [AncestorLpnNo], [DescendantLpnNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_LpnClosure_DescendantLpnNo_Depth] ON [T_LpnClosure] ([DescendantLpnNo], [Depth]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_T_LpnContent_LpnNo_ProductCd_LotNo_SerialNo] ON [T_LpnContent] ([TenantId], [LpnNo], [ProductCd], [LotNo], [SerialNo]) WHERE [SerialNo] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_T_LpnContent_SerialNo] ON [T_LpnContent] ([TenantId], [SerialNo]) WHERE [SerialNo] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_LpnEvent_LpnNo_OccurredAt] ON [T_LpnEvent] ([LpnNo], [OccurredAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_LpnEvent_OperationId] ON [T_LpnEvent] ([OperationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_LpnPolicy_WarehouseCd_ContainerType] ON [T_LpnPolicy] ([TenantId], [WarehouseCd], [ContainerType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_MobileTaskEvent_OperationId] ON [T_MobileTaskEvent] ([OperationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_MobileTaskEvent_TaskNo_OccurredAt] ON [T_MobileTaskEvent] ([TaskNo], [OccurredAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_MobileTaskReservation_TaskNo] ON [T_MobileTaskReservation] ([TenantId], [TaskNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_MobileTaskReservation_WarehouseCd_FromLocationCd_ProductCd_LotNo_IsActive] ON [T_MobileTaskReservation] ([WarehouseCd], [FromLocationCd], [ProductCd], [LotNo], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_MobileTaskScanLog_ClientScanNo] ON [T_MobileTaskScanLog] ([TenantId], [ClientScanNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_MobileTaskScanLog_RetainUntil] ON [T_MobileTaskScanLog] ([RetainUntil]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_MobileTaskScanLog_TaskNo_ExecutionVersion_ScannedAt] ON [T_MobileTaskScanLog] ([TaskNo], [ExecutionVersion], [ScannedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_StockSerial_LpnNo] ON [T_StockSerial] ([LpnNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_StockSerial_ProductCd_SerialNo] ON [T_StockSerial] ([TenantId], [ProductCd], [SerialNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_StockSerial_WarehouseCd_LocationCd_ProductCd_LotNo] ON [T_StockSerial] ([WarehouseCd], [LocationCd], [ProductCd], [LotNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_StockSerialTransaction_OperationId] ON [T_StockSerialTransaction] ([OperationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_StockSerialTransaction_ProductCd_SerialNo_OccurredAt] ON [T_StockSerialTransaction] ([ProductCd], [SerialNo], [OccurredAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_StockSerialTransaction_TxnNo] ON [T_StockSerialTransaction] ([TenantId], [TxnNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_TaskCommandReceipt_OperationId] ON [T_TaskCommandReceipt] ([TenantId], [OperationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE INDEX [IX_T_TaskCommandReceipt_TaskNo_CommandName] ON [T_TaskCommandReceipt] ([TaskNo], [CommandName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_WmsFeatureFlag_WarehouseCd] ON [T_WmsFeatureFlag] ([TenantId], [WarehouseCd]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726152637_ProductionMoveSerialLpnV2'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726152637_ProductionMoveSerialLpnV2', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726161123_ProductionDataIntegrityAndScanRetention'
)
BEGIN
    ALTER TABLE [T_WmsFeatureFlag] ADD [ScanRetentionDays] int NOT NULL DEFAULT 180;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726161123_ProductionDataIntegrityAndScanRetention'
)
BEGIN
    DROP INDEX [IX_T_StockTransaction_RelatedType_RelatedNo] ON [T_StockTransaction];
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[T_StockTransaction]') AND [c].[name] = N'RelatedNo');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [T_StockTransaction] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [T_StockTransaction] ALTER COLUMN [RelatedNo] nvarchar(36) NULL;
    CREATE INDEX [IX_T_StockTransaction_RelatedType_RelatedNo] ON [T_StockTransaction] ([RelatedType], [RelatedNo]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726161123_ProductionDataIntegrityAndScanRetention'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726161123_ProductionDataIntegrityAndScanRetention', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260727141550_WmsRoleOperationalScopes'
)
BEGIN
    CREATE TABLE [T_WmsRoleScope] (
        [Id] uniqueidentifier NOT NULL,
        [RoleId] int NOT NULL,
        [WarehouseCd] nvarchar(10) NOT NULL,
        [AreaCd] nvarchar(20) NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_T_WmsRoleScope] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260727141550_WmsRoleOperationalScopes'
)
BEGIN
    CREATE INDEX [IX_T_WmsRoleScope_RoleId_WarehouseCd_AreaCd] ON [T_WmsRoleScope] ([RoleId], [WarehouseCd], [AreaCd]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260727141550_WmsRoleOperationalScopes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_WmsRoleScope_TenantId_RoleId_WarehouseCd_AreaCd] ON [T_WmsRoleScope] ([TenantId], [RoleId], [WarehouseCd], [AreaCd]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260727141550_WmsRoleOperationalScopes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260727141550_WmsRoleOperationalScopes', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728153808_WmsFeatureFlagApproval'
)
BEGIN
    DROP INDEX [IX_T_WmsFeatureFlag_WarehouseCd] ON [T_WmsFeatureFlag];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728153808_WmsFeatureFlagApproval'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_WmsFeatureFlag_TenantId_WarehouseCd] ON [T_WmsFeatureFlag] ([TenantId], [WarehouseCd]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728153808_WmsFeatureFlagApproval'
)
BEGIN
    CREATE TABLE [T_WmsFeatureFlagChange] (
        [Id] uniqueidentifier NOT NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [WarehouseCd] nvarchar(10) NOT NULL,
        [BaseProductionMoveEnabled] bit NOT NULL,
        [BaseSerialLpnEnabled] bit NOT NULL,
        [BaseScanRetentionDays] int NOT NULL,
        [BaseFeatureRowVersion] nvarchar(128) NOT NULL,
        [TargetProductionMoveEnabled] bit NOT NULL,
        [TargetSerialLpnEnabled] bit NOT NULL,
        [TargetScanRetentionDays] int NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [ChangeTicket] nvarchar(100) NOT NULL,
        [EvidenceUri] nvarchar(1000) NULL,
        [Status] nvarchar(20) NOT NULL,
        [RequestedById] uniqueidentifier NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [FlowInstanceId] uniqueidentifier NOT NULL,
        [DecidedById] uniqueidentifier NULL,
        [DecidedAtUtc] datetime2 NULL,
        [AppliedAtUtc] datetime2 NULL,
        [FailureCode] nvarchar(64) NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_WmsFeatureFlagChange] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728153808_WmsFeatureFlagApproval'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_WmsFeatureFlagChange_TenantId_OperationId] ON [T_WmsFeatureFlagChange] ([TenantId], [OperationId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728153808_WmsFeatureFlagApproval'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_T_WmsFeatureFlagChange_TenantId_WarehouseCd] ON [T_WmsFeatureFlagChange] ([TenantId], [WarehouseCd]) WHERE [IsDeleted] = 0 AND [Status] = ''PENDING''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728153808_WmsFeatureFlagApproval'
)
BEGIN
    CREATE INDEX [IX_T_WmsFeatureFlagChange_TenantId_WarehouseCd_RequestedAtUtc] ON [T_WmsFeatureFlagChange] ([TenantId], [WarehouseCd], [RequestedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728153808_WmsFeatureFlagApproval'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728153808_WmsFeatureFlagApproval', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730161925_SpaceE07S02WmsAdapterLedger'
)
BEGIN
    CREATE TABLE [T_SpaceWmsOperation] (
        [Id] uniqueidentifier NOT NULL,
        [OperationKey] nvarchar(250) NOT NULL,
        [PayloadHash] char(64) NOT NULL,
        [State] int NOT NULL,
        [ExternalOperationId] nvarchar(100) NULL,
        [ResultJson] nvarchar(max) NOT NULL,
        [ObservedAtUtc] datetime2 NOT NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_SpaceWmsOperation] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730161925_SpaceE07S02WmsAdapterLedger'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_SpaceWmsOperation_TenantId_OperationKey] ON [T_SpaceWmsOperation] ([TenantId], [OperationKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730161925_SpaceE07S02WmsAdapterLedger'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730161925_SpaceE07S02WmsAdapterLedger', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802184419_SpaceE11S04DispatchApproval'
)
BEGIN
    CREATE TABLE [T_SpaceDispatchApprovalRequest] (
        [Id] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [RecommendationId] uniqueidentifier NOT NULL,
        [PublishedVersionId] uniqueidentifier NOT NULL,
        [WarehouseCode] nvarchar(10) NOT NULL,
        [RecommendationDefinitionVersion] nvarchar(50) NOT NULL,
        [RecommendationRequestHash] char(64) NOT NULL,
        [PayloadHash] char(64) NOT NULL,
        [SelectionJson] nvarchar(max) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [RequestedById] uniqueidentifier NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [FlowInstanceId] uniqueidentifier NOT NULL,
        [DecidedById] uniqueidentifier NULL,
        [DecidedAtUtc] datetime2 NULL,
        [AppliedAtUtc] datetime2 NULL,
        [AdapterId] nvarchar(100) NOT NULL,
        [ResultJson] nvarchar(max) NOT NULL,
        [FailureCode] nvarchar(100) NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_SpaceDispatchApprovalRequest] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802184419_SpaceE11S04DispatchApproval'
)
BEGIN
    CREATE UNIQUE INDEX [IX_T_SpaceDispatchApprovalRequest_TenantId_FlowInstanceId] ON [T_SpaceDispatchApprovalRequest] ([TenantId], [FlowInstanceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802184419_SpaceE11S04DispatchApproval'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_T_SpaceDispatchApprovalRequest_TenantId_SiteId_RecommendationId] ON [T_SpaceDispatchApprovalRequest] ([TenantId], [SiteId], [RecommendationId]) WHERE [IsDeleted] = 0 AND [Status] = ''PendingApproval''');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802184419_SpaceE11S04DispatchApproval'
)
BEGIN
    CREATE INDEX [IX_T_SpaceDispatchApprovalRequest_TenantId_SiteId_RequestedAtUtc] ON [T_SpaceDispatchApprovalRequest] ([TenantId], [SiteId], [RequestedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802184419_SpaceE11S04DispatchApproval'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802184419_SpaceE11S04DispatchApproval', N'8.0.12');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation'
)
BEGIN
    ALTER TABLE [T_SpaceDispatchApprovalRequest] ADD [CompensatedAtUtc] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation'
)
BEGIN
    ALTER TABLE [T_SpaceDispatchApprovalRequest] ADD [CompensatedById] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation'
)
BEGIN
    ALTER TABLE [T_SpaceDispatchApprovalRequest] ADD [CompensationReason] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation'
)
BEGIN
    ALTER TABLE [T_SpaceDispatchApprovalRequest] ADD [RetryAttemptCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation'
)
BEGIN
    ALTER TABLE [T_SpaceDispatchApprovalRequest] ADD CONSTRAINT [AK_T_SpaceDispatchApprovalRequest_TenantId_Id] UNIQUE ([TenantId], [Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation'
)
BEGIN
    CREATE TABLE [T_SpaceDispatchExecutionAction] (
        [Id] uniqueidentifier NOT NULL,
        [ApprovalRequestId] uniqueidentifier NOT NULL,
        [SiteId] uniqueidentifier NOT NULL,
        [RecommendationId] uniqueidentifier NOT NULL,
        [ActionType] nvarchar(32) NOT NULL,
        [PayloadHash] char(64) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Status] nvarchar(32) NOT NULL,
        [RequestedById] uniqueidentifier NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [AdapterId] nvarchar(100) NOT NULL,
        [ReceiptJson] nvarchar(max) NOT NULL,
        [FailureCode] nvarchar(100) NULL,
        [Creator] nvarchar(100) NULL,
        [CreateDate] datetime2 NOT NULL,
        [Modifier] nvarchar(100) NULL,
        [ModifyDate] datetime2 NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_T_SpaceDispatchExecutionAction] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_SpaceDispatchExecutionAction_Status] CHECK ([Status] IN ('Applied','FailedNoEffect','RejectedNoEffect')),
        CONSTRAINT [CK_SpaceDispatchExecutionAction_Type] CHECK ([ActionType] IN ('RetryAssignment','CompensateAssignment')),
        CONSTRAINT [FK_T_SpaceDispatchExecutionAction_T_SpaceDispatchApprovalRequest_TenantId_ApprovalRequestId] FOREIGN KEY ([TenantId], [ApprovalRequestId]) REFERENCES [T_SpaceDispatchApprovalRequest] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation'
)
BEGIN
    CREATE INDEX [IX_T_SpaceDispatchExecutionAction_TenantId_ApprovalRequestId_ActionType] ON [T_SpaceDispatchExecutionAction] ([TenantId], [ApprovalRequestId], [ActionType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation'
)
BEGIN
    CREATE INDEX [IX_T_SpaceDispatchExecutionAction_TenantId_ApprovalRequestId_RequestedAtUtc] ON [T_SpaceDispatchExecutionAction] ([TenantId], [ApprovalRequestId], [RequestedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260802192420_SpaceE11S05ExecutionReceiptsCompensation', N'8.0.12');
END;
GO

COMMIT;
GO
