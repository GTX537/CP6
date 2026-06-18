using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class A2ProductProcessHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CycleTime",
                table: "T_ProductProcess",
                type: "decimal(21,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SetupHour",
                table: "T_ProductProcess",
                type: "decimal(21,8)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardCrewSize",
                table: "T_ProductProcess",
                type: "decimal(21,8)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CycleTime",
                table: "T_ProductProcess");

            migrationBuilder.DropColumn(
                name: "SetupHour",
                table: "T_ProductProcess");

            migrationBuilder.DropColumn(
                name: "StandardCrewSize",
                table: "T_ProductProcess");
        }
    }
}
