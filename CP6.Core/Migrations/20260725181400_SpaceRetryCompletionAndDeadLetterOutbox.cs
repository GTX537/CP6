using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SpaceRetryCompletionAndDeadLetterOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeadLetterNotificationLeaseId",
                table: "T_IntegrationEvent",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeadLetterNotificationLeaseUntilUtc",
                table: "T_IntegrationEvent",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeadLetterNotifiedAtUtc",
                table: "T_IntegrationEvent",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetryCompletionLeaseId",
                table: "T_IntegrationEvent",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RetryCompletionSucceeded",
                table: "T_IntegrationEvent",
                type: "bit",
                nullable: true);

            // Existing Space dead letters predate the durable notification
            // outbox. Baseline only those historical rows as already
            // notified so deployment cannot replay stale operator alerts.
            migrationBuilder.Sql(
                """
                UPDATE [T_IntegrationEvent]
                SET [DeadLetterNotifiedAtUtc] = SYSUTCDATETIME()
                WHERE [SourceModule] = N'SPACE'
                  AND [Status] = N'DEAD'
                  AND [DeadLetterNotifiedAtUtc] IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_T_IntegrationEvent_TenantId_Status_DeadLetterNotifiedAtUtc_DeadLetterNotificationLeaseUntilUtc",
                table: "T_IntegrationEvent",
                columns: new[] { "TenantId", "Status", "DeadLetterNotifiedAtUtc", "DeadLetterNotificationLeaseUntilUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_T_IntegrationEvent_TenantId_Status_DeadLetterNotifiedAtUtc_DeadLetterNotificationLeaseUntilUtc",
                table: "T_IntegrationEvent");

            migrationBuilder.DropColumn(
                name: "DeadLetterNotificationLeaseId",
                table: "T_IntegrationEvent");

            migrationBuilder.DropColumn(
                name: "DeadLetterNotificationLeaseUntilUtc",
                table: "T_IntegrationEvent");

            migrationBuilder.DropColumn(
                name: "DeadLetterNotifiedAtUtc",
                table: "T_IntegrationEvent");

            migrationBuilder.DropColumn(
                name: "RetryCompletionLeaseId",
                table: "T_IntegrationEvent");

            migrationBuilder.DropColumn(
                name: "RetryCompletionSucceeded",
                table: "T_IntegrationEvent");
        }
    }
}
