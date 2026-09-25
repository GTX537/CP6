using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations.Plm
{
    /// <inheritdoc />
    public partial class PlmWp0Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "plm");

            migrationBuilder.CreateTable(
                name: "IterationCandidate",
                schema: "plm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IterationKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, collation: "Latin1_General_100_BIN2"),
                    IterationVersion = table.Column<int>(type: "int", nullable: false),
                    ReviewPackageDigest = table.Column<string>(type: "char(64)", nullable: true, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<string>(type: "varchar(12)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IterationCandidate", x => x.Id);
                    table.UniqueConstraint("AK_IterationCandidate_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_PlmIteration_Key", "LEN([IterationKey]) > 0");
                    table.CheckConstraint("CK_PlmIteration_ReviewDigest", "[ReviewPackageDigest] IS NULL OR (LEN([ReviewPackageDigest]) = 64 AND [ReviewPackageDigest] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')");
                    table.CheckConstraint("CK_PlmIteration_Status", "[Status] COLLATE Latin1_General_100_BIN2 = 'OPEN'");
                    table.CheckConstraint("CK_PlmIteration_Version", "[IterationVersion] > 0");
                });

            migrationBuilder.CreateTable(
                name: "EngineeringCandidate",
                schema: "plm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IterationCandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CandidateVersion = table.Column<int>(type: "int", nullable: false),
                    CurrentManifestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "varchar(12)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringCandidate", x => x.Id);
                    table.UniqueConstraint("AK_EngineeringCandidate_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_PlmCandidate_Key", "LEN([CandidateKey]) > 0 AND LEN([ProductCd]) > 0");
                    table.CheckConstraint("CK_PlmCandidate_Status", "[Status] COLLATE Latin1_General_100_BIN2 IN ('DRAFT','VALIDATED','FROZEN')");
                    table.CheckConstraint("CK_PlmCandidate_Version", "[CandidateVersion] > 0");
                    table.ForeignKey(
                        name: "FK_EngineeringCandidate_IterationCandidate_TenantId_IterationCandidateId",
                        columns: x => new { x.TenantId, x.IterationCandidateId },
                        principalSchema: "plm",
                        principalTable: "IterationCandidate",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TechnicalManifest",
                schema: "plm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EngineeringCandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateVersion = table.Column<int>(type: "int", nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "varchar(40)", nullable: false),
                    CanonicalBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ManifestDigest = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    CaptureRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaptureInputHash = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CapturedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicalManifest", x => x.Id);
                    table.UniqueConstraint("AK_TechnicalManifest_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.UniqueConstraint("AK_TechnicalManifest_TenantId_Id_EngineeringCandidateId_CandidateVersion", x => new { x.TenantId, x.Id, x.EngineeringCandidateId, x.CandidateVersion });
                    table.CheckConstraint("CK_PlmManifest_Digests", "LEN([ManifestDigest]) = 64 AND [ManifestDigest] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([CaptureInputHash]) = 64 AND [CaptureInputHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_PlmManifest_Schema", "[SchemaVersion] COLLATE Latin1_General_100_BIN2 = 'plm-technical-manifest-v1' AND DATALENGTH([CanonicalBytes]) > 0");
                    table.CheckConstraint("CK_PlmManifest_Version", "[CandidateVersion] > 0 AND [ItemCount] > 0");
                    table.ForeignKey(
                        name: "FK_TechnicalManifest_EngineeringCandidate_TenantId_EngineeringCandidateId",
                        columns: x => new { x.TenantId, x.EngineeringCandidateId },
                        principalSchema: "plm",
                        principalTable: "EngineeringCandidate",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringBaseline",
                schema: "plm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EngineeringCandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateVersion = table.Column<int>(type: "int", nullable: false),
                    ManifestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestDigest = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    FreezeRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FreezeInputHash = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<string>(type: "varchar(16)", nullable: false),
                    FrozenAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    FrozenBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    SupersededBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupersedeReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SupersedeRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersedeInputHash = table.Column<string>(type: "char(64)", nullable: true, collation: "Latin1_General_100_BIN2"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringBaseline", x => x.Id);
                    table.CheckConstraint("CK_PlmBaseline_Digests", "LEN([ManifestDigest])=64 AND [ManifestDigest] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([FreezeInputHash])=64 AND [FreezeInputHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_PlmBaseline_Status", "[Status] COLLATE Latin1_General_100_BIN2 IN ('FROZEN','SUPERSEDED')");
                    table.CheckConstraint("CK_PlmBaseline_SupersedeAudit", "([Status] COLLATE Latin1_General_100_BIN2='FROZEN' AND [SupersededAtUtc] IS NULL AND [SupersededBy] IS NULL AND [SupersedeReason] IS NULL AND [SupersedeRequestId] IS NULL AND [SupersedeInputHash] IS NULL) OR ([Status] COLLATE Latin1_General_100_BIN2='SUPERSEDED' AND [SupersededAtUtc] IS NOT NULL AND [SupersededBy] IS NOT NULL AND [SupersedeReason] IS NOT NULL AND [SupersedeRequestId] IS NOT NULL AND [SupersedeInputHash] IS NOT NULL)");
                    table.CheckConstraint("CK_PlmBaseline_Version", "[CandidateVersion] > 0");
                    table.ForeignKey(
                        name: "FK_EngineeringBaseline_EngineeringCandidate_TenantId_EngineeringCandidateId",
                        columns: x => new { x.TenantId, x.EngineeringCandidateId },
                        principalSchema: "plm",
                        principalTable: "EngineeringCandidate",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EngineeringBaseline_TechnicalManifest_TenantId_ManifestId_EngineeringCandidateId_CandidateVersion",
                        columns: x => new { x.TenantId, x.ManifestId, x.EngineeringCandidateId, x.CandidateVersion },
                        principalSchema: "plm",
                        principalTable: "TechnicalManifest",
                        principalColumns: new[] { "TenantId", "Id", "EngineeringCandidateId", "CandidateVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TechnicalManifestItem",
                schema: "plm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemType = table.Column<string>(type: "varchar(10)", nullable: false),
                    SourceOwnerType = table.Column<string>(type: "varchar(32)", nullable: false),
                    IdentityKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ProcessCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, collation: "Latin1_General_100_BIN2"),
                    MaterialCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true, collation: "Latin1_General_100_BIN2"),
                    TaskCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, collation: "Latin1_General_100_BIN2"),
                    SemanticSortOrder = table.Column<int>(type: "int", nullable: true),
                    ItemCanonicalBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ItemDigest = table.Column<string>(type: "char(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRowVersion = table.Column<byte[]>(type: "varbinary(8)", nullable: true),
                    CapturedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    CapturedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TechnicalManifestItem", x => x.Id);
                    table.UniqueConstraint("AK_TechnicalManifestItem_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_PlmItem_Bytes", "DATALENGTH([ItemCanonicalBytes]) > 0 AND LEN([IdentityKey]) > 0");
                    table.CheckConstraint("CK_PlmItem_Digest", "LEN([ItemDigest]) = 64 AND [ItemDigest] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_PlmItem_Type", "([ItemType] COLLATE Latin1_General_100_BIN2='PRODUCT' AND [SourceOwnerType] COLLATE Latin1_General_100_BIN2='ProductMaster' AND [ProcessCd] IS NULL AND [MaterialCd] IS NULL AND [TaskCd] IS NULL AND [SemanticSortOrder] IS NULL) OR ([ItemType] COLLATE Latin1_General_100_BIN2='BOM' AND [SourceOwnerType] COLLATE Latin1_General_100_BIN2='ProductMaterial' AND [ProcessCd] IS NOT NULL AND [MaterialCd] IS NOT NULL AND [TaskCd] IS NULL AND [SemanticSortOrder]>0) OR ([ItemType] COLLATE Latin1_General_100_BIN2='ROUTING' AND [SourceOwnerType] COLLATE Latin1_General_100_BIN2='ProductProcess' AND [ProcessCd] IS NULL AND [MaterialCd] IS NULL AND [TaskCd] IS NOT NULL AND [SemanticSortOrder]>0)");
                    table.ForeignKey(
                        name: "FK_TechnicalManifestItem_TechnicalManifest_TenantId_ManifestId",
                        columns: x => new { x.TenantId, x.ManifestId },
                        principalSchema: "plm",
                        principalTable: "TechnicalManifest",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringBaseline_TenantId_ManifestId_EngineeringCandidateId_CandidateVersion",
                schema: "plm",
                table: "EngineeringBaseline",
                columns: new[] { "TenantId", "ManifestId", "EngineeringCandidateId", "CandidateVersion" });

            migrationBuilder.CreateIndex(
                name: "UX_PlmBaseline_Tenant_Candidate_Version",
                schema: "plm",
                table: "EngineeringBaseline",
                columns: new[] { "TenantId", "EngineeringCandidateId", "CandidateVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlmBaseline_Tenant_FreezeRequest",
                schema: "plm",
                table: "EngineeringBaseline",
                columns: new[] { "TenantId", "FreezeRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlmBaseline_Tenant_SupersedeRequest",
                schema: "plm",
                table: "EngineeringBaseline",
                columns: new[] { "TenantId", "SupersedeRequestId" },
                unique: true,
                filter: "[SupersedeRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_PlmCandidate_Tenant_Iteration_Key",
                schema: "plm",
                table: "EngineeringCandidate",
                columns: new[] { "TenantId", "IterationCandidateId", "CandidateKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlmIteration_Tenant_Key_Version",
                schema: "plm",
                table: "IterationCandidate",
                columns: new[] { "TenantId", "IterationKey", "IterationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlmManifest_Tenant_Candidate_Version",
                schema: "plm",
                table: "TechnicalManifest",
                columns: new[] { "TenantId", "EngineeringCandidateId", "CandidateVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlmManifest_Tenant_CaptureRequest",
                schema: "plm",
                table: "TechnicalManifest",
                columns: new[] { "TenantId", "CaptureRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlmItem_Tenant_Manifest_Type_Identity",
                schema: "plm",
                table: "TechnicalManifestItem",
                columns: new[] { "TenantId", "ManifestId", "ItemType", "IdentityKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PlmItem_Tenant_Manifest_Type_SortOrder",
                schema: "plm",
                table: "TechnicalManifestItem",
                columns: new[] { "TenantId", "ManifestId", "ItemType", "SemanticSortOrder" },
                unique: true,
                filter: "[SemanticSortOrder] IS NOT NULL");

            migrationBuilder.Sql("""
CREATE TRIGGER [plm].[TR_PlmIteration_Immutable] ON [plm].[IterationCandidate]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51000, 'PLM immutable record cannot be updated or deleted.', 1;
END
""");
            migrationBuilder.Sql("""
CREATE TRIGGER [plm].[TR_PlmManifest_Immutable] ON [plm].[TechnicalManifest]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51000, 'PLM immutable record cannot be updated or deleted.', 1;
END
""");
            migrationBuilder.Sql("""
CREATE TRIGGER [plm].[TR_PlmItem_Immutable] ON [plm].[TechnicalManifestItem]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51000, 'PLM immutable record cannot be updated or deleted.', 1;
END
""");
            migrationBuilder.Sql("""
CREATE TRIGGER [plm].[TR_PlmCandidate_Protected] ON [plm].[EngineeringCandidate]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id = d.Id
        WHERE i.Id IS NULL OR i.TenantId <> d.TenantId
           OR i.IterationCandidateId <> d.IterationCandidateId
           OR i.CandidateKey <> d.CandidateKey OR i.ProductCd <> d.ProductCd
           OR i.CreatedAtUtc <> d.CreatedAtUtc OR i.CreatedBy COLLATE Latin1_General_100_BIN2 <> d.CreatedBy COLLATE Latin1_General_100_BIN2
           OR NOT (
              (d.Status='DRAFT' AND i.Status='DRAFT' AND d.CurrentManifestId IS NULL AND i.CurrentManifestId IS NOT NULL AND i.CandidateVersion=d.CandidateVersion)
              OR (d.CurrentManifestId IS NOT NULL AND i.CurrentManifestId IS NOT NULL AND i.CurrentManifestId<>d.CurrentManifestId AND i.CandidateVersion=d.CandidateVersion+1 AND i.Status='DRAFT')
              OR (d.Status='DRAFT' AND i.Status='VALIDATED' AND i.CandidateVersion=d.CandidateVersion AND i.CurrentManifestId=d.CurrentManifestId)
              OR (d.Status='VALIDATED' AND i.Status='FROZEN' AND i.CandidateVersion=d.CandidateVersion AND i.CurrentManifestId=d.CurrentManifestId)
           )
    ) THROW 51001, 'PLM EngineeringCandidate transition is invalid.', 1;
END
""");
            migrationBuilder.Sql("""
CREATE TRIGGER [plm].[TR_PlmBaseline_Protected] ON [plm].[EngineeringBaseline]
AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id = d.Id
        WHERE i.Id IS NULL OR i.TenantId<>d.TenantId OR i.EngineeringCandidateId<>d.EngineeringCandidateId
           OR i.CandidateVersion<>d.CandidateVersion OR i.ManifestId<>d.ManifestId
           OR i.ManifestDigest COLLATE Latin1_General_100_BIN2<>d.ManifestDigest COLLATE Latin1_General_100_BIN2 OR i.FreezeRequestId<>d.FreezeRequestId
           OR i.FreezeInputHash<>d.FreezeInputHash OR i.FrozenAtUtc<>d.FrozenAtUtc OR i.FrozenBy COLLATE Latin1_General_100_BIN2<>d.FrozenBy COLLATE Latin1_General_100_BIN2
           OR d.Status<>'FROZEN' OR i.Status<>'SUPERSEDED'
           OR d.SupersededAtUtc IS NOT NULL OR d.SupersededBy IS NOT NULL OR d.SupersedeReason IS NOT NULL
           OR d.SupersedeRequestId IS NOT NULL OR d.SupersedeInputHash IS NOT NULL
           OR i.SupersededAtUtc IS NULL OR i.SupersededBy IS NULL OR i.SupersedeReason IS NULL
           OR i.SupersedeRequestId IS NULL OR i.SupersedeInputHash IS NULL
    ) THROW 51002, 'PLM EngineeringBaseline transition is invalid.', 1;
END
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Disposable local/test databases only. Never roll back a production frozen baseline.
            migrationBuilder.DropTable(
                name: "EngineeringBaseline",
                schema: "plm");

            migrationBuilder.DropTable(
                name: "TechnicalManifestItem",
                schema: "plm");

            migrationBuilder.DropTable(
                name: "TechnicalManifest",
                schema: "plm");

            migrationBuilder.DropTable(
                name: "EngineeringCandidate",
                schema: "plm");

            migrationBuilder.DropTable(
                name: "IterationCandidate",
                schema: "plm");
        }
    }
}
