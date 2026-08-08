using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE09S02ExternalGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_ExternalGrant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldPolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CanExport = table.Column<bool>(type: "bit", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    GrantVersion = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_Space_ExternalGrant", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalGrant_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ExternalGrant_Status", "[Status] >= 0 AND [Status] <= 2");
                    table.CheckConstraint("CK_Space_ExternalGrant_Validity", "[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");
                    table.CheckConstraint("CK_Space_ExternalGrant_Version", "[GrantVersion] > 0");
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrant_Organization_Tenant",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Space_ExternalOrganization",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalGrantFloor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FloorLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalGrantFloor", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalGrantFloor_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrantFloor_Grant_Tenant",
                        columns: x => new { x.TenantId, x.GrantId },
                        principalTable: "Space_ExternalGrant",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalGrantObject",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessObjectType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NormalizedBusinessObjectType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    BusinessObjectId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedBusinessObjectId = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalGrantObject", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalGrantObject_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrantObject_Grant_Tenant",
                        columns: x => new { x.TenantId, x.GrantId },
                        principalTable: "Space_ExternalGrant",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalGrantOwner",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NormalizedOwnerId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalGrantOwner", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalGrantOwner_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrantOwner_Grant_Tenant",
                        columns: x => new { x.TenantId, x.GrantId },
                        principalTable: "Space_ExternalGrant",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalGrantZone",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneLogicalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    GrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_ExternalGrantZone", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalGrantZone_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Space_ExternalGrantZone_Grant_Tenant",
                        columns: x => new { x.TenantId, x.GrantId },
                        principalTable: "Space_ExternalGrant",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExternalGrant_Organization_Site_Status",
                table: "Space_ExternalGrant",
                columns: new[] { "TenantId", "OrganizationId", "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExternalGrant_Organization_Status_Validity",
                table: "Space_ExternalGrant",
                columns: new[] { "TenantId", "OrganizationId", "Status", "ValidFromUtc", "ValidToUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalGrantFloor_Current",
                table: "Space_ExternalGrantFloor",
                columns: new[] { "TenantId", "GrantId", "FloorLogicalId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalGrantObject_Current",
                table: "Space_ExternalGrantObject",
                columns: new[] { "TenantId", "GrantId", "NormalizedBusinessObjectType", "NormalizedBusinessObjectId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalGrantOwner_Current",
                table: "Space_ExternalGrantOwner",
                columns: new[] { "TenantId", "GrantId", "NormalizedOwnerId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalGrantZone_Current",
                table: "Space_ExternalGrantZone",
                columns: new[] { "TenantId", "GrantId", "ZoneLogicalId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_ExternalGrantFloor");

            migrationBuilder.DropTable(
                name: "Space_ExternalGrantObject");

            migrationBuilder.DropTable(
                name: "Space_ExternalGrantOwner");

            migrationBuilder.DropTable(
                name: "Space_ExternalGrantZone");

            migrationBuilder.DropTable(
                name: "Space_ExternalGrant");
        }
    }
}
