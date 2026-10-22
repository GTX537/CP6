using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261022103000_PlanningOwnerCatalogV1")]
public sealed class PlanningOwnerCatalogV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.action,v.name,0,t."Id",LOCALTIMESTAMP
        FROM (VALUES ('mes-planning-board','query','读取计划看板'),
          ('mes-plan-achievement','query','读取计划达成率'),('mes-plan-achievement','export','导出计划达成率')) v(menu,action,name)
        JOIN public."Sys_Menus" m ON m."MenuKey"=v.menu CROSS JOIN public."Sys_Tenants" t
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" a
          WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.action);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Current Owner action catalogs require explicit forward recovery.");
}
