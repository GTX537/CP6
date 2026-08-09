using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SpaceAnalyticsControlTowerCurrent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_AbcSnapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    WindowFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WindowTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    WindowDays = table.Column<int>(type: "int", nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ThresholdA = table.Column<decimal>(type: "decimal(6,5)", nullable: false),
                    ThresholdB = table.Column<decimal>(type: "decimal(6,5)", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_Space_AbcSnapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Space_AnalyticsConfig",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WindowDays = table.Column<int>(type: "int", nullable: false),
                    Metric = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ThresholdA = table.Column<decimal>(type: "decimal(6,5)", nullable: false),
                    ThresholdB = table.Column<decimal>(type: "decimal(6,5)", nullable: false),
                    StaleAfterHours = table.Column<int>(type: "int", nullable: false),
                    ScheduledHourLocal = table.Column<int>(type: "int", nullable: false),
                    EnableScheduledSnapshot = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_Space_AnalyticsConfig", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Space_AbcSnapshot_TenantId_SiteId_CalculatedAt",
                table: "Space_AbcSnapshot",
                columns: new[] { "TenantId", "SiteId", "CalculatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_AbcSnapshot_TenantId_SiteId_ScheduledDate",
                table: "Space_AbcSnapshot",
                columns: new[] { "TenantId", "SiteId", "ScheduledDate" },
                unique: true,
                filter: "[ScheduledDate] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Space_AnalyticsConfig_TenantId",
                table: "Space_AnalyticsConfig",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_AbcSnapshot");

            migrationBuilder.DropTable(
                name: "Space_AnalyticsConfig");
        }
    }
}
