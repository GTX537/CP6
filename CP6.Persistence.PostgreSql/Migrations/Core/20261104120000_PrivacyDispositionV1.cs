using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261104120000_PrivacyDispositionV1")]
public sealed class PrivacyDispositionV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_privacy_disposition_v1;
        CREATE TABLE cp6_privacy_disposition_v1.policies(
          sequence bigserial PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,policy_id uuid NOT NULL,
          version uuid UNIQUE NOT NULL,target_tenant uuid NOT NULL,basis_ref uuid NOT NULL,enabled boolean NOT NULL,
          owner_id uuid NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE INDEX policy_latest ON cp6_privacy_disposition_v1.policies(environment_id,actor_tenant,policy_id,sequence DESC);
        CREATE TABLE cp6_privacy_disposition_v1.holds(
          sequence bigserial PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,target_tenant uuid NOT NULL,subject_id uuid NOT NULL,
          version uuid UNIQUE NOT NULL,previous_version uuid,active boolean NOT NULL,basis_ref uuid NOT NULL,review_at timestamptz,
          owner_id uuid NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp(),CHECK(NOT active OR review_at IS NOT NULL));
        CREATE INDEX hold_latest ON cp6_privacy_disposition_v1.holds(environment_id,target_tenant,subject_id,sequence DESC);
        CREATE TABLE cp6_privacy_disposition_v1.plans(
          id uuid PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,request_ref uuid NOT NULL,requester_id uuid NOT NULL,
          scope_json text NOT NULL,scope_digest text NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TABLE cp6_privacy_disposition_v1.plan_events(
          sequence bigserial PRIMARY KEY,plan_id uuid NOT NULL REFERENCES cp6_privacy_disposition_v1.plans,
          version uuid UNIQUE NOT NULL,previous_version uuid,state text NOT NULL CHECK(state IN('REQUESTED','APPROVED','REJECTED','REVOKED')),
          actor_id uuid NOT NULL,decision_ref uuid,operation_id uuid NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp(),
          UNIQUE(plan_id,previous_version));
        CREATE INDEX event_latest ON cp6_privacy_disposition_v1.plan_events(plan_id,sequence DESC);
        CREATE FUNCTION cp6_privacy_disposition_v1.gate() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN PERFORM cp6_authentication_v1.acquire();RETURN NULL;END $$;
        CREATE FUNCTION cp6_privacy_disposition_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'PRIVACY_DISPOSITION_HISTORY_IMMUTABLE' USING ERRCODE='23514';END $$;
        DO $$ DECLARE n text;BEGIN
          FOREACH n IN ARRAY ARRAY['policies','holds','plans','plan_events'] LOOP
            EXECUTE format('CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.gate()',n);
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.immutable()',n);
          END LOOP;
        END $$;
        REVOKE ALL ON SCHEMA cp6_privacy_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_privacy_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cp6_privacy_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_privacy_disposition_v1 FROM PUBLIC;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","OrderNo","Enable","CreateDate")
        SELECT 32981,'privacy-disposition','隐私处置计划','/platform/gdpr',0,true,LOCALTIMESTAMP
        WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='privacy-disposition');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.action,v.name,0,t."Id",LOCALTIMESTAMP
        FROM (VALUES('read','读取处置计划与原回执'),('policy','管理处置政策'),('prepare','准备精确处置计划'),('approve','独立批准处置计划'),
          ('revoke','撤销处置计划'),('hold','管理处置保留限制'))v(action,name)
        JOIN public."Sys_Menus" m ON m."MenuKey"='privacy-disposition' CROSS JOIN public."Sys_Tenants" t
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.action);
        -- No role grants or destructive execution are introduced.
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Privacy disposition decisions are forward-only.");
}
