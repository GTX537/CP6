using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class Phase10aAddCreditNoteAndReturnedQty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedQty",
                table: "T_OrderDetail",
                type: "decimal(21,8)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "T_CreditNote",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WebOrderNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    RmaNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CustomerCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductCd = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LotNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(21,8)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_CreditNote", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_T_CreditNote_CreditNoteNo",
                table: "T_CreditNote",
                column: "CreditNoteNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_T_CreditNote_WebOrderNo",
                table: "T_CreditNote",
                column: "WebOrderNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "T_CreditNote");

            migrationBuilder.DropColumn(
                name: "ReturnedQty",
                table: "T_OrderDetail");
        }
    }
}
