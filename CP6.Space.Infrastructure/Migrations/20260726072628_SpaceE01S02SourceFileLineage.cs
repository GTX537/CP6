using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE01S02SourceFileLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Space_ModelVersion_TenantId_Id",
                table: "Space_ModelVersion",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "Space_File",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OriginalName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    DeclaredContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DetectedContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Extension = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    ScanEngine = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SignatureVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ScanResultCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RetentionClass = table.Column<short>(type: "smallint", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_File", x => x.Id);
                    table.UniqueConstraint("AK_Space_File_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Space_ModelSource",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceType = table.Column<short>(type: "smallint", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ParserVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MappingProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MappingProfileVersion = table.Column<long>(type: "bigint", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ScaleToMillimeters = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    TransformJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    State = table.Column<short>(type: "smallint", nullable: false),
                    ImportedCommandBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ModelSource", x => x.Id);
                    table.UniqueConstraint("AK_Space_ModelSource_TenantId_ModelVersionId_Id", x => new { x.TenantId, x.ModelVersionId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_ModelSource_File_Tenant",
                        columns: x => new { x.TenantId, x.FileId },
                        principalTable: "Space_File",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_ModelSource_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_Artifact",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ArtifactType = table.Column<short>(type: "smallint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Artifact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Space_Artifact_File_Tenant",
                        columns: x => new { x.TenantId, x.FileId },
                        principalTable: "Space_File",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_Artifact_Source_Tenant_Version",
                        columns: x => new { x.TenantId, x.ModelVersionId, x.SourceId },
                        principalTable: "Space_ModelSource",
                        principalColumns: new[] { "TenantId", "ModelVersionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_Artifact_Version_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Artifact_Tenant_File_Active",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "FileId" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_Artifact_Tenant_Version",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "ModelVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_Artifact_Tenant_Version_Source_Active",
                table: "Space_Artifact",
                columns: new[] { "TenantId", "ModelVersionId", "SourceId" },
                filter: "[SourceId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_File_Tenant_State",
                table: "Space_File",
                columns: new[] { "TenantId", "State" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_File_StorageKey",
                table: "Space_File",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_File_Tenant_Hash_Retention_Reusable",
                table: "Space_File",
                columns: new[] { "TenantId", "Sha256", "RetentionClass" },
                unique: true,
                filter: "[Sha256] IS NOT NULL AND [State] IN (1, 2, 3) AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelSource_Tenant_File_Active",
                table: "Space_ModelSource",
                columns: new[] { "TenantId", "FileId" },
                filter: "[FileId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ModelSource_Tenant_SourceHash",
                table: "Space_ModelSource",
                columns: new[] { "TenantId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ModelSource_Version_Hash_Type_Active",
                table: "Space_ModelSource",
                columns: new[] { "TenantId", "ModelVersionId", "Sha256", "SourceType" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_Artifact");

            migrationBuilder.DropTable(
                name: "Space_ModelSource");

            migrationBuilder.DropTable(
                name: "Space_File");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Space_ModelVersion_TenantId_Id",
                table: "Space_ModelVersion");
        }
    }
}
