using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class SecAuthHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "Sys_Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailedLoginAt",
                table: "Sys_Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastLoginIp",
                table: "Sys_Users",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginTime",
                table: "Sys_Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "Sys_Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Sys_Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAt",
                table: "Sys_Users",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "LastFailedLoginAt",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "LastLoginIp",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "LastLoginTime",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Sys_Users");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAt",
                table: "Sys_Users");
        }
    }
}
