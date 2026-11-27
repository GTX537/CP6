using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261126235900_EngineeringRequirementExportV1")]
public sealed class EngineeringRequirementExportV1 : Migration
{
    protected override void Up(MigrationBuilder migration) => migration.Sql("""
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'export','导出当前工程需求范围',10,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='plm-engineering-requirement' AND NOT EXISTS (
            SELECT FROM public."Sys_MenuAction" a
            WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='export'
        );
        """);

    protected override void Down(MigrationBuilder migration) =>
        throw new NotSupportedException("工程需求导出权限变更使用前向迁移。");
}
