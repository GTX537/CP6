using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261024011200_ReportCenterOwnerCatalogV1")]
public sealed class ReportCenterOwnerCatalogV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.Sql("""
        -- Keep the existing route/menu identity; action definitions never grant any role a capability.
        UPDATE public."Sys_Menus" SET "MenuKey"='wms-report-center' WHERE "MenuId"=481;
        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.code,v.name,0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('query','读取当前库存及原流水观察'),('export','导出仓储报告')) AS v(code,name)
        WHERE m."MenuKey"='wms-report-center' AND NOT EXISTS
          (SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.code);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)=>throw new NotSupportedException("Report Owner catalog requires explicit forward recovery.");
}
