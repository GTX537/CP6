using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CP6.Core.Migrations
{
    /// <inheritdoc />
    public partial class MultiTenantWf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Wf_FormDef",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户（TenantContext.DefaultTenant），存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Wf_FormData",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户（TenantContext.DefaultTenant），存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Wf_FlowTask",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户（TenantContext.DefaultTenant），存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Wf_FlowInstance",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户（TenantContext.DefaultTenant），存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Wf_FlowHistory",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户（TenantContext.DefaultTenant），存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Wf_FlowDelegate",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户（TenantContext.DefaultTenant），存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Wf_FlowDef",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户（TenantContext.DefaultTenant），存量数据可见

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Wf_ApprovalBinding",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-0000000000A1"));   // 回填默认租户（TenantContext.DefaultTenant），存量数据可见
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Wf_FormDef");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Wf_FormData");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Wf_FlowTask");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Wf_FlowInstance");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Wf_FlowHistory");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Wf_FlowDelegate");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Wf_FlowDef");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Wf_ApprovalBinding");
        }
    }
}
