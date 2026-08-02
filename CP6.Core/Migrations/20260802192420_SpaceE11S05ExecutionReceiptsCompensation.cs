using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE11S05ExecutionReceiptsCompensation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompensatedAtUtc",
                table: "T_SpaceDispatchApprovalRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompensatedById",
                table: "T_SpaceDispatchApprovalRequest",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompensationReason",
                table: "T_SpaceDispatchApprovalRequest",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryAttemptCount",
                table: "T_SpaceDispatchApprovalRequest",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_T_SpaceDispatchApprovalRequest_TenantId_Id",
                table: "T_SpaceDispatchApprovalRequest",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "T_SpaceDispatchExecutionAction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecommendationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AdapterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReceiptJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_SpaceDispatchExecutionAction", x => x.Id);
                    table.CheckConstraint("CK_SpaceDispatchExecutionAction_Status", "[Status] IN ('Applied','FailedNoEffect','RejectedNoEffect')");
                    table.CheckConstraint("CK_SpaceDispatchExecutionAction_Type", "[ActionType] IN ('RetryAssignment','CompensateAssignment')");
                    table.ForeignKey(
                        name: "FK_T_SpaceDispatchExecutionAction_T_SpaceDispatchApprovalRequest_TenantId_ApprovalRequestId",
                        columns: x => new { x.TenantId, x.ApprovalRequestId },
                        principalTable: "T_SpaceDispatchApprovalRequest",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_SpaceDispatchExecutionAction_TenantId_ApprovalRequestId_ActionType",
                table: "T_SpaceDispatchExecutionAction",
                columns: new[] { "TenantId", "ApprovalRequestId", "ActionType" });

            migrationBuilder.CreateIndex(
                name: "IX_T_SpaceDispatchExecutionAction_TenantId_ApprovalRequestId_RequestedAtUtc",
                table: "T_SpaceDispatchExecutionAction",
                columns: new[] { "TenantId", "ApprovalRequestId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_SpaceDispatchExecutionAction");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_T_SpaceDispatchApprovalRequest_TenantId_Id",
                table: "T_SpaceDispatchApprovalRequest");

            migrationBuilder.DropColumn(
                name: "CompensatedAtUtc",
                table: "T_SpaceDispatchApprovalRequest");

            migrationBuilder.DropColumn(
                name: "CompensatedById",
                table: "T_SpaceDispatchApprovalRequest");

            migrationBuilder.DropColumn(
                name: "CompensationReason",
                table: "T_SpaceDispatchApprovalRequest");

            migrationBuilder.DropColumn(
                name: "RetryAttemptCount",
                table: "T_SpaceDispatchApprovalRequest");
        }
    }
}
