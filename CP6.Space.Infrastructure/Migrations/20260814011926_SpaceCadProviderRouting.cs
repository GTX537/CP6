using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceCadProviderRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderKey",
                table: "Space_CadParsePreparation",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderVersion",
                table: "Space_CadParsePreparation",
                type: "varchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Space_CadSiteProviderConfiguration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfigurationRevision = table.Column<long>(type: "bigint", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    ChangeReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_CadSiteProviderConfiguration", x => x.Id);
                    table.UniqueConstraint("AK_Space_CadSiteProviderConfiguration_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Space_CadSiteProviderCertification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderKey = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Role = table.Column<short>(type: "smallint", nullable: false),
                    DeploymentMode = table.Column<short>(type: "smallint", nullable: false),
                    DataBoundary = table.Column<short>(type: "smallint", nullable: false),
                    ApprovalEvidenceReference = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: false),
                    SecretReference = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: true),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SupportsDwg = table.Column<bool>(type: "bit", nullable: false),
                    SupportsDxf = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_CadSiteProviderCertification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Space_CadProviderCertification_Configuration_Tenant",
                        columns: x => new { x.TenantId, x.ConfigurationId },
                        principalTable: "Space_CadSiteProviderConfiguration",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_CadProviderCertification_Site_Expiry",
                table: "Space_CadSiteProviderCertification",
                columns: new[] { "TenantId", "SiteId", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_CadProviderCertification_Provider",
                table: "Space_CadSiteProviderCertification",
                columns: new[] { "TenantId", "ConfigurationId", "ProviderKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_CadProviderCertification_Role",
                table: "Space_CadSiteProviderCertification",
                columns: new[] { "TenantId", "ConfigurationId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_CadProviderConfiguration_Current",
                table: "Space_CadSiteProviderConfiguration",
                columns: new[] { "TenantId", "SiteId" },
                unique: true,
                filter: "[IsCurrent] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Space_CadProviderConfiguration_Site_Revision",
                table: "Space_CadSiteProviderConfiguration",
                columns: new[] { "TenantId", "SiteId", "ConfigurationRevision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_CadSiteProviderCertification");

            migrationBuilder.DropTable(
                name: "Space_CadSiteProviderConfiguration");

            migrationBuilder.DropColumn(
                name: "ProviderKey",
                table: "Space_CadParsePreparation");

            migrationBuilder.DropColumn(
                name: "ProviderVersion",
                table: "Space_CadParsePreparation");
        }
    }
}
