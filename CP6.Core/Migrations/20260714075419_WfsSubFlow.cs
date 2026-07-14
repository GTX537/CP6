using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class WfsSubFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentInstanceId",
                table: "Wf_FlowInstance",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentTokenId",
                table: "Wf_FlowInstance",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubIndex",
                table: "Wf_FlowInstance",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowInstance_Parent",
                table: "Wf_FlowInstance",
                columns: new[] { "TenantId", "ParentInstanceId" });

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FlowInstance_SubSlot",
                table: "Wf_FlowInstance",
                columns: new[] { "TenantId", "ParentTokenId", "SubIndex" },
                unique: true,
                filter: "[ParentTokenId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wf_FlowInstance_Parent",
                table: "Wf_FlowInstance");

            migrationBuilder.DropIndex(
                name: "UX_Wf_FlowInstance_SubSlot",
                table: "Wf_FlowInstance");

            migrationBuilder.DropColumn(
                name: "ParentInstanceId",
                table: "Wf_FlowInstance");

            migrationBuilder.DropColumn(
                name: "ParentTokenId",
                table: "Wf_FlowInstance");

            migrationBuilder.DropColumn(
                name: "SubIndex",
                table: "Wf_FlowInstance");
        }
    }
}
