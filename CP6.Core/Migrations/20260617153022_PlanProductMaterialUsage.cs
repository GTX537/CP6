using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PlanProductMaterialUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitUsage",
                table: "T_ProductMaterial",
                type: "decimal(21,8)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UsageType",
                table: "T_ProductMaterial",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UsageUnit",
                table: "T_ProductMaterial",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitUsage",
                table: "T_ProductMaterial");

            migrationBuilder.DropColumn(
                name: "UsageType",
                table: "T_ProductMaterial");

            migrationBuilder.DropColumn(
                name: "UsageUnit",
                table: "T_ProductMaterial");
        }
    }
}
