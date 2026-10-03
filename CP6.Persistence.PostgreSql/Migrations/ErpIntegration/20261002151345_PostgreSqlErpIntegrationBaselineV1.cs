using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Persistence.PostgreSql.Migrations.ErpIntegration
{
    /// <inheritdoc />
    public partial class PostgreSqlErpIntegrationBaselineV1 : Migration
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
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastRequestVersion = table.Column<int>(type: "integer", nullable: false),
                    LastAggregateVersion = table.Column<int>(type: "integer", nullable: false),
                    LastResultVersion = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aggregate", x => new { x.TenantId, x.Kind, x.AggregateId });
                    table.CheckConstraint("CK_Aggregate_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Aggregate_RowVersion_TokenLength");
                });

            migrationBuilder.CreateTable(
                name: "CommandInbox",
                schema: "erp_integration",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "C"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateVersion = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ConflictCount = table.Column<int>(type: "integer", nullable: false),
                    LastConflictSha256 = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ErrorCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplayedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplayReasonCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandInbox", x => x.MessageId);
                    table.CheckConstraint("CK_CommandInbox_ErrorCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ErrorCode\"), 'UTF8')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_CommandInbox_ErrorCode_Cp936Capacity");
                    table.CheckConstraint("CK_CommandInbox_EventType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"EventType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_CommandInbox_EventType_Utf16Capacity");
                    table.CheckConstraint("CK_CommandInbox_LastConflictSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"LastConflictSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_CommandInbox_LastConflictSha256_Cp936Capacity");
                    table.CheckConstraint("CK_CommandInbox_MessageId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"MessageId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_CommandInbox_MessageId_Utf16Capacity");
                    table.CheckConstraint("CK_CommandInbox_PayloadSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PayloadSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_CommandInbox_PayloadSha256_Cp936Capacity");
                    table.CheckConstraint("CK_CommandInbox_ReplayReasonCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ReplayReasonCode\"), 'UTF8')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_CommandInbox_ReplayReasonCode_Cp936Capacity");
                    table.CheckConstraint("CK_CommandInbox_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_CommandInbox_RowVersion_TokenLength");
                });

            migrationBuilder.CreateTable(
                name: "Cp6_DeadLetterRecord",
                schema: "erp_integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ConsumerName = table.Column<string>(type: "bpchar", maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    PayloadSha256 = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ErrorCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    SupportReference = table.Column<string>(type: "bpchar", maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReplayedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplayReasonCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cp6_DeadLetterRecord", x => x.Id);
                    table.CheckConstraint("CK_Cp6_DeadLetterRecord_ConsumerName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ConsumerName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_DeadLetterRecord_ConsumerName_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_DeadLetterRecord_ErrorCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ErrorCode\"), 'UTF8')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_DeadLetterRecord_ErrorCode_Cp936Capacity");
                    table.CheckConstraint("CK_Cp6_DeadLetterRecord_MessageId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"MessageId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_DeadLetterRecord_MessageId_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_DeadLetterRecord_PayloadSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PayloadSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_DeadLetterRecord_PayloadSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Cp6_DeadLetterRecord_ReplayReasonCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ReplayReasonCode\"), 'UTF8')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_DeadLetterRecord_ReplayReasonCode_Cp936Capacity");
                    table.CheckConstraint("CK_Cp6_DeadLetterRecord_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_DeadLetterRecord_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Cp6_DeadLetterRecord_SupportReference_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SupportReference\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_DeadLetterRecord_SupportReference_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Cp6_OutboxMessage",
                schema: "erp_integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicName = table.Column<string>(type: "bpchar", maxLength: 249, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PartitionKey = table.Column<string>(type: "bpchar", maxLength: 512, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    PayloadSha256 = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CorrelationId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    CausationId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AggregateId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AggregateVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseOwner = table.Column<string>(type: "bpchar", maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LeaseToken = table.Column<string>(type: "bpchar", unicode: false, maxLength: 32, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    SupportReference = table.Column<string>(type: "bpchar", maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cp6_OutboxMessage", x => x.Id);
                    table.CheckConstraint("CK_Cp6_OutboxMessage_AggregateId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AggregateId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_AggregateId_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_CausationId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"CausationId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_CausationId_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_CorrelationId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"CorrelationId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_CorrelationId_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_LastErrorCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"LastErrorCode\"), 'UTF8')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_LastErrorCode_Cp936Capacity");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_LeaseOwner_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"LeaseOwner\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_LeaseOwner_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_LeaseToken_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"LeaseToken\"), 'UTF8')) <= 32")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_LeaseToken_Cp936Capacity");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_MessageId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"MessageId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_MessageId_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_PartitionKey_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"PartitionKey\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 512")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_PartitionKey_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_PayloadSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PayloadSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_PayloadSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_SupportReference_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SupportReference\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_SupportReference_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_OutboxMessage_TopicName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"TopicName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 249")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_OutboxMessage_TopicName_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryReplayAudit",
                schema: "erp_integration",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "bpchar", unicode: false, maxLength: 16, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TargetId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "C"),
                    MessageId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: true, collation: "C"),
                    PayloadSha256 = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    InputRowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, nullable: false),
                    ActorId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ReasonCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PreviousAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ReplayedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryReplayAudit", x => new { x.TenantId, x.OperationId });
                    table.CheckConstraint("CK_DeliveryReplayAudit_ActorId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ActorId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_DeliveryReplayAudit_ActorId_Utf16Capacity");
                    table.CheckConstraint("CK_DeliveryReplayAudit_InputRowVersion_BinaryCapacity", "octet_length(\"InputRowVersion\") <= 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_DeliveryReplayAudit_InputRowVersion_BinaryCapacity");
                    table.CheckConstraint("CK_DeliveryReplayAudit_Kind_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"Kind\"), 'UTF8')) <= 16")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_DeliveryReplayAudit_Kind_Cp936Capacity");
                    table.CheckConstraint("CK_DeliveryReplayAudit_MessageId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"MessageId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_DeliveryReplayAudit_MessageId_Utf16Capacity");
                    table.CheckConstraint("CK_DeliveryReplayAudit_PayloadSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PayloadSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_DeliveryReplayAudit_PayloadSha256_Cp936Capacity");
                    table.CheckConstraint("CK_DeliveryReplayAudit_ReasonCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ReasonCode\"), 'UTF8')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_DeliveryReplayAudit_ReasonCode_Cp936Capacity");
                    table.CheckConstraint("CK_DeliveryReplayAudit_TargetId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"TargetId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_DeliveryReplayAudit_TargetId_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "InboxReplayAudit",
                schema: "erp_integration",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "C"),
                    PayloadSha256 = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    InputRowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, nullable: false),
                    ActorId = table.Column<string>(type: "bpchar", maxLength: 100, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ReasonCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PreviousAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ReplayedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxReplayAudit", x => new { x.TenantId, x.OperationId });
                    table.CheckConstraint("CK_InboxReplayAudit_ActorId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ActorId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 100")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_InboxReplayAudit_ActorId_Utf16Capacity");
                    table.CheckConstraint("CK_InboxReplayAudit_InputRowVersion_BinaryCapacity", "octet_length(\"InputRowVersion\") <= 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_InboxReplayAudit_InputRowVersion_BinaryCapacity");
                    table.CheckConstraint("CK_InboxReplayAudit_MessageId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"MessageId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_InboxReplayAudit_MessageId_Utf16Capacity");
                    table.CheckConstraint("CK_InboxReplayAudit_PayloadSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PayloadSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_InboxReplayAudit_PayloadSha256_Cp936Capacity");
                    table.CheckConstraint("CK_InboxReplayAudit_ReasonCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"ReasonCode\"), 'UTF8')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_InboxReplayAudit_ReasonCode_Cp936Capacity");
                });

            migrationBuilder.CreateTable(
                name: "OrderBridgeDispatch",
                schema: "erp_integration",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderKey = table.Column<string>(type: "bpchar", maxLength: 20, nullable: false, collation: "C"),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AvailableAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LeaseOwner = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LeaseExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderBridgeDispatch", x => new { x.TenantId, x.OrderKey });
                    table.CheckConstraint("CK_OrderBridgeDispatch_LastErrorCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"LastErrorCode\"), 'UTF8')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_OrderBridgeDispatch_LastErrorCode_Cp936Capacity");
                    table.CheckConstraint("CK_OrderBridgeDispatch_LeaseOwner_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"LeaseOwner\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_OrderBridgeDispatch_LeaseOwner_Cp936Capacity");
                    table.CheckConstraint("CK_OrderBridgeDispatch_OrderKey_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"OrderKey\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 20")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_OrderBridgeDispatch_OrderKey_Utf16Capacity");
                    table.CheckConstraint("CK_OrderBridgeDispatch_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_OrderBridgeDispatch_RowVersion_TokenLength");
                });

            migrationBuilder.CreateTable(
                name: "Request",
                schema: "erp_integration",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestVersion = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    InputSha256 = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    Terminal = table.Column<bool>(type: "boolean", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    ResultVersion = table.Column<int>(type: "integer", nullable: false),
                    ResultType = table.Column<string>(type: "bpchar", maxLength: 200, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    ResultDataJson = table.Column<string>(type: "bpchar", nullable: false, collation: "cp6_ci_as_provider_v1"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Request", x => new { x.TenantId, x.Kind, x.AggregateId, x.RequestVersion });
                    table.CheckConstraint("CK_Request_InputSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"InputSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Request_InputSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Request_ResultType_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ResultType\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 200")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Request_ResultType_Utf16Capacity");
                    table.CheckConstraint("CK_Request_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Request_RowVersion_TokenLength");
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
                name: "IX_DeliveryReplayAudit_TenantId_Kind_TargetId_ReplayedAtUtc",
                schema: "erp_integration",
                table: "DeliveryReplayAudit",
                columns: new[] { "TenantId", "Kind", "TargetId", "ReplayedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxReplayAudit_TenantId_MessageId_ReplayedAtUtc",
                schema: "erp_integration",
                table: "InboxReplayAudit",
                columns: new[] { "TenantId", "MessageId", "ReplayedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderBridgeDispatch_CompletedAtUtc_AvailableAtU_dafc50a86d55",
                schema: "erp_integration",
                table: "OrderBridgeDispatch",
                columns: new[] { "CompletedAtUtc", "AvailableAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Request_TenantId_RequestId",
                schema: "erp_integration",
                table: "Request",
                columns: new[] { "TenantId", "RequestId" },
                unique: true);
            PostgreSqlTokenV1.Install(migrationBuilder, "cp6_storage_erp", PostgreSqlErpIntegrationTokenTablesV1.All);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("CP6 PostgreSQL storage migrations are forward-only.");
    }
}
