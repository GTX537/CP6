using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261116033000_BusinessPartnerProfileUsesV1")]
public sealed class BusinessPartnerProfileUsesV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",uses.code,uses.name,0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('profile-query','查询业务伙伴档案'),('export','导出业务伙伴列表')) AS uses(code,name)
        WHERE m."MenuKey"='erp-business-partner' AND NOT EXISTS (
            SELECT FROM public."Sys_MenuAction" a
            WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=uses.code
        );
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("业务伙伴档案权限只支持前向迁移。");
}
