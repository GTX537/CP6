using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class RemoveArticleAndDashboardRevamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Articles");

            // 文章管理（练习用）整体下线：清理已存在数据库中的菜单与角色菜单（MenuId=1）
            migrationBuilder.Sql("DELETE FROM Sys_RoleMenus WHERE MenuId = 1;");
            migrationBuilder.Sql("DELETE FROM Sys_Menus WHERE MenuId = 1;");
            // 清理与文章相关的 i18n 词条
            migrationBuilder.Sql("DELETE FROM Sys_Langs WHERE LangKey = 'nav.1';");
            migrationBuilder.Sql("DELETE FROM Sys_Langs WHERE LangKey = 'dashboard.totalArticles';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Author = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Articles", x => x.Id);
                });
        }
    }
}
