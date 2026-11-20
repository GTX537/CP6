using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261120142000_IntegrationOpsAuthorityV1")]
public sealed class IntegrationOpsAuthorityV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- 登记独立操作权限。实际授予由当前租户的 IAM 管理员办理。
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus"
            WHERE "MenuId">=1 AND "MenuId"<2147483647) available
            WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END
          FROM public."Sys_Menus"),'integration-ops','集成异常与原结果',
          '/erp/integration-ops','Connection',NULL,154,true,LOCALTIMESTAMP
        WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='integration-ops');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('query','查询本租户原记录',0),('evidence','读取原身份与摘要',1),
          ('deliver','重投已提交的原结果',2),('replay','申请原操作受控续行',3),('audit','读取受控重放审计',4)) a(code,name,sort)
        WHERE m."MenuKey"='integration-ops' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("集成操作权限与原审计使用前向恢复。");
}
