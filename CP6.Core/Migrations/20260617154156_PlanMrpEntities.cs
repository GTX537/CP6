using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PlanMrpEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Plan_MrpRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RunAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScopeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_Plan_MrpRun", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plan_NetRequirement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MrpRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Bucket = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Gross = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    OnHand = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    InTransit = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    InWip = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    FirmPlanned = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    SafetyStock = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    Net = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
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
                    table.PrimaryKey("PK_Plan_NetRequirement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plan_Pegging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlannedOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    SourceRefNo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
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
                    table.PrimaryKey("PK_Plan_Pegging", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plan_PlannedOrder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MrpRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ItemCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    RequiredDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ConvertedDocNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
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
                    table.PrimaryKey("PK_Plan_PlannedOrder", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Plan_MrpRun_Status",
                table: "Plan_MrpRun",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_Plan_MrpRun_No",
                table: "Plan_MrpRun",
                columns: new[] { "TenantId", "RunNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plan_NetRequirement_MrpRunId_ItemCd_Bucket",
                table: "Plan_NetRequirement",
                columns: new[] { "MrpRunId", "ItemCd", "Bucket" });

            migrationBuilder.CreateIndex(
                name: "IX_Plan_Pegging_PlannedOrderId",
                table: "Plan_Pegging",
                column: "PlannedOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Plan_PlannedOrder_ConvertedDocNo",
                table: "Plan_PlannedOrder",
                column: "ConvertedDocNo");

            migrationBuilder.CreateIndex(
                name: "IX_Plan_PlannedOrder_ItemCd_Status_RequiredDate",
                table: "Plan_PlannedOrder",
                columns: new[] { "ItemCd", "Status", "RequiredDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Plan_PlannedOrder_MrpRunId_ItemCd",
                table: "Plan_PlannedOrder",
                columns: new[] { "MrpRunId", "ItemCd" });

            migrationBuilder.CreateIndex(
                name: "IX_Plan_PlannedOrder_Status",
                table: "Plan_PlannedOrder",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Plan_MrpRun");

            migrationBuilder.DropTable(
                name: "Plan_NetRequirement");

            migrationBuilder.DropTable(
                name: "Plan_Pegging");

            migrationBuilder.DropTable(
                name: "Plan_PlannedOrder");
        }
    }
}
