using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class Phase6OrderCancelAndIntegrationEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "T_Order",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "T_Order",
                type: "datetime2",
                nullable: true);

            // 既存受注的 OrderStatus 落入 "CONFIRMED"（合理默认 — 它们已通过受注作成）
            // 注意：新规受注由 C# entity 默认值 OrderLifecycleStatus.Confirmed 设置
            migrationBuilder.AddColumn<string>(
                name: "OrderStatus",
                table: "T_Order",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "CONFIRMED");

            migrationBuilder.AddColumn<bool>(
                name: "IsAlert",
                table: "Sys_OperLogs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "T_IntegrationEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceModule = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TargetModule = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    HookName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourceNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NextRetryAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_IntegrationEvent", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_Order_OrderStatus_IsDeleted",
                table: "T_Order",
                columns: new[] { "OrderStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_OperLogs_IsAlert_CreateDate",
                table: "Sys_OperLogs",
                columns: new[] { "IsAlert", "CreateDate" });

            migrationBuilder.CreateIndex(
                name: "IX_T_IntegrationEvent_CorrelationId",
                table: "T_IntegrationEvent",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_T_IntegrationEvent_SourceNo_HookName",
                table: "T_IntegrationEvent",
                columns: new[] { "SourceNo", "HookName" });

            migrationBuilder.CreateIndex(
                name: "IX_T_IntegrationEvent_Status_NextRetryAt",
                table: "T_IntegrationEvent",
                columns: new[] { "Status", "NextRetryAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_IntegrationEvent");

            migrationBuilder.DropIndex(
                name: "IX_T_Order_OrderStatus_IsDeleted",
                table: "T_Order");

            migrationBuilder.DropIndex(
                name: "IX_Sys_OperLogs_IsAlert_CreateDate",
                table: "Sys_OperLogs");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "T_Order");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "T_Order");

            migrationBuilder.DropColumn(
                name: "OrderStatus",
                table: "T_Order");

            migrationBuilder.DropColumn(
                name: "IsAlert",
                table: "Sys_OperLogs");
        }
    }
}
