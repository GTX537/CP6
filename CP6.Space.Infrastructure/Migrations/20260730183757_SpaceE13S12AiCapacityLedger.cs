using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE13S12AiCapacityLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Space_AiBudgetReservation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderRequestKey = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PeriodDay = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodMonth = table.Column<int>(type: "int", nullable: false),
                    ReservedCostMinor = table.Column<long>(type: "bigint", nullable: false),
                    ActualCostMinor = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_Space_AiBudgetReservation", x => x.Id);
                    table.UniqueConstraint("AK_Space_AiBudgetReservation_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Space_AiBudgetReservation_Cost", "[ReservedCostMinor] >= 0 AND ([ActualCostMinor] IS NULL OR [ActualCostMinor] >= 0)");
                    table.CheckConstraint("CK_Space_AiBudgetReservation_Currency", "[ReservedCostMinor] = 0 OR [Currency] IS NOT NULL");
                    table.CheckConstraint("CK_Space_AiBudgetReservation_Period", "[PeriodMonth] = YEAR([PeriodDay]) * 100 + MONTH([PeriodDay])");
                    table.ForeignKey(
                        name: "FK_Space_AiBudgetReservation_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Space_TenantAiWorkSlot",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotNo = table.Column<int>(type: "int", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseOwner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_TenantAiWorkSlot", x => new { x.TenantId, x.SlotNo });
                    table.CheckConstraint("CK_Space_TenantAiWorkSlot_Lease", "([RunId] IS NULL AND [LeaseOwner] IS NULL AND [LeaseExpiresAtUtc] IS NULL) OR ([RunId] IS NOT NULL AND [LeaseOwner] IS NOT NULL AND [LeaseExpiresAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_Space_TenantAiWorkSlot_SlotNo", "[SlotNo] >= 1 AND [SlotNo] <= 3");
                    table.ForeignKey(
                        name: "FK_Space_TenantAiWorkSlot_Run_Tenant",
                        columns: x => new { x.TenantId, x.RunId },
                        principalTable: "Space_GenerationRun",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiBudgetReservation_Tenant_Day",
                table: "Space_AiBudgetReservation",
                columns: new[] { "TenantId", "Currency", "PeriodDay", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AiBudgetReservation_Tenant_Month",
                table: "Space_AiBudgetReservation",
                columns: new[] { "TenantId", "Currency", "PeriodMonth", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_AiBudgetReservation_TenantId_RunId",
                table: "Space_AiBudgetReservation",
                columns: new[] { "TenantId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "UX_AiBudgetReservation_Tenant_Request",
                table: "Space_AiBudgetReservation",
                columns: new[] { "TenantId", "ProviderRequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantAiWorkSlot_Tenant_Expiry",
                table: "Space_TenantAiWorkSlot",
                columns: new[] { "TenantId", "LeaseExpiresAtUtc", "SlotNo" });

            migrationBuilder.CreateIndex(
                name: "UX_TenantAiWorkSlot_Tenant_Run",
                table: "Space_TenantAiWorkSlot",
                columns: new[] { "TenantId", "RunId" },
                unique: true,
                filter: "[RunId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_AiBudgetReservation");

            migrationBuilder.DropTable(
                name: "Space_TenantAiWorkSlot");
        }
    }
}
