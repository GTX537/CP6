using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class FinJournalKernel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fin_JournalEntry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    No = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VoucherDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    SourceDocNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MakerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MakerAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CheckerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CheckerAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AutoPosted = table.Column<bool>(type: "bit", nullable: false),
                    ReversedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReverseOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_JournalEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_Sequence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SeqKey = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SeqDate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CurrentValue = table.Column<int>(type: "int", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_Sequence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fin_JournalLine",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PartnerId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CostObjectType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CostObjectId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyCd = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    FxRate = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    OrigAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Memo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fin_JournalLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fin_JournalLine_Fin_JournalEntry_EntryId",
                        column: x => x.EntryId,
                        principalTable: "Fin_JournalEntry",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_JournalEntry_No",
                table: "Fin_JournalEntry",
                column: "No",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fin_JournalEntry_PeriodId_Status",
                table: "Fin_JournalEntry",
                columns: new[] { "PeriodId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Fin_JournalEntry_ReverseOfId",
                table: "Fin_JournalEntry",
                column: "ReverseOfId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_JournalEntry_Status",
                table: "Fin_JournalEntry",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_JournalEntry_VoucherDate",
                table: "Fin_JournalEntry",
                column: "VoucherDate");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_JournalLine_AccountId",
                table: "Fin_JournalLine",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_JournalLine_CostCenterId",
                table: "Fin_JournalLine",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_JournalLine_EntryId",
                table: "Fin_JournalLine",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Fin_Sequence_SeqKey_SeqDate",
                table: "Fin_Sequence",
                columns: new[] { "SeqKey", "SeqDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fin_JournalLine");

            migrationBuilder.DropTable(
                name: "Fin_Sequence");

            migrationBuilder.DropTable(
                name: "Fin_JournalEntry");
        }
    }
}
