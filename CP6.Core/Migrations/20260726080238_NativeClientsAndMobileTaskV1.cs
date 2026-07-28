using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class NativeClientsAndMobileTaskV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompletionOperationId",
                table: "T_MobileTask",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppVersion",
                table: "Sys_RefreshTokens",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientKind",
                table: "Sys_RefreshTokens",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Web");

            migrationBuilder.AddColumn<string>(
                name: "DeviceId",
                table: "Sys_RefreshTokens",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Sys_RefreshTokens",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_T_MobileTask_CompletionOperationId",
                table: "T_MobileTask",
                columns: new[] { "TenantId", "CompletionOperationId" },
                unique: true,
                filter: "[CompletionOperationId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_T_MobileTask_CompletionOperationId",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "CompletionOperationId",
                table: "T_MobileTask");

            migrationBuilder.DropColumn(
                name: "AppVersion",
                table: "Sys_RefreshTokens");

            migrationBuilder.DropColumn(
                name: "ClientKind",
                table: "Sys_RefreshTokens");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "Sys_RefreshTokens");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Sys_RefreshTokens");
        }
    }
}
