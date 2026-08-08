BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726092519_SpaceE01S05DesignApiIdempotency'
)
BEGIN
    CREATE TABLE [Space_IdempotencyRecord] (
        [Id] uniqueidentifier NOT NULL,
        [PrincipalId] uniqueidentifier NOT NULL,
        [Operation] nvarchar(100) NOT NULL,
        [IdempotencyKeyHash] char(64) NOT NULL,
        [RequestHash] char(64) NOT NULL,
        [ResponseJson] nvarchar(max) NOT NULL,
        [HttpStatusCode] int NOT NULL,
        [ReplayUntilUtc] datetime2 NOT NULL,
        [RetainUntilUtc] datetime2 NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [CreatedAtUtc] datetime2 NOT NULL,
        [CreatedBy] uniqueidentifier NULL,
        [ModifiedAtUtc] datetime2 NULL,
        [ModifiedBy] uniqueidentifier NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_Space_IdempotencyRecord] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726092519_SpaceE01S05DesignApiIdempotency'
)
BEGIN
    CREATE INDEX [IX_Space_IdempotencyRecord_Tenant_Retention] ON [Space_IdempotencyRecord] ([TenantId], [RetainUntilUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726092519_SpaceE01S05DesignApiIdempotency'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Space_IdempotencyRecord_Tenant_Principal_Operation_Key] ON [Space_IdempotencyRecord] ([TenantId], [PrincipalId], [Operation], [IdempotencyKeyHash]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory_Space]
    WHERE [MigrationId] = N'20260726092519_SpaceE01S05DesignApiIdempotency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory_Space] ([MigrationId], [ProductVersion])
    VALUES (N'20260726092519_SpaceE01S05DesignApiIdempotency', N'8.0.12');
END;
GO

COMMIT;
GO
