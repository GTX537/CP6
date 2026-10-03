BEGIN TRANSACTION;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_Quotation]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_Quotation' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_Quotation; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N'' OR QUOTENAME(@cp6_collation) IS NULL
    THROW 51043, N'BUG143 dbo.T_Quotation.Creator has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    IF EXISTS (SELECT 1 FROM [dbo].[T_Quotation] WITH (TABLOCKX, HOLDLOCK)
               WHERE DATALENGTH([Creator]) > 200)
        THROW 51043, N'BUG143 dbo.T_Quotation.Creator contains more than 100 UTF-16 units; no data changed.', 1;

    -- Revalidate after acquiring the data lock, before using the captured collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_Quotation.Creator metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_Quotation] ALTER COLUMN [Creator] nvarchar(100) COLLATE '
        + QUOTENAME(@cp6_collation) + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_Quotation]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_Quotation' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_Quotation; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N'' OR QUOTENAME(@cp6_collation) IS NULL
    THROW 51043, N'BUG143 dbo.T_Quotation.Modifier has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    IF EXISTS (SELECT 1 FROM [dbo].[T_Quotation] WITH (TABLOCKX, HOLDLOCK)
               WHERE DATALENGTH([Modifier]) > 200)
        THROW 51043, N'BUG143 dbo.T_Quotation.Modifier contains more than 100 UTF-16 units; no data changed.', 1;

    -- Revalidate after acquiring the data lock, before using the captured collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_Quotation.Modifier metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_Quotation] ALTER COLUMN [Modifier] nvarchar(100) COLLATE '
        + QUOTENAME(@cp6_collation) + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_QuotationCalc]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationCalc' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_QuotationCalc; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N'' OR QUOTENAME(@cp6_collation) IS NULL
    THROW 51043, N'BUG143 dbo.T_QuotationCalc.Creator has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    IF EXISTS (SELECT 1 FROM [dbo].[T_QuotationCalc] WITH (TABLOCKX, HOLDLOCK)
               WHERE DATALENGTH([Creator]) > 200)
        THROW 51043, N'BUG143 dbo.T_QuotationCalc.Creator contains more than 100 UTF-16 units; no data changed.', 1;

    -- Revalidate after acquiring the data lock, before using the captured collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationCalc.Creator metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_QuotationCalc] ALTER COLUMN [Creator] nvarchar(100) COLLATE '
        + QUOTENAME(@cp6_collation) + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_QuotationCalc]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationCalc' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_QuotationCalc; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N'' OR QUOTENAME(@cp6_collation) IS NULL
    THROW 51043, N'BUG143 dbo.T_QuotationCalc.Modifier has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    IF EXISTS (SELECT 1 FROM [dbo].[T_QuotationCalc] WITH (TABLOCKX, HOLDLOCK)
               WHERE DATALENGTH([Modifier]) > 200)
        THROW 51043, N'BUG143 dbo.T_QuotationCalc.Modifier contains more than 100 UTF-16 units; no data changed.', 1;

    -- Revalidate after acquiring the data lock, before using the captured collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationCalc.Modifier metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_QuotationCalc] ALTER COLUMN [Modifier] nvarchar(100) COLLATE '
        + QUOTENAME(@cp6_collation) + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_QuotationDetail]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationDetail' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_QuotationDetail; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N'' OR QUOTENAME(@cp6_collation) IS NULL
    THROW 51043, N'BUG143 dbo.T_QuotationDetail.Creator has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    IF EXISTS (SELECT 1 FROM [dbo].[T_QuotationDetail] WITH (TABLOCKX, HOLDLOCK)
               WHERE DATALENGTH([Creator]) > 200)
        THROW 51043, N'BUG143 dbo.T_QuotationDetail.Creator contains more than 100 UTF-16 units; no data changed.', 1;

    -- Revalidate after acquiring the data lock, before using the captured collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Creator' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationDetail.Creator metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_QuotationDetail] ALTER COLUMN [Creator] nvarchar(100) COLLATE '
        + QUOTENAME(@cp6_collation) + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;
GO

DECLARE @cp6_object_id int = OBJECT_ID(N'[dbo].[T_QuotationDetail]', N'U');
IF @cp6_object_id IS NULL OR NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE object_id = @cp6_object_id AND schema_id = SCHEMA_ID(N'dbo')
      AND name COLLATE Latin1_General_100_BIN2 = N'T_QuotationDetail' COLLATE Latin1_General_100_BIN2)
    THROW 51043, N'BUG143 missing exact dbo.T_QuotationDetail; audit capacity repair refused.', 1;

DECLARE @cp6_column_id int, @cp6_max_length smallint, @cp6_collation sysname;
SELECT @cp6_column_id = c.column_id, @cp6_max_length = c.max_length, @cp6_collation = c.collation_name
FROM sys.columns AS c INNER JOIN sys.types AS t ON t.user_type_id = c.user_type_id
WHERE c.object_id = @cp6_object_id
  AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
  AND t.name = N'nvarchar' AND t.is_user_defined = 0 AND t.is_assembly_type = 0
  AND c.system_type_id = TYPE_ID(N'nvarchar')
  AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
  AND c.max_length IN (-1, 200);
IF @cp6_column_id IS NULL OR @cp6_collation IS NULL OR @cp6_collation = N'' OR QUOTENAME(@cp6_collation) IS NULL
    THROW 51043, N'BUG143 dbo.T_QuotationDetail.Modifier has an unknown definition or collation; existing column preserved.', 1;

IF @cp6_max_length = -1
BEGIN
    -- DATALENGTH counts UTF-16 bytes, including trailing spaces and surrogate pairs.
    -- Retain the exclusive table lock until the outer migration transaction finishes.
    IF EXISTS (SELECT 1 FROM [dbo].[T_QuotationDetail] WITH (TABLOCKX, HOLDLOCK)
               WHERE DATALENGTH([Modifier]) > 200)
        THROW 51043, N'BUG143 dbo.T_QuotationDetail.Modifier contains more than 100 UTF-16 units; no data changed.', 1;

    -- Revalidate after acquiring the data lock, before using the captured collation.
    IF NOT EXISTS (
        SELECT 1 FROM sys.columns AS c
        WHERE c.object_id = @cp6_object_id AND c.column_id = @cp6_column_id
          AND c.name COLLATE Latin1_General_100_BIN2 = N'Modifier' COLLATE Latin1_General_100_BIN2
          AND c.user_type_id = TYPE_ID(N'nvarchar') AND c.max_length = -1
          AND c.is_nullable = 1 AND c.is_computed = 0 AND c.is_identity = 0
          AND c.collation_name COLLATE Latin1_General_100_BIN2 = @cp6_collation COLLATE Latin1_General_100_BIN2)
        THROW 51043, N'BUG143 dbo.T_QuotationDetail.Modifier metadata changed; audit capacity repair refused.', 1;

    DECLARE @cp6_alter nvarchar(max) = N'ALTER TABLE [dbo].[T_QuotationDetail] ALTER COLUMN [Modifier] nvarchar(100) COLLATE '
        + QUOTENAME(@cp6_collation) + N' NULL;';
    EXEC sys.sp_executesql @cp6_alter;
END;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261002193500_RestoreQuotationAuditColumnCapacity', N'8.0.30');
GO

COMMIT;
GO

