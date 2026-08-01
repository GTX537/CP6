using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE09S01ExternalOrganizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_ExternalOrganization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    BusinessPartnerType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    BusinessPartnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NormalizedCode = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    SecurityStamp = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_Space_ExternalOrganization", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalOrganization_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ExternalOrganization_BusinessPartner", "([BusinessPartnerType] IS NULL AND [BusinessPartnerId] IS NULL) OR ([BusinessPartnerType] IS NOT NULL AND [BusinessPartnerId] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_ExternalOrganization_Status", "[Status] >= 0 AND [Status] <= 2");
                    table.CheckConstraint("CK_Space_ExternalOrganization_Type", "[Type] >= 0 AND [Type] <= 2");
                });

            migrationBuilder.CreateTable(
                name: "Space_ExternalMembership",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidToUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SecurityStamp = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_Space_ExternalMembership", x => x.Id);
                    table.UniqueConstraint("AK_Space_ExternalMembership_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_ExternalMembership_Role", "[Role] >= 0 AND [Role] <= 2");
                    table.CheckConstraint("CK_Space_ExternalMembership_Status", "[Status] >= 0 AND [Status] <= 3");
                    table.CheckConstraint("CK_Space_ExternalMembership_Validity", "[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");
                    table.ForeignKey(
                        name: "FK_Space_ExternalMembership_Organization_Tenant",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Space_ExternalOrganization",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExternalMembership_Tenant_User_Status_Validity",
                table: "Space_ExternalMembership",
                columns: new[] { "TenantId", "UserId", "Status", "ValidFromUtc", "ValidToUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalMembership_Tenant_Organization_User_Current",
                table: "Space_ExternalMembership",
                columns: new[] { "TenantId", "OrganizationId", "UserId" },
                unique: true,
                filter: "[Status] <> 3 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Space_ExternalOrganization_Tenant_Status_Name",
                table: "Space_ExternalOrganization",
                columns: new[] { "TenantId", "Status", "Name" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalOrganization_Tenant_Type_Code",
                table: "Space_ExternalOrganization",
                columns: new[] { "TenantId", "Type", "NormalizedCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_ExternalOrganization_Tenant_Type_Partner",
                table: "Space_ExternalOrganization",
                columns: new[] { "TenantId", "Type", "BusinessPartnerType", "BusinessPartnerId" },
                unique: true,
                filter: "[BusinessPartnerId] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_ExternalMembership");

            migrationBuilder.DropTable(
                name: "Space_ExternalOrganization");
        }
    }
}
