using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261104093000_RequirementConflictAuthorityV1")]
public sealed class RequirementConflictAuthorityV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,10,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        JOIN (VALUES('plm-engineering-requirement','source-confirm','需求来源范围有权确认'),
          ('plm-requirement-set','conflict-decide','需求集冲突裁决')) a(resource,code,name) ON true
        WHERE m."MenuKey"=a.resource AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Source decisions require forward recovery.");
}
