using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class FinAutoVoucherIdx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_Fin_JournalEntry_AutoVoucherSource",
                table: "Fin_JournalEntry",
                columns: new[] { "Source", "SourceDocNo" },
                unique: true,
                filter: "[Source] <> 0 AND [Status] = 2 AND [SourceDocNo] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Fin_JournalEntry_AutoVoucherSource",
                table: "Fin_JournalEntry");
        }
    }
}
