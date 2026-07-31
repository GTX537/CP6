using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE05S04AssetLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [Space_ElementRevision]
                    WHERE [ModelAssetId] IS NOT NULL
                )
                BEGIN
                    THROW 51000,
                        'E05-S04 requires all legacy ModelAssetId values to be audited and cleared before asset-version enforcement.',
                        1;
                END;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "ModelAssetOwnerTenantId",
                table: "Space_ElementRevision",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "ModelAssetScope",
                table: "Space_ElementRevision",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Space_Asset",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<short>(type: "smallint", nullable: false),
                    OwnerTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_Asset", x => x.Id);
                    table.UniqueConstraint("AK_Space_Asset_Scope_Owner_Id", x => new { x.Scope, x.OwnerTenantId, x.Id });
                    table.CheckConstraint("CK_Space_Asset_ScopeOwner", "([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')");
                });

            migrationBuilder.CreateTable(
                name: "Space_AssetVersion",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<short>(type: "smallint", nullable: false),
                    OwnerTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<long>(type: "bigint", nullable: false),
                    Format = table.Column<short>(type: "smallint", nullable: false),
                    ParameterSchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviewRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RenderArtifactRef = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ContentHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_AssetVersion", x => x.Id);
                    table.UniqueConstraint("AK_Space_AssetVersion_Scope_Owner_Id", x => new { x.Scope, x.OwnerTenantId, x.Id });
                    table.CheckConstraint("CK_Space_AssetVersion_ScopeOwner", "([Scope] = 0 AND [OwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([Scope] = 1 AND [OwnerTenantId] <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_Space_AssetVersion_VersionNo", "[VersionNo] > 0");
                    table.ForeignKey(
                        name: "FK_Space_AssetVersion_Asset_Scope_Owner_Asset",
                        columns: x => new { x.Scope, x.OwnerTenantId, x.AssetId },
                        principalTable: "Space_Asset",
                        principalColumns: new[] { "Scope", "OwnerTenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ElementRevision_ModelAssetScope_ModelAssetOwnerTenantId_ModelAssetId",
                table: "Space_ElementRevision",
                columns: new[] { "ModelAssetScope", "ModelAssetOwnerTenantId", "ModelAssetId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_ElementRevision_ModelAssetScope",
                table: "Space_ElementRevision",
                sql: "([ModelAssetId] IS NULL AND [ModelAssetScope] IS NULL AND [ModelAssetOwnerTenantId] IS NULL) OR ([ModelAssetId] IS NOT NULL AND [ModelAssetScope] IS NOT NULL AND [ModelAssetOwnerTenantId] IS NOT NULL AND (([ModelAssetScope] = 0 AND [ModelAssetOwnerTenantId] = '00000000-0000-0000-0000-000000000000') OR ([ModelAssetScope] = 1 AND [ModelAssetOwnerTenantId] = [TenantId])))");

            migrationBuilder.CreateIndex(
                name: "IX_Space_Asset_Scope_Owner_Category",
                table: "Space_Asset",
                columns: new[] { "Scope", "OwnerTenantId", "Category" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_Asset_Scope_Owner_Code_Active",
                table: "Space_Asset",
                columns: new[] { "Scope", "OwnerTenantId", "AssetCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_AssetVersion_Scope_Owner_Asset_VersionNo",
                table: "Space_AssetVersion",
                columns: new[] { "Scope", "OwnerTenantId", "AssetId", "VersionNo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_ElementRevision_AssetVersion_Scope_Owner_Version",
                table: "Space_ElementRevision",
                columns: new[] { "ModelAssetScope", "ModelAssetOwnerTenantId", "ModelAssetId" },
                principalTable: "Space_AssetVersion",
                principalColumns: new[] { "Scope", "OwnerTenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Space_ElementRevision_AssetVersion_Scope_Owner_Version",
                table: "Space_ElementRevision");

            migrationBuilder.DropTable(
                name: "Space_AssetVersion");

            migrationBuilder.DropTable(
                name: "Space_Asset");

            migrationBuilder.DropIndex(
                name: "IX_Space_ElementRevision_ModelAssetScope_ModelAssetOwnerTenantId_ModelAssetId",
                table: "Space_ElementRevision");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Space_ElementRevision_ModelAssetScope",
                table: "Space_ElementRevision");

            migrationBuilder.DropColumn(
                name: "ModelAssetOwnerTenantId",
                table: "Space_ElementRevision");

            migrationBuilder.DropColumn(
                name: "ModelAssetScope",
                table: "Space_ElementRevision");
        }
    }
}
