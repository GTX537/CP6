using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class PubB1Multirole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MenuKey",
                table: "Sys_Menus",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sys_UserRole",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    Creator = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Modifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModifyDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserRole", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_Menus_MenuKey",
                table: "Sys_Menus",
                column: "MenuKey",
                unique: true,
                filter: "[MenuKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserRole_UserId",
                table: "Sys_UserRole",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserRole_UserId_RoleId",
                table: "Sys_UserRole",
                columns: new[] { "UserId", "RoleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_UserRole");

            migrationBuilder.DropIndex(
                name: "IX_Sys_Menus_MenuKey",
                table: "Sys_Menus");

            migrationBuilder.DropColumn(
                name: "MenuKey",
                table: "Sys_Menus");
        }
    }
}
