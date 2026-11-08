using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261108093000_PrivacyDispositionExecutionV1")]
public sealed class PrivacyDispositionExecutionV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_privacy_disposition_v1.executions(
          plan_id uuid PRIMARY KEY REFERENCES cp6_privacy_disposition_v1.plans,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,
          target_tenant uuid NOT NULL,subject_id uuid NOT NULL,receipt_json text NOT NULL);
        CREATE INDEX execution_subject ON cp6_privacy_disposition_v1.executions(environment_id,target_tenant,subject_id);
        CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_disposition_v1.executions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.gate();
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_disposition_v1.executions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.immutable();
        REVOKE ALL ON cp6_privacy_disposition_v1.executions FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'execute','执行已批准主体资料匿名化',0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t WHERE m."MenuKey"='privacy-disposition'
        AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='execute');
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Privacy execution receipts are forward-only.");
}
