using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class OaP0DraftAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DispatchAttempts",
                table: "Wf_Notification",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DispatchStatus",
                table: "Wf_Notification",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DispatchedAtUtc",
                table: "Wf_Notification",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailRequested",
                table: "Wf_Notification",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EventKey",
                table: "Wf_Notification",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InAppRequested",
                table: "Wf_Notification",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastDispatchError",
                table: "Wf_Notification",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAtUtc",
                table: "Wf_Notification",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wf_Notification_Dispatch",
                table: "Wf_Notification",
                columns: new[] { "TenantId", "DispatchStatus", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Wf_Notification_Event",
                table: "Wf_Notification",
                columns: new[] { "TenantId", "EventKey" },
                unique: true,
                filter: "[EventKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowTask_PendingPage",
                table: "Wf_FlowTask",
                columns: new[] { "TenantId", "AssigneeId", "Status", "InstanceId", "CreateDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wf_Notification_Dispatch",
                table: "Wf_Notification");

            migrationBuilder.DropIndex(
                name: "UX_Wf_Notification_Event",
                table: "Wf_Notification");

            migrationBuilder.DropIndex(
                name: "IX_Wf_FlowTask_PendingPage",
                table: "Wf_FlowTask");

            migrationBuilder.DropColumn(
                name: "DispatchAttempts",
                table: "Wf_Notification");

            migrationBuilder.DropColumn(
                name: "DispatchStatus",
                table: "Wf_Notification");

            migrationBuilder.DropColumn(
                name: "DispatchedAtUtc",
                table: "Wf_Notification");

            migrationBuilder.DropColumn(
                name: "EmailRequested",
                table: "Wf_Notification");

            migrationBuilder.DropColumn(
                name: "EventKey",
                table: "Wf_Notification");

            migrationBuilder.DropColumn(
                name: "InAppRequested",
                table: "Wf_Notification");

            migrationBuilder.DropColumn(
                name: "LastDispatchError",
                table: "Wf_Notification");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "Wf_Notification");
        }
    }
}
