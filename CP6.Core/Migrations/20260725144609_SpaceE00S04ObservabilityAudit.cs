using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE00S04ObservabilityAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                table: "T_IntegrationEvent",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublishAttemptId",
                table: "T_IntegrationEvent",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Space_AuditEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActorName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrganizationContextId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FloorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AuthorizationEvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BeforeHash = table.Column<string>(type: "char(64)", nullable: true),
                    AfterHash = table.Column<string>(type: "char(64)", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraceId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublishAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttemptNo = table.Column<int>(type: "int", nullable: true),
                    ClientType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_AuditEvent", x => x.Id);
                    table.CheckConstraint("CK_Space_AuditEvent_ActorType", "[ActorType] IN ('User','System')");
                    table.CheckConstraint("CK_Space_AuditEvent_Correlation", "[CorrelationId] <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Space_AuditEvent_Outcome", "[Outcome] IN ('Started','Succeeded','Failed','Denied')");
                    table.CheckConstraint("CK_Space_AuditEvent_Tenant", "[TenantId] <> '00000000-0000-0000-0000-000000000000'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_IntegrationEvent_TenantId_CorrelationId",
                table: "T_IntegrationEvent",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_T_IntegrationEvent_TenantId_JobId",
                table: "T_IntegrationEvent",
                columns: new[] { "TenantId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_T_IntegrationEvent_TenantId_PublishAttemptId",
                table: "T_IntegrationEvent",
                columns: new[] { "TenantId", "PublishAttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_AuditEvent_TenantId_CorrelationId_OccurredAtUtc",
                table: "Space_AuditEvent",
                columns: new[] { "TenantId", "CorrelationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_AuditEvent_TenantId_JobId_RunId",
                table: "Space_AuditEvent",
                columns: new[] { "TenantId", "JobId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_AuditEvent_TenantId_OccurredAtUtc",
                table: "Space_AuditEvent",
                columns: new[] { "TenantId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Space_AuditEvent_TenantId_PublishAttemptId_OccurredAtUtc",
                table: "Space_AuditEvent",
                columns: new[] { "TenantId", "PublishAttemptId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Space_AuditEvent");

            migrationBuilder.DropIndex(
                name: "IX_T_IntegrationEvent_TenantId_CorrelationId",
                table: "T_IntegrationEvent");

            migrationBuilder.DropIndex(
                name: "IX_T_IntegrationEvent_TenantId_JobId",
                table: "T_IntegrationEvent");

            migrationBuilder.DropIndex(
                name: "IX_T_IntegrationEvent_TenantId_PublishAttemptId",
                table: "T_IntegrationEvent");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "T_IntegrationEvent");

            migrationBuilder.DropColumn(
                name: "PublishAttemptId",
                table: "T_IntegrationEvent");
        }
    }
}
