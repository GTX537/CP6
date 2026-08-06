BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [GenerationProposalId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [GenerationRunId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [ResolutionDecisionId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD [ResolutionKind] smallint NOT NULL DEFAULT CAST(0 AS smallint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    UPDATE [Space_ModelIssue] SET [ResolutionKind] = 1 WHERE [Status] = 1 AND [ResolutionCommandBatchId] IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    CREATE TABLE [Space_GenerationLockedFact] (
        [Id] uniqueidentifier NOT NULL,
        [RunId] uniqueidentifier NOT NULL,
        [BasedOnRunId] uniqueidentifier NOT NULL,
        [SourceProposalId] uniqueidentifier NOT NULL,
        [SourceDecisionId] uniqueidentifier NOT NULL,
        [SourceHash] char(64) NOT NULL,
        [SourceKey] nvarchar(256) NOT NULL,
        [ProposalType] nvarchar(64) NOT NULL,
        [FieldPath] nvarchar(256) NOT NULL,
        [ValueJson] nvarchar(max) NOT NULL,
        [MatchMethod] smallint NOT NULL,
        [MatchScore] decimal(6,5) NOT NULL,
        [IsConfirmed] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_GenerationLockedFact] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Space_GenerationLockedFact_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [CK_Space_GenerationLockedFact_Match] CHECK ([MatchScore] >= 0 AND [MatchScore] <= 1 AND [RunId] <> [BasedOnRunId]),
        CONSTRAINT [FK_Space_GenerationLockedFact_BasedOnRun_Tenant] FOREIGN KEY ([TenantId], [BasedOnRunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationLockedFact_Decision_Tenant] FOREIGN KEY ([TenantId], [SourceDecisionId]) REFERENCES [Space_ProposalDecision] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationLockedFact_Proposal_Tenant_Run] FOREIGN KEY ([TenantId], [BasedOnRunId], [SourceProposalId]) REFERENCES [Space_GenerationProposal] ([TenantId], [RunId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Space_GenerationLockedFact_Run_Tenant] FOREIGN KEY ([TenantId], [RunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Space_ModelIssue_Tenant_Run_Proposal_Status] ON [Space_ModelIssue] ([TenantId], [GenerationRunId], [GenerationProposalId], [Status], [Severity]) WHERE [GenerationRunId] IS NOT NULL AND [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    CREATE INDEX [IX_Space_ModelIssue_TenantId_ResolutionDecisionId] ON [Space_ModelIssue] ([TenantId], [ResolutionDecisionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [CK_Space_ModelIssue_GenerationScope] CHECK (([GenerationProposalId] IS NULL OR [GenerationRunId] IS NOT NULL) AND ([ResolutionDecisionId] IS NULL OR [GenerationProposalId] IS NOT NULL))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    EXEC(N'ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [CK_Space_ModelIssue_Resolution] CHECK (([Status] <> 1 AND [ResolutionKind] = 0 AND [ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NULL) OR ([Status] = 1 AND (([ResolutionKind] = 1 AND [ResolutionCommandBatchId] IS NOT NULL AND [ResolutionDecisionId] IS NULL) OR ([ResolutionKind] IN (2, 3) AND [ResolutionCommandBatchId] IS NULL AND [ResolutionDecisionId] IS NOT NULL))))');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    CREATE INDEX [IX_GenerationLockedFact_Tenant_Decision_Run] ON [Space_GenerationLockedFact] ([TenantId], [SourceDecisionId], [RunId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    CREATE INDEX [IX_Space_GenerationLockedFact_TenantId_BasedOnRunId_SourceProposalId] ON [Space_GenerationLockedFact] ([TenantId], [BasedOnRunId], [SourceProposalId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_GenerationLockedFact_Tenant_Run_Source_Type_Field] ON [Space_GenerationLockedFact] ([TenantId], [RunId], [SourceKey], [ProposalType], [FieldPath]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [FK_Space_ModelIssue_GenerationRun_Tenant] FOREIGN KEY ([TenantId], [GenerationRunId]) REFERENCES [Space_GenerationRun] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [FK_Space_ModelIssue_Proposal_Tenant_Run] FOREIGN KEY ([TenantId], [GenerationRunId], [GenerationProposalId]) REFERENCES [Space_GenerationProposal] ([TenantId], [RunId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    ALTER TABLE [Space_ModelIssue] ADD CONSTRAINT [FK_Space_ModelIssue_ResolutionDecision_Tenant] FOREIGN KEY ([TenantId], [ResolutionDecisionId]) REFERENCES [Space_ProposalDecision] ([TenantId], [Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260806054950_SpaceE13S09ProposalDecisions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260806054950_SpaceE13S09ProposalDecisions', N'8.0.12');
END;
GO

COMMIT;
GO
