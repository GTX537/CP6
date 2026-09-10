using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations.IdentityPriority
{
    /// <inheritdoc />
    public partial class CrmIdentityPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "crm_identity_priority");

            migrationBuilder.CreateTable(
                name: "Cp6_DeadLetterRecord",
                schema: "crm_identity_priority",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConsumerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PayloadSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ErrorCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    SupportReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReplayedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplayReasonCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cp6_DeadLetterRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cp6_InboxAggregateCheckpoint",
                schema: "crm_identity_priority",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AggregateVersion = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cp6_InboxAggregateCheckpoint", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cp6_InboxMessage",
                schema: "crm_identity_priority",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicName = table.Column<string>(type: "nvarchar(249)", maxLength: 249, nullable: false),
                    PartitionKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    PayloadSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AggregateVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    OutcomeCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    SupportReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cp6_InboxMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cp6_OutboxMessage",
                schema: "crm_identity_priority",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TopicName = table.Column<string>(type: "nvarchar(249)", maxLength: 249, nullable: false),
                    PartitionKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Payload = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AggregateVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AvailableAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LeaseToken = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeadLetteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    SupportReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cp6_OutboxMessage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_DeadLetterRecord_CreatedAtUtc",
                schema: "crm_identity_priority",
                table: "Cp6_DeadLetterRecord",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_DeadLetterRecord_Direction_MessageId_ConsumerName",
                schema: "crm_identity_priority",
                table: "Cp6_DeadLetterRecord",
                columns: new[] { "Direction", "MessageId", "ConsumerName" });

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_InboxAggregateCheckpoint_ConsumerName_TenantId_AggregateId",
                schema: "crm_identity_priority",
                table: "Cp6_InboxAggregateCheckpoint",
                columns: new[] { "ConsumerName", "TenantId", "AggregateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_InboxMessage_ConsumerName_MessageId",
                schema: "crm_identity_priority",
                table: "Cp6_InboxMessage",
                columns: new[] { "ConsumerName", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_InboxMessage_Status_ProcessedAtUtc",
                schema: "crm_identity_priority",
                table: "Cp6_InboxMessage",
                columns: new[] { "Status", "ProcessedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_OutboxMessage_MessageId",
                schema: "crm_identity_priority",
                table: "Cp6_OutboxMessage",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_OutboxMessage_Status_AvailableAtUtc_LeaseExpiresAtUtc",
                schema: "crm_identity_priority",
                table: "Cp6_OutboxMessage",
                columns: new[] { "Status", "AvailableAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_OutboxMessage_TenantId_AggregateId_AggregateVersion",
                schema: "crm_identity_priority",
                table: "Cp6_OutboxMessage",
                columns: new[] { "TenantId", "AggregateId", "AggregateVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("C02 authorization history is forward-only.");
    }
}
