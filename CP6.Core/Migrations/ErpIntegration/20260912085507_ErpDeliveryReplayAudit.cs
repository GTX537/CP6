using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations.ErpIntegration
{
    /// <inheritdoc />
    public partial class ErpDeliveryReplayAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryReplayAudit",
                schema: "erp_integration",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    MessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    PayloadSha256 = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    InputRowVersion = table.Column<byte[]>(type: "varbinary(8)", maxLength: 8, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    PreviousAttemptCount = table.Column<int>(type: "int", nullable: false),
                    ReplayedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryReplayAudit", x => new { x.TenantId, x.OperationId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryReplayAudit_TenantId_Kind_TargetId_ReplayedAtUtc",
                schema: "erp_integration",
                table: "DeliveryReplayAudit",
                columns: new[] { "TenantId", "Kind", "TargetId", "ReplayedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("C03 delivery replay audit is forward-only.");
    }
}
