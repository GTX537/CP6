using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProductionDataIntegrityAndScanRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScanRetentionDays",
                table: "T_WmsFeatureFlag",
                type: "int",
                nullable: false,
                defaultValue: 180);

            migrationBuilder.AlterColumn<string>(
                name: "RelatedNo",
                table: "T_StockTransaction",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(25)",
                oldMaxLength: 25,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScanRetentionDays",
                table: "T_WmsFeatureFlag");

            migrationBuilder.AlterColumn<string>(
                name: "RelatedNo",
                table: "T_StockTransaction",
                type: "nvarchar(25)",
                maxLength: 25,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(36)",
                oldMaxLength: 36,
                oldNullable: true);
        }
    }
}
