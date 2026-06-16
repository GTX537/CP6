using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class OaStage3AdvancedFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddSignSource",
                table: "Wf_FlowTask",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAt",
                table: "Wf_FlowTask",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TimeoutHandled",
                table: "Wf_FlowTask",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Wf_FlowDelegate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Remark = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wf_FlowDelegate", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowTask_StatusDue",
                table: "Wf_FlowTask",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowDelegate_GrantorEnable",
                table: "Wf_FlowDelegate",
                columns: new[] { "GrantorId", "Enable" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Wf_FlowDelegate");

            migrationBuilder.DropIndex(
                name: "IX_Wf_FlowTask_StatusDue",
                table: "Wf_FlowTask");

            migrationBuilder.DropColumn(
                name: "AddSignSource",
                table: "Wf_FlowTask");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "Wf_FlowTask");

            migrationBuilder.DropColumn(
                name: "TimeoutHandled",
                table: "Wf_FlowTask");
        }
    }
}
