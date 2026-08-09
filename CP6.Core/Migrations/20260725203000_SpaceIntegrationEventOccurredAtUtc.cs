using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SpaceIntegrationEventOccurredAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredAtUtc",
                table: "T_IntegrationEvent",
                type: "datetime2",
                nullable: true);

            // New Space writers already stored UTC ticks in CreateDate and
            // used a distinct durable JobId. Only this unambiguous identity
            // shape is safe to backfill in SQL. Legacy server-local rows are
            // normalized under an application lock at startup.
            migrationBuilder.Sql(
                """
                UPDATE [T_IntegrationEvent]
                SET [OccurredAtUtc] = [CreateDate]
                WHERE [SourceModule] = N'SPACE'
                  AND [OccurredAtUtc] IS NULL
                  AND [JobId] IS NOT NULL
                  AND [JobId] <> '00000000-0000-0000-0000-000000000000'
                  AND [JobId] <> [Id];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_T_IntegrationEvent_TenantId_SourceModule_CorrelationId_OccurredAtUtc_Id",
                table: "T_IntegrationEvent",
                columns: new[] { "TenantId", "SourceModule", "CorrelationId", "OccurredAtUtc", "Id" },
                descending: new[] { false, false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_T_IntegrationEvent_TenantId_SourceModule_OccurredAtUtc_Id",
                table: "T_IntegrationEvent",
                columns: new[] { "TenantId", "SourceModule", "OccurredAtUtc", "Id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_T_IntegrationEvent_TenantId_SourceModule_CorrelationId_OccurredAtUtc_Id",
                table: "T_IntegrationEvent");

            migrationBuilder.DropIndex(
                name: "IX_T_IntegrationEvent_TenantId_SourceModule_OccurredAtUtc_Id",
                table: "T_IntegrationEvent");

            migrationBuilder.DropColumn(
                name: "OccurredAtUtc",
                table: "T_IntegrationEvent");
        }
    }
}
