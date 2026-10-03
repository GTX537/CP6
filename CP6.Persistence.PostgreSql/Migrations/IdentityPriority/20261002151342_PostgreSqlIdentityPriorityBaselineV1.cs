using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Persistence.PostgreSql.Migrations.IdentityPriority
{
    /// <inheritdoc />
    public partial class PostgreSqlIdentityPriorityBaselineV1 : Migration
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
                name: "Cp6_InboxAggregateCheckpoint",
                schema: "crm_identity_priority",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AggregateVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cp6_InboxAggregateCheckpoint", x => x.Id);
                    table.CheckConstraint("CK_Cp6_InboxAggregateCheckpoint_AggregateId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AggregateId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxAggregateCheckpoint_AggregateId_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_InboxAggregateCheckpoint_ConsumerName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ConsumerName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxAggregateCheckpoint_ConsumerName_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_InboxAggregateCheckpoint_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxAggregateCheckpoint_RowVersion_TokenLength");
                });

            migrationBuilder.CreateTable(
                name: "Cp6_InboxMessage",
                schema: "crm_identity_priority",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    MessageId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicName = table.Column<string>(type: "bpchar", maxLength: 249, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PartitionKey = table.Column<string>(type: "bpchar", maxLength: 512, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    PayloadSha256 = table.Column<string>(type: "bpchar", unicode: false, maxLength: 64, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AggregateId = table.Column<string>(type: "bpchar", maxLength: 128, nullable: false, collation: "cp6_ci_as_provider_v1"),
                    AggregateVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    OutcomeCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    LastErrorCode = table.Column<string>(type: "bpchar", unicode: false, maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    SupportReference = table.Column<string>(type: "bpchar", maxLength: 128, nullable: true, collation: "cp6_ci_as_provider_v1"),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", maxLength: 8, rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cp6_InboxMessage", x => x.Id);
                    table.CheckConstraint("CK_Cp6_InboxMessage_AggregateId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"AggregateId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxMessage_AggregateId_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_InboxMessage_ConsumerName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"ConsumerName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxMessage_ConsumerName_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_InboxMessage_LastErrorCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"LastErrorCode\"), 'UTF8')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxMessage_LastErrorCode_Cp936Capacity");
                    table.CheckConstraint("CK_Cp6_InboxMessage_MessageId_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"MessageId\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxMessage_MessageId_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_InboxMessage_OutcomeCode_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"OutcomeCode\"), 'UTF8')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxMessage_OutcomeCode_Cp936Capacity");
                    table.CheckConstraint("CK_Cp6_InboxMessage_PartitionKey_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"PartitionKey\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 512")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxMessage_PartitionKey_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_InboxMessage_PayloadSha256_Cp936Capacity", "public.cp6_cp936_length_v1(convert_from(pg_catalog.bpcharsend(\"PayloadSha256\"), 'UTF8')) <= 64")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxMessage_PayloadSha256_Cp936Capacity");
                    table.CheckConstraint("CK_Cp6_InboxMessage_RowVersion_TokenLength", "octet_length(\"RowVersion\") = 8")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxMessage_RowVersion_TokenLength");
                    table.CheckConstraint("CK_Cp6_InboxMessage_SupportReference_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"SupportReference\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 128")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxMessage_SupportReference_Utf16Capacity");
                    table.CheckConstraint("CK_Cp6_InboxMessage_TopicName_Utf16Capacity", "char_length(regexp_replace((convert_from(pg_catalog.bpcharsend(\"TopicName\"), 'UTF8'))::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g')) <= 249")
                        .Annotation("CP6:GeneratedCapacity", true)
                        .Annotation("CP6:OriginalName", "CK_Cp6_InboxMessage_TopicName_Utf16Capacity");
                });

            migrationBuilder.CreateTable(
                name: "Cp6_OutboxMessage",
                schema: "crm_identity_priority",
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
                name: "IX_Cp6_InboxAggregateCheckpoint_ConsumerName_Tenan_8bdad6570c8d",
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
            PostgreSqlTokenV1.Install(migrationBuilder, "cp6_storage_identity", PostgreSqlIdentityPriorityTokenTablesV1.All);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("CP6 PostgreSQL storage migrations are forward-only.");
    }
}
