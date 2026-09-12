using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations.ErpIntegration
{
    /// <inheritdoc />
    public partial class ErpIntegrationQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "erp_integration");

            migrationBuilder.CreateTable(
                name: "Aggregate",
                schema: "erp_integration",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastRequestVersion = table.Column<int>(type: "int", nullable: false),
                    LastAggregateVersion = table.Column<int>(type: "int", nullable: false),
                    LastResultVersion = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aggregate", x => new { x.TenantId, x.Kind, x.AggregateId });
                });

            migrationBuilder.CreateTable(
                name: "CommandInbox",
                schema: "erp_integration",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateVersion = table.Column<int>(type: "int", nullable: false),
                    Payload = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ConflictCount = table.Column<int>(type: "int", nullable: false),
                    LastConflictSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ErrorCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RetryAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplayedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReplayReasonCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandInbox", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "Cp6_DeadLetterRecord",
                schema: "erp_integration",
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
                name: "Cp6_OutboxMessage",
                schema: "erp_integration",
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

            migrationBuilder.CreateTable(
                name: "OrderBridgeDispatch",
                schema: "erp_integration",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    AvailableAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    LeaseOwner = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderBridgeDispatch", x => new { x.TenantId, x.OrderKey });
                });

            migrationBuilder.CreateTable(
                name: "Request",
                schema: "erp_integration",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestVersion = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Terminal = table.Column<bool>(type: "bit", nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    ResultVersion = table.Column<int>(type: "int", nullable: false),
                    ResultType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ResultDataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Request", x => new { x.TenantId, x.Kind, x.AggregateId, x.RequestVersion });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommandInbox_TenantId_Status_RetryAtUtc",
                schema: "erp_integration",
                table: "CommandInbox",
                columns: new[] { "TenantId", "Status", "RetryAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_DeadLetterRecord_CreatedAtUtc",
                schema: "erp_integration",
                table: "Cp6_DeadLetterRecord",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_DeadLetterRecord_Direction_MessageId_ConsumerName",
                schema: "erp_integration",
                table: "Cp6_DeadLetterRecord",
                columns: new[] { "Direction", "MessageId", "ConsumerName" });

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_OutboxMessage_MessageId",
                schema: "erp_integration",
                table: "Cp6_OutboxMessage",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_OutboxMessage_Status_AvailableAtUtc_LeaseExpiresAtUtc",
                schema: "erp_integration",
                table: "Cp6_OutboxMessage",
                columns: new[] { "Status", "AvailableAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Cp6_OutboxMessage_TenantId_AggregateId_AggregateVersion",
                schema: "erp_integration",
                table: "Cp6_OutboxMessage",
                columns: new[] { "TenantId", "AggregateId", "AggregateVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderBridgeDispatch_CompletedAtUtc_AvailableAtUtc_LeaseExpiresAtUtc",
                schema: "erp_integration",
                table: "OrderBridgeDispatch",
                columns: new[] { "CompletedAtUtc", "AvailableAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Request_TenantId_RequestId",
                schema: "erp_integration",
                table: "Request",
                columns: new[] { "TenantId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("C03 transactional integration history is forward-only.");
    }
}
