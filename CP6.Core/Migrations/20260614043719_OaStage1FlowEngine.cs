using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class OaStage1FlowEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Wf_FlowDef",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FlowName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FormKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowDef", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wf_FlowHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wf_FlowInstance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FlowKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BizType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BizId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CurrentNode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VarsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StarterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowInstance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wf_FlowTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Countersign = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowTask", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FlowDef_FlowKey",
                table: "Wf_FlowDef",
                column: "FlowKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowHistory_Instance",
                table: "Wf_FlowHistory",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowInstance_FlowStatus",
                table: "Wf_FlowInstance",
                columns: new[] { "FlowKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowInstance_Starter",
                table: "Wf_FlowInstance",
                column: "StarterId");

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowTask_AssigneeStatus",
                table: "Wf_FlowTask",
                columns: new[] { "AssigneeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowTask_InstanceNode",
                table: "Wf_FlowTask",
                columns: new[] { "InstanceId", "NodeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Wf_FlowDef");

            migrationBuilder.DropTable(
                name: "Wf_FlowHistory");

            migrationBuilder.DropTable(
                name: "Wf_FlowInstance");

            migrationBuilder.DropTable(
                name: "Wf_FlowTask");
        }
    }
}
