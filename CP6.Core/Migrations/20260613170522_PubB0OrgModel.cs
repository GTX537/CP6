using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PubB0OrgModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeptId",
                table: "Sys_Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Sys_Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ManagerId",
                table: "Sys_Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sys_Dept",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeptCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DeptName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(900)", maxLength: 900, nullable: false),
                    LeaderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Sort = table.Column<int>(type: "int", nullable: false),
                    Enable = table.Column<bool>(type: "bit", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_Dept", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_Users_DeptId",
                table: "Sys_Users",
                column: "DeptId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_Dept_DeptCode",
                table: "Sys_Dept",
                column: "DeptCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_Dept_ParentId",
                table: "Sys_Dept",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_Dept_Path",
                table: "Sys_Dept",
                column: "Path");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_Dept");

            migrationBuilder.DropIndex(
                name: "IX_Sys_Users_DeptId",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "DeptId",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "ManagerId",
                table: "Sys_Users");
        }
    }
}
