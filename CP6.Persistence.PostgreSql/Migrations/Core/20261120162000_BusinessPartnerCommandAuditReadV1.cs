using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261120162000_BusinessPartnerCommandAuditReadV1")]
public sealed class BusinessPartnerCommandAuditReadV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- 登记独立动作，保留现有租户对角色授予权限的责任。
        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'command-result.audit.read','审计读取交易伙伴原命令',0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='erp-business-partner' AND NOT EXISTS (
            SELECT FROM public."Sys_MenuAction" a
            WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='command-result.audit.read'
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("伙伴审计权限登记只支持前向迁移。");
}
