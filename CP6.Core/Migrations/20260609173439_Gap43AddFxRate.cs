using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class Gap43AddFxRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCd",
                table: "T_WebBusinessPartner",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCd",
                table: "T_Order",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "JPY");

            migrationBuilder.AddColumn<decimal>(
                name: "FxRate",
                table: "T_Order",
                type: "decimal(18,6)",
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.CreateTable(
                name: "T_FxRate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyCd = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    RateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_FxRate", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_FxRate_CurrencyCd_RateDate",
                table: "T_FxRate",
                columns: new[] { "CurrencyCd", "RateDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_FxRate");

            migrationBuilder.DropColumn(
                name: "CurrencyCd",
                table: "T_WebBusinessPartner");

            migrationBuilder.DropColumn(
                name: "CurrencyCd",
                table: "T_Order");

            migrationBuilder.DropColumn(
                name: "FxRate",
                table: "T_Order");
        }
    }
}
