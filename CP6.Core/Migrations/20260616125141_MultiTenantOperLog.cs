using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenantOperLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sys_OperLogs_IsAlert_CreateDate",
                table: "Sys_OperLogs");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Sys_OperLogs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // 章10 §8 存量回填：既有审计日志归默认租户（与单租户期行为一致，否则 TenantId=Empty 不匹配过滤而不可见）
            migrationBuilder.Sql(
                "UPDATE [Sys_OperLogs] SET [TenantId] = '00000000-0000-0000-0000-0000000000A1' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_OperLog_Tenant_Alert",
                table: "Sys_OperLogs",
                columns: new[] { "TenantId", "IsAlert", "CreateDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sys_OperLog_Tenant_Alert",
                table: "Sys_OperLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Sys_OperLogs");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_OperLogs_IsAlert_CreateDate",
                table: "Sys_OperLogs",
                columns: new[] { "IsAlert", "CreateDate" });
        }
    }
}
