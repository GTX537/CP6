using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class Phase7AddStockQcStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QcStatus",
                table: "T_Stock",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "PENDING");

            migrationBuilder.CreateIndex(
                name: "IX_T_Stock_QcStatus",
                table: "T_Stock",
                column: "QcStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_T_Stock_QcStatus",
                table: "T_Stock");

            migrationBuilder.DropColumn(
                name: "QcStatus",
                table: "T_Stock");
        }
    }
}
