using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class WmsFeatureFlagApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_T_WmsFeatureFlag_WarehouseCd",
                table: "T_WmsFeatureFlag");

            migrationBuilder.CreateIndex(
                name: "IX_T_WmsFeatureFlag_TenantId_WarehouseCd",
                table: "T_WmsFeatureFlag",
                columns: new[] { "TenantId", "WarehouseCd" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "T_WmsFeatureFlagChange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BaseProductionMoveEnabled = table.Column<bool>(type: "bit", nullable: false),
                    BaseSerialLpnEnabled = table.Column<bool>(type: "bit", nullable: false),
                    BaseScanRetentionDays = table.Column<int>(type: "int", nullable: false),
                    BaseFeatureRowVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TargetProductionMoveEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TargetSerialLpnEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TargetScanRetentionDays = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ChangeTicket = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EvidenceUri = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FlowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_WmsFeatureFlagChange", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_WmsFeatureFlagChange_TenantId_OperationId",
                table: "T_WmsFeatureFlagChange",
                columns: new[] { "TenantId", "OperationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_WmsFeatureFlagChange_TenantId_WarehouseCd",
                table: "T_WmsFeatureFlagChange",
                columns: new[] { "TenantId", "WarehouseCd" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "IX_T_WmsFeatureFlagChange_TenantId_WarehouseCd_RequestedAtUtc",
                table: "T_WmsFeatureFlagChange",
                columns: new[] { "TenantId", "WarehouseCd", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_WmsFeatureFlagChange");

            migrationBuilder.DropIndex(
                name: "IX_T_WmsFeatureFlag_TenantId_WarehouseCd",
                table: "T_WmsFeatureFlag");

            migrationBuilder.CreateIndex(
                name: "IX_T_WmsFeatureFlag_WarehouseCd",
                table: "T_WmsFeatureFlag",
                column: "WarehouseCd",
                unique: true);
        }
    }
}
