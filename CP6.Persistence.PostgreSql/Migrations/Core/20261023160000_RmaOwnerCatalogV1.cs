using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261023160000_RmaOwnerCatalogV1")]
public sealed class RmaOwnerCatalogV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'query','读取退货',0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='wms-rma' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a
          WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='query');
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Current Owner action catalogs require explicit forward recovery.");
}
