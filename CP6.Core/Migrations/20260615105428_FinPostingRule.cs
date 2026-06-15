using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class FinPostingRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fin_PostingRule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VoucherSource = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_PostingRule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_PostingRuleLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    AccountRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AmountField = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CarryPartner = table.Column<bool>(type: "bit", nullable: false),
                    CarryCostCenter = table.Column<bool>(type: "bit", nullable: false),
                    FallbackAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LineAccountField = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LineAmountField = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_PostingRuleLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fin_PostingRuleLine_Fin_PostingRule_RuleId",
                        column: x => x.RuleId,
                        principalTable: "Fin_PostingRule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_PostingRule_EventType_IsActive",
                table: "Fin_PostingRule",
                columns: new[] { "EventType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_PostingRuleLine_RuleId",
                table: "Fin_PostingRuleLine",
                column: "RuleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fin_PostingRuleLine");

            migrationBuilder.DropTable(
                name: "Fin_PostingRule");
        }
    }
}
