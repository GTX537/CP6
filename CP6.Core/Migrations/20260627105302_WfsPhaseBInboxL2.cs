using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class WfsPhaseBInboxL2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRead",
                table: "Wf_FlowTask",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                table: "Wf_FlowTask",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowTask_AssigneeRead",
                table: "Wf_FlowTask",
                columns: new[] { "AssigneeId", "IsRead" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wf_FlowTask_AssigneeRead",
                table: "Wf_FlowTask");

            migrationBuilder.DropColumn(
                name: "IsRead",
                table: "Wf_FlowTask");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "Wf_FlowTask");
        }
    }
}
