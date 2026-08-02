using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE13S16AiPolicyManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_AiTenantPolicy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    DataPolicy = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    AllowedSiteIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllowedProviderAliasesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MaxConcurrentRuns = table.Column<int>(type: "int", nullable: false),
                    ExternalProviderEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DailyBudgetMinor = table.Column<long>(type: "bigint", nullable: true),
                    MonthlyBudgetMinor = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Space_AiTenantPolicy", x => x.Id);
                    table.UniqueConstraint("AK_Space_AiTenantPolicy_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_AiTenantPolicy_Budget", "([DailyBudgetMinor] IS NULL OR [DailyBudgetMinor] >= 0) AND ([MonthlyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] >= 0) AND ([DailyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] IS NULL OR [MonthlyBudgetMinor] >= [DailyBudgetMinor])");
                    table.CheckConstraint("CK_Space_AiTenantPolicy_Concurrency", "[MaxConcurrentRuns] >= 1 AND [MaxConcurrentRuns] <= 3");
                    table.CheckConstraint("CK_Space_AiTenantPolicy_Currency", "([DailyBudgetMinor] IS NULL AND [MonthlyBudgetMinor] IS NULL) OR [Currency] IS NOT NULL");
                    table.CheckConstraint("CK_Space_AiTenantPolicy_Version", "[Version] >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "UX_AiTenantPolicy_Tenant_Active",
                table: "Space_AiTenantPolicy",
                column: "TenantId",
                unique: true,
                filter: "[IsActive] = 1 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_AiTenantPolicy_Tenant_Version",
                table: "Space_AiTenantPolicy",
                columns: new[] { "TenantId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_AiTenantPolicy");
        }
    }
}
