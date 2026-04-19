using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSysLang : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_Langs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LangKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ZhCN = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ZhTW = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    En = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Ja = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Ko = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_Langs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_Langs");
        }
    }
}
