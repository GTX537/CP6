using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class WfsPhaseCprimeDesigner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FlowCode",
                table: "Wf_FlowDef",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FunctionId",
                table: "Wf_FlowDef",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FlowDef_Code",
                table: "Wf_FlowDef",
                columns: new[] { "TenantId", "FlowCode" },
                unique: true,
                filter: "[FlowCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Wf_FlowDef_Function",
                table: "Wf_FlowDef",
                columns: new[] { "TenantId", "FunctionId" },
                unique: true,
                filter: "[FunctionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Wf_FlowDef_Code",
                table: "Wf_FlowDef");

            migrationBuilder.DropIndex(
                name: "UX_Wf_FlowDef_Function",
                table: "Wf_FlowDef");

            migrationBuilder.DropColumn(
                name: "FlowCode",
                table: "Wf_FlowDef");

            migrationBuilder.DropColumn(
                name: "FunctionId",
                table: "Wf_FlowDef");
        }
    }
}
