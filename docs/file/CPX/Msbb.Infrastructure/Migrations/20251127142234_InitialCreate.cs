using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Msbb.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "M_GENERAL",
                columns: table => new
                {
                    ClassCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value1 = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NumValue1 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_M_GENERAL", x => new { x.ClassCode, x.Code });
                });

            migrationBuilder.CreateTable(
                name: "T_SYS_LOG",
                columns: table => new
                {
                    LogSeq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LogDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LoginID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BaseCD = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    UserCD = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LogType = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_SYS_LOG", x => x.LogSeq);
                });

            migrationBuilder.CreateTable(
                name: "T_SYS_MENU",
                columns: table => new
                {
                    MajorCategoryNO = table.Column<int>(type: "int", nullable: false),
                    FunctionNO = table.Column<int>(type: "int", nullable: false),
                    MajorCategoryName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FunctionName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FunctionID = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    DelFlg = table.Column<bool>(type: "bit", nullable: false),
                    InsDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InsUsrID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdUsrID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_T_SYS_MENU", x => new { x.MajorCategoryNO, x.FunctionNO });
                });

            migrationBuilder.InsertData(
                table: "M_GENERAL",
                columns: new[] { "ClassCode", "Code", "DisplayOrder", "Name", "NumValue1", "Value1" },
                values: new object[,]
                {
                    { "UNIT", "01", 1, "個", null, null },
                    { "UNIT", "02", 2, "枚", null, null },
                    { "UNIT", "03", 3, "式", null, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "M_GENERAL");

            migrationBuilder.DropTable(
                name: "T_SYS_LOG");

            migrationBuilder.DropTable(
                name: "T_SYS_MENU");
        }
    }
}
