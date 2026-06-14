using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class I18nP3_SysLangTenantAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Sys_Lang_LangKey",
                table: "Sys_Langs");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Sys_Langs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                // i18n 优化 P3：既有/种子词条视为已审校；DB 默认 reviewed（管理页手动新增另置 draft）。
                defaultValue: "reviewed");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Sys_Langs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Sys_Langs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Sys_Langs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Sys_Lang_Tenant_Key",
                table: "Sys_Langs",
                columns: new[] { "TenantId", "LangKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Sys_Lang_Tenant_Key",
                table: "Sys_Langs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Sys_Langs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_Langs");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Sys_Langs");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Sys_Langs");

            migrationBuilder.CreateIndex(
                name: "UX_Sys_Lang_LangKey",
                table: "Sys_Langs",
                column: "LangKey",
                unique: true);
        }
    }
}
