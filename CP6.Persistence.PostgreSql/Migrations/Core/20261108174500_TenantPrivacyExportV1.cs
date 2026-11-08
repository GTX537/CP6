using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261108174500_TenantPrivacyExportV1")]
public sealed class TenantPrivacyExportV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_privacy_tenant_export_v1;
        CREATE TABLE cp6_privacy_tenant_export_v1.commands(
          environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,actor_id uuid NOT NULL,operation_id uuid NOT NULL,
          state text NOT NULL CHECK(state IN ('COMMITTED','CLOSED')),input_bytes bytea,result_bytes bytea,
          CHECK((state='COMMITTED' AND input_bytes IS NOT NULL AND result_bytes IS NOT NULL) OR (state='CLOSED' AND input_bytes IS NULL AND result_bytes IS NULL)),
          PRIMARY KEY(environment_id,actor_tenant,actor_id,operation_id));
        CREATE TABLE cp6_privacy_tenant_export_v1.artifacts(
          id uuid PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,actor_id uuid NOT NULL,recipient_id uuid NOT NULL,
          target_tenant uuid NOT NULL,policy text NOT NULL,expires_at timestamptz NOT NULL,
          receipt_bytes bytea NOT NULL,revoked_at timestamptz);
        CREATE INDEX tenant_artifacts_target ON cp6_privacy_tenant_export_v1.artifacts(target_tenant);
        CREATE TABLE cp6_privacy_tenant_export_v1.payloads(artifact_id uuid PRIMARY KEY REFERENCES cp6_privacy_tenant_export_v1.artifacts(id),bytes bytea NOT NULL);
        CREATE TABLE cp6_privacy_tenant_export_v1.audit(id uuid PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,actor_id uuid NOT NULL,
          operation_id uuid,artifact_id uuid,kind text NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE FUNCTION cp6_privacy_tenant_export_v1.gate() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN PERFORM cp6_authentication_v1.acquire();RETURN NULL;END $$;
        CREATE FUNCTION cp6_privacy_tenant_export_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'PRIVACY_HISTORY_IMMUTABLE' USING ERRCODE='23514';END $$;
        CREATE FUNCTION cp6_privacy_tenant_export_v1.artifact_change() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP<>'UPDATE' THEN RAISE EXCEPTION 'PRIVACY_ARTIFACT_IDENTITY_IMMUTABLE' USING ERRCODE='23514';END IF;
          IF ROW(NEW.id,NEW.environment_id,NEW.actor_tenant,NEW.actor_id,NEW.recipient_id,NEW.target_tenant,NEW.policy,NEW.expires_at,NEW.receipt_bytes)
             IS DISTINCT FROM ROW(OLD.id,OLD.environment_id,OLD.actor_tenant,OLD.actor_id,OLD.recipient_id,OLD.target_tenant,OLD.policy,OLD.expires_at,OLD.receipt_bytes)
             OR OLD.revoked_at IS NOT NULL OR NEW.revoked_at IS NULL THEN
            RAISE EXCEPTION 'PRIVACY_ARTIFACT_IDENTITY_IMMUTABLE' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        DO $$ DECLARE name text; BEGIN
          FOREACH name IN ARRAY ARRAY['commands','artifacts','payloads','audit'] LOOP
            EXECUTE format('CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_tenant_export_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_tenant_export_v1.gate()',name);
          END LOOP;
          FOREACH name IN ARRAY ARRAY['commands','audit'] LOOP
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_tenant_export_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_tenant_export_v1.immutable()',name);
          END LOOP;
        END $$;
        CREATE TRIGGER artifact_change BEFORE UPDATE OR DELETE ON cp6_privacy_tenant_export_v1.artifacts FOR EACH ROW EXECUTE FUNCTION cp6_privacy_tenant_export_v1.artifact_change();
        CREATE TRIGGER artifact_no_truncate BEFORE TRUNCATE ON cp6_privacy_tenant_export_v1.artifacts FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_tenant_export_v1.immutable();
        CREATE TRIGGER payload_no_rewrite BEFORE UPDATE ON cp6_privacy_tenant_export_v1.payloads FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_tenant_export_v1.immutable();
        -- Payload deletion is reserved for a subsequent explicit disposition Owner. No cleanup job or legal retention period is introduced.
        REVOKE ALL ON SCHEMA cp6_privacy_tenant_export_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_privacy_tenant_export_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_privacy_tenant_export_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.action,v.name,0,t."Id",LOCALTIMESTAMP
        FROM (VALUES('tenant-read','读取租户导出原结果'),('tenant-export','生成租户已分类导出'),('tenant-download','下载租户受控制品'),('tenant-revoke','撤销租户制品访问'),('tenant-scope-identity','租户身份来源'),('tenant-scope-security','租户安全来源'),('tenant-scope-roles','租户角色关系来源'))v(action,name)
        JOIN public."Sys_Menus" m ON m."MenuKey"='privacy-export' CROSS JOIN public."Sys_Tenants" t
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.action);
        -- Deliberately no role grants. Existing platform status alone does not authorize privacy export.
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Privacy operation history is forward-only.");
}
