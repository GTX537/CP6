using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <summary>
    /// 章10 §2：凭证不可篡改 DB 触发器（双保险）。已过账(Status=2)/已红冲(4)凭证的分录行被 UPDATE/DELETE 时
    /// ROLLBACK + THROW。应用层 JournalEntryService 本就无 Update/Delete（只 Post/Reverse），此为 DB 级兜底——
    /// 防直连 SQL / 未来代码绕过应用层篡改已过账凭证。INSERT（建草稿/红冲）不受限。
    /// </summary>
    public partial class FinJournalLineNoMutateTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_FinJournalLine_NoMutate
ON Fin_JournalLine
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM deleted d
        INNER JOIN Fin_JournalEntry e ON e.Id = d.EntryId
        WHERE e.Status IN (2, 4)
    )
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 50010, N'E-FIN-160: 已过账凭证行不可修改或删除（凭证不可改不可删，只能红冲）', 1;
    END
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF OBJECT_ID('trg_FinJournalLine_NoMutate', 'TR') IS NOT NULL DROP TRIGGER trg_FinJournalLine_NoMutate;");
        }
    }
}
