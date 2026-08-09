using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE07S05WmsAdoption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_WmsAdoption",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdapterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DataSource = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DataSourceKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WmsLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalLocationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WmsLocationCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WmsIsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExternalVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WmsStateHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    LastObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ModelVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocationLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BoundLocationCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BoundAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_Space_WmsAdoption", x => x.Id);
                    table.UniqueConstraint("AK_Space_WmsAdoption_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_WmsAdoption_ModelVersion_Tenant",
                        columns: x => new { x.TenantId, x.ModelVersionId },
                        principalTable: "Space_ModelVersion",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_WmsAdoption_Tenant_Site_Adapter_Status_Code",
                table: "Space_WmsAdoption",
                columns: new[] { "TenantId", "SiteId", "AdapterId", "Status", "WmsLocationCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_WmsAdoption_TenantId_ModelVersionId",
                table: "Space_WmsAdoption",
                columns: new[] { "TenantId", "ModelVersionId" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_WmsAdoption_Tenant_Site_Adapter_External",
                table: "Space_WmsAdoption",
                columns: new[] { "TenantId", "SiteId", "AdapterId", "ExternalLocationId" },
                unique: true,
                filter: "[ExternalLocationId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_WmsAdoption_Tenant_Site_Adapter_Location",
                table: "Space_WmsAdoption",
                columns: new[] { "TenantId", "SiteId", "AdapterId", "LocationLogicalId" },
                unique: true,
                filter: "[LocationLogicalId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_WmsAdoption_Tenant_Site_Adapter_WmsLogical",
                table: "Space_WmsAdoption",
                columns: new[] { "TenantId", "SiteId", "AdapterId", "WmsLogicalId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_WmsAdoption");
        }
    }
}
