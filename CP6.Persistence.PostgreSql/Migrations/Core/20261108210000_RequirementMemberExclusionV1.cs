using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261108210000_RequirementMemberExclusionV1")]
public sealed class RequirementMemberExclusionV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'exclusion-decide','需求集可选成员排除决定',11,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='plm-requirement-set' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"='exclusion-decide');
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Member exclusion decisions require forward recovery.");
}
