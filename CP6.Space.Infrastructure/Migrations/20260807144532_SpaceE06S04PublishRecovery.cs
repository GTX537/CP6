using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Space.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE06S04PublishRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [Space_PublishAttempt]
                    WHERE [OwnsPublishSlot] = 1 AND [IsDeleted] = 0
                )
                BEGIN
                    THROW 51020, 'Resolve every active E06-S03 publish attempt before applying E06-S04 recovery.', 1;
                END;
                """);

            migrationBuilder.AddColumn<int>(
                name: "BatchAttemptNo",
                table: "Space_PublishBatch",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RequestJson",
                table: "Space_PublishBatch",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{\"items\":[]}");

            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                table: "Space_PublishAttempt",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRetriedAtUtc",
                table: "Space_PublishAttempt",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastRetriedBy",
                table: "Space_PublishAttempt",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManualRetryCount",
                table: "Space_PublishAttempt",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "QueuedAtUtc",
                table: "Space_PublishAttempt",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "RequestJson",
                table: "Space_PublishAttempt",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.Sql(
                """
                UPDATE [Space_PublishAttempt]
                SET [QueuedAtUtc] = [StartedAtUtc]
                WHERE [QueuedAtUtc] = '0001-01-01T00:00:00.0000000';
                """);

            migrationBuilder.CreateTable(
                name: "Space_PublishAuditEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EventNo = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<short>(type: "smallint", nullable: false),
                    AttemptStatus = table.Column<short>(type: "smallint", nullable: false),
                    Step = table.Column<short>(type: "smallint", nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PreviousEventHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                    EventHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Space_PublishAuditEvent", x => x.Id);
                    table.CheckConstraint("CK_Space_PublishAuditEvent_Invariants", "[EventNo] > 0 AND ISJSON([EvidenceJson]) = 1 AND LEN([EvidenceHash]) = 64 AND [EvidenceHash] NOT LIKE '%[^0-9a-f]%' AND LEN([EventHash]) = 64 AND [EventHash] NOT LIKE '%[^0-9a-f]%' AND ([PreviousEventHash] IS NULL OR (LEN([PreviousEventHash]) = 64 AND [PreviousEventHash] NOT LIKE '%[^0-9a-f]%'))");
                    table.ForeignKey(
                        name: "FK_Space_PublishAuditEvent_Attempt_Tenant",
                        columns: x => new { x.TenantId, x.AttemptId },
                        principalTable: "Space_PublishAttempt",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Space_PublishAuditEvent_Job_Tenant",
                        columns: x => new { x.TenantId, x.JobId },
                        principalTable: "Space_Job",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_PublishBatch_Recovery",
                table: "Space_PublishBatch",
                sql: "[AttemptCount] >= 0 AND [BatchAttemptNo] >= 0 AND ISJSON([RequestJson]) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishAttempt_TenantId_JobId",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "JobId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Space_PublishAttempt_Recovery",
                table: "Space_PublishAttempt",
                sql: "[ManualRetryCount] >= 0 AND ISJSON([RequestJson]) = 1 AND (([ManualRetryCount] = 0 AND [LastRetriedAtUtc] IS NULL AND [LastRetriedBy] IS NULL) OR ([ManualRetryCount] > 0 AND [LastRetriedAtUtc] IS NOT NULL AND [LastRetriedBy] IS NOT NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_Space_PublishAuditEvent_Tenant_Job_Occurred",
                table: "Space_PublishAuditEvent",
                columns: new[] { "TenantId", "JobId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishAuditEvent_Tenant_Attempt_Dedupe",
                table: "Space_PublishAuditEvent",
                columns: new[] { "TenantId", "AttemptId", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Space_PublishAuditEvent_Tenant_Attempt_EventNo",
                table: "Space_PublishAuditEvent",
                columns: new[] { "TenantId", "AttemptId", "EventNo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Space_PublishAttempt_Job_Tenant",
                table: "Space_PublishAttempt",
                columns: new[] { "TenantId", "JobId" },
                principalTable: "Space_Job",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "THROW 51020, 'E06-S04 publish recovery evidence is forward-only; apply a higher forward-fix migration.', 1;");
        }
    }
}
