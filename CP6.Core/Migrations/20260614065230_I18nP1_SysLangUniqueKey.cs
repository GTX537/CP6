using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class I18nP1_SysLangUniqueKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 唯一索引前先去重历史重复 LangKey（保留 Id 最小一行），否则 CreateIndex 会失败。
            migrationBuilder.Sql(@"
DELETE t FROM Sys_Langs t
INNER JOIN (
    SELECT LangKey, MIN(Id) AS KeepId
    FROM Sys_Langs
    GROUP BY LangKey
    HAVING COUNT(*) > 1
) d ON t.LangKey = d.LangKey AND t.Id <> d.KeepId;");

            migrationBuilder.CreateIndex(
                name: "UX_Sys_Lang_LangKey",
                table: "Sys_Langs",
                column: "LangKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Sys_Lang_LangKey",
                table: "Sys_Langs");
        }
    }
}
