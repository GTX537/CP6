using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class WfsSerialSign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StagePlanJson",
                table: "Wf_FlowToken",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StageIndex",
                table: "Wf_FlowTask",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StageRound",
                table: "Wf_FlowTask",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StageIndex",
                table: "Wf_FlowFormTo",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StageRound",
                table: "Wf_FlowFormTo",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wf_FlowTask_Tally",
                table: "Wf_FlowTask",
                columns: new[] { "InstanceId", "NodeId", "TokenId", "StageIndex", "StageRound", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wf_FlowTask_Tally",
                table: "Wf_FlowTask");

            migrationBuilder.DropColumn(
                name: "StagePlanJson",
                table: "Wf_FlowToken");

            migrationBuilder.DropColumn(
                name: "StageIndex",
                table: "Wf_FlowTask");

            migrationBuilder.DropColumn(
                name: "StageRound",
                table: "Wf_FlowTask");

            migrationBuilder.DropColumn(
                name: "StageIndex",
                table: "Wf_FlowFormTo");

            migrationBuilder.DropColumn(
                name: "StageRound",
                table: "Wf_FlowFormTo");
        }
    }
}
