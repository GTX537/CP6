using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SpaceE11S04DispatchApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "T_SpaceDispatchApprovalRequest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecommendationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublishedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RecommendationDefinitionVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RecommendationRequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SelectionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FlowInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecidedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdapterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_T_SpaceDispatchApprovalRequest", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_SpaceDispatchApprovalRequest_TenantId_FlowInstanceId",
                table: "T_SpaceDispatchApprovalRequest",
                columns: new[] { "TenantId", "FlowInstanceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_SpaceDispatchApprovalRequest_TenantId_SiteId_RecommendationId",
                table: "T_SpaceDispatchApprovalRequest",
                columns: new[] { "TenantId", "SiteId", "RecommendationId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] = 'PendingApproval'");

            migrationBuilder.CreateIndex(
                name: "IX_T_SpaceDispatchApprovalRequest_TenantId_SiteId_RequestedAtUtc",
                table: "T_SpaceDispatchApprovalRequest",
                columns: new[] { "TenantId", "SiteId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_SpaceDispatchApprovalRequest");
        }
    }
}
