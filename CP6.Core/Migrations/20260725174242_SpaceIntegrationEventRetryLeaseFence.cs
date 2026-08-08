using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SpaceIntegrationEventRetryLeaseFence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RetryLeaseId",
                table: "T_IntegrationEvent",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_IntegrationEvent_TenantId_RetryLeaseId",
                table: "T_IntegrationEvent",
                columns: new[] { "TenantId", "RetryLeaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_T_IntegrationEvent_TenantId_RetryLeaseId",
                table: "T_IntegrationEvent");

            migrationBuilder.DropColumn(
                name: "RetryLeaseId",
                table: "T_IntegrationEvent");
        }
    }
}
