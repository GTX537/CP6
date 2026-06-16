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
            // 用 IF EXISTS 幂等删：该索引在部分环境存在 schema drift（Phase6 迁移已记录应用但实库实际缺失），
            // 直接 DropIndex 会因索引不存在而中断迁移；存在则照常删，缺失则跳过。
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_Sys_OperLogs_IsAlert_CreateDate] ON [Sys_OperLogs];");

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
