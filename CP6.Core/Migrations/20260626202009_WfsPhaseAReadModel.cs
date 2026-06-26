using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class WfsPhaseAReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Wf_FlowCc",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtNodeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowCc", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wf_FlowData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StepSeq = table.Column<int>(type: "int", nullable: false),
                    NodeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wf_FlowFormTo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StepSeq = table.Column<int>(type: "int", nullable: false),
                    FromNodeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NodeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NodeCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NodeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpectedHandlerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActualHandlerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OnBehalfOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HandledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowFormTo", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowCc_Instance",
                table: "Wf_FlowCc",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowCc_Recipient",
                table: "Wf_FlowCc",
                columns: new[] { "RecipientId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowData_Step",
                table: "Wf_FlowData",
                columns: new[] { "InstanceId", "StepSeq" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowFormTo_Handler",
                table: "Wf_FlowFormTo",
                columns: new[] { "ExpectedHandlerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowFormTo_Step",
                table: "Wf_FlowFormTo",
                columns: new[] { "InstanceId", "StepSeq" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowFormTo_Token",
                table: "Wf_FlowFormTo",
                columns: new[] { "InstanceId", "TokenId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Wf_FlowCc");

            migrationBuilder.DropTable(
                name: "Wf_FlowData");

            migrationBuilder.DropTable(
                name: "Wf_FlowFormTo");
        }
    }
}
