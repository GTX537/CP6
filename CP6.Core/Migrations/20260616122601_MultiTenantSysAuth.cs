using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenantSysAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sys_Users",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量用户/权限可见且登录可用

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sys_UserRole",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量用户/权限可见且登录可用

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sys_RoleFieldPerm",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量用户/权限可见且登录可用

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sys_RoleDataScope",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量用户/权限可见且登录可用

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sys_RoleAction",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量用户/权限可见且登录可用

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sys_MenuAction",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量用户/权限可见且登录可用

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sys_Dept",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户，存量用户/权限可见且登录可用
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_UserRole");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_RoleFieldPerm");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_RoleDataScope");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_RoleAction");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_MenuAction");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_Dept");
        }
    }
}
