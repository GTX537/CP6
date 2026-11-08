using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261108185000_TenantPayloadDispositionV1")]
public sealed class TenantPayloadDispositionV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_privacy_tenant_payload_disposition_v1;
        ALTER TABLE cp6_privacy_tenant_export_v1.payloads ADD COLUMN revision uuid NOT NULL DEFAULT gen_random_uuid();
        CREATE TABLE cp6_privacy_tenant_payload_disposition_v1.policies(
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,policy_id uuid NOT NULL,
          version uuid NOT NULL UNIQUE,target_tenant uuid NOT NULL,basis_ref uuid NOT NULL,enabled boolean NOT NULL,
          minimum_days integer NOT NULL CHECK(minimum_days BETWEEN 0 AND 36500),require_access_closed boolean NOT NULL,
          owner_id uuid NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE INDEX payload_policy_current ON cp6_privacy_tenant_payload_disposition_v1.policies(environment_id,actor_tenant,policy_id,sequence DESC);
        CREATE TABLE cp6_privacy_tenant_payload_disposition_v1.plans(
          id uuid PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,request_ref uuid NOT NULL,requester_id uuid NOT NULL,
          scope_json text NOT NULL,scope_digest text NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TABLE cp6_privacy_tenant_payload_disposition_v1.events(
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,plan_id uuid NOT NULL REFERENCES cp6_privacy_tenant_payload_disposition_v1.plans,
          version uuid NOT NULL UNIQUE,previous_version uuid,state text NOT NULL CHECK(state IN('REQUESTED','APPROVED','REJECTED','REVOKED')),
          actor_id uuid NOT NULL,decision_ref uuid,operation_id uuid NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TABLE cp6_privacy_tenant_payload_disposition_v1.executions(
          plan_id uuid PRIMARY KEY REFERENCES cp6_privacy_tenant_payload_disposition_v1.plans,approval_version uuid NOT NULL,
          artifact_id uuid NOT NULL UNIQUE,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,operator_id uuid NOT NULL,operation_id uuid NOT NULL,
          scope_json text NOT NULL,receipt_json text NOT NULL,writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
        CREATE TABLE cp6_privacy_tenant_payload_disposition_v1.tombstones(
          artifact_id uuid PRIMARY KEY,source_plan_id uuid NOT NULL UNIQUE,environment_id uuid NOT NULL,operator_tenant uuid NOT NULL,
          target_tenant uuid NOT NULL,policy_id uuid NOT NULL,scope_json text NOT NULL,receipt_json text NOT NULL,
          installed_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
        DO $$ DECLARE n text;BEGIN
          FOREACH n IN ARRAY ARRAY['policies','plans','events','executions','tombstones'] LOOP
            EXECUTE format('CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_tenant_payload_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.gate()',n);
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_tenant_payload_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.immutable()',n);
          END LOOP;
          FOREACH n IN ARRAY ARRAY['policies','tombstones'] LOOP
            EXECUTE format('CREATE TRIGGER restore_source_revision AFTER INSERT ON cp6_privacy_tenant_payload_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.advance_restore_epoch()',n);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_privacy_tenant_payload_disposition_v1.live_origin() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p cp6_privacy_tenant_payload_disposition_v1.plans;e cp6_privacy_tenant_payload_disposition_v1.events;s jsonb;r jsonb;v uuid;l bigint;BEGIN
          SELECT * INTO p FROM cp6_privacy_tenant_payload_disposition_v1.plans WHERE id=NEW.plan_id;
          SELECT * INTO e FROM cp6_privacy_tenant_payload_disposition_v1.events WHERE plan_id=NEW.plan_id ORDER BY sequence DESC LIMIT 1;
          SELECT revision,octet_length(bytes)::bigint INTO v,l FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id=NEW.artifact_id;
          s=NEW.scope_json::jsonb;r=NEW.receipt_json::jsonb;
          IF p.id IS NULL OR v IS NULL OR NEW.writer_xid<>pg_current_xact_id() OR NEW.scope_json::jsonb IS DISTINCT FROM p.scope_json::jsonb
            OR (e.state,e.version) IS DISTINCT FROM ('APPROVED'::text,NEW.approval_version) OR e.actor_id=p.requester_id
            OR (NEW.environment_id,NEW.actor_tenant,NEW.artifact_id,v,l)
              IS DISTINCT FROM ((s->>'EnvironmentId')::uuid,(s->>'OperatorTenantId')::uuid,(s->>'ArtifactId')::uuid,(s->>'PayloadVersion')::uuid,(s->>'PayloadLength')::bigint)
            OR (r->>'Owner',r->>'OwnerOutcome',r->>'ScopeDigest',(r->>'PlanId')::uuid,(r->>'OperationId')::uuid,(r->>'OperatorId')::uuid,
                (r->>'RequesterId')::uuid,(r->>'ApproverId')::uuid,(r->>'ArtifactId')::uuid,(r->>'BeforeVersion')::uuid,(r->>'RemovedBytes')::bigint)
              IS DISTINCT FROM ('Main.Privacy.TenantExportPayload.v1','APPLIED',p.scope_digest,NEW.plan_id,NEW.operation_id,NEW.operator_id,p.requester_id,e.actor_id,NEW.artifact_id,v,l)
            THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_APPROVED_ORIGIN_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER live_origin BEFORE INSERT ON cp6_privacy_tenant_payload_disposition_v1.executions FOR EACH ROW EXECUTE FUNCTION cp6_privacy_tenant_payload_disposition_v1.live_origin();
        CREATE FUNCTION cp6_privacy_tenant_payload_disposition_v1.tomb_origin() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE a cp6_privacy_tenant_export_v1.artifacts;s jsonb;BEGIN
          s=NEW.scope_json::jsonb;SELECT * INTO a FROM cp6_privacy_tenant_export_v1.artifacts WHERE id=NEW.artifact_id;
          IF NEW.installed_xid<>pg_current_xact_id()
            OR (NEW.environment_id,NEW.operator_tenant,NEW.artifact_id,NEW.target_tenant,NEW.policy_id)
              IS DISTINCT FROM ((s->>'EnvironmentId')::uuid,(s->>'OperatorTenantId')::uuid,(s->>'ArtifactId')::uuid,(s->>'TargetTenantId')::uuid,(s->>'PolicyId')::uuid)
            OR a.id IS NOT NULL AND ((a.environment_id,a.actor_tenant,a.target_tenant)
              IS DISTINCT FROM (NEW.environment_id,NEW.operator_tenant,NEW.target_tenant)
              OR convert_from(a.receipt_bytes,'UTF8')::jsonb->>'Sha256' IS DISTINCT FROM s->>'OriginalSha256')
            OR NOT EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.executions x WHERE x.plan_id=NEW.source_plan_id AND x.artifact_id=NEW.artifact_id
              AND x.writer_xid=pg_current_xact_id() AND x.scope_json::jsonb=s AND x.receipt_json::jsonb=NEW.receipt_json::jsonb)

            THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_TOMBSTONE_ORIGIN_REQUIRED' USING ERRCODE='23514';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER tomb_origin BEFORE INSERT ON cp6_privacy_tenant_payload_disposition_v1.tombstones FOR EACH ROW EXECUTE FUNCTION cp6_privacy_tenant_payload_disposition_v1.tomb_origin();
        CREATE FUNCTION cp6_privacy_tenant_payload_disposition_v1.no_resurrection() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.tombstones WHERE artifact_id=NEW.artifact_id)
            THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_DISPOSED_IDENTITY' USING ERRCODE='23514';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER payload_no_resurrection BEFORE INSERT OR UPDATE ON cp6_privacy_tenant_export_v1.payloads FOR EACH ROW EXECUTE FUNCTION cp6_privacy_tenant_payload_disposition_v1.no_resurrection();
        CREATE FUNCTION cp6_privacy_tenant_payload_disposition_v1.active_tenant_hold(environment uuid,target uuid) RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT EXISTS(SELECT FROM (SELECT DISTINCT ON(subject_id) active FROM cp6_privacy_disposition_v1.holds
            WHERE environment_id=environment AND target_tenant=target ORDER BY subject_id,sequence DESC) h WHERE active);
        $$;
        CREATE FUNCTION cp6_privacy_tenant_payload_disposition_v1.owned_delete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_DISPOSITION_REQUIRED' USING ERRCODE='23514';END IF;
          IF EXISTS(SELECT FROM cp6_privacy_tenant_export_v1.artifacts a WHERE a.id=OLD.artifact_id
              AND cp6_privacy_tenant_payload_disposition_v1.active_tenant_hold(a.environment_id,a.target_tenant))
            THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_ACTIVE_HOLD' USING ERRCODE='23514';END IF;
          IF NOT EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.tombstones t WHERE t.artifact_id=OLD.artifact_id AND
              (EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.executions x WHERE x.plan_id=t.source_plan_id AND x.artifact_id=t.artifact_id
                  AND x.writer_xid=pg_current_xact_id() AND (x.scope_json::jsonb->>'PayloadVersion')::uuid=OLD.revision)
               ))
            THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_DISPOSITION_REQUIRED' USING ERRCODE='23514';END IF;RETURN OLD;
        END $$;
        CREATE TRIGGER payload_owned_delete BEFORE DELETE ON cp6_privacy_tenant_export_v1.payloads FOR EACH ROW EXECUTE FUNCTION cp6_privacy_tenant_payload_disposition_v1.owned_delete();
        CREATE TRIGGER payload_no_truncate BEFORE TRUNCATE ON cp6_privacy_tenant_export_v1.payloads FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_tenant_payload_disposition_v1.owned_delete();
        CREATE FUNCTION cp6_privacy_tenant_payload_disposition_v1.live_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_privacy_tenant_payload_disposition_v1.tombstones t WHERE t.artifact_id=NEW.artifact_id AND t.source_plan_id=NEW.plan_id AND t.receipt_json::jsonb=NEW.receipt_json::jsonb)
            OR EXISTS(SELECT FROM cp6_privacy_tenant_export_v1.payloads WHERE artifact_id=NEW.artifact_id)
            OR EXISTS(SELECT FROM cp6_privacy_tenant_export_v1.artifacts WHERE id=NEW.artifact_id AND revoked_at IS NULL)
            OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.actor_tenant AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.operator_id
              AND o.owner='Main.Privacy.TenantExportPayload.v1/'||NEW.environment_id::text||'/'||NEW.operator_id::text AND o.state='COMMITTED'
              AND convert_from(o.result_bytes,'UTF8')::jsonb->'Execution'=NEW.receipt_json::jsonb)
            THEN RAISE EXCEPTION 'PRIVACY_TENANT_PAYLOAD_EFFECT_AND_RECEIPT_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER live_complete AFTER INSERT ON cp6_privacy_tenant_payload_disposition_v1.executions DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION cp6_privacy_tenant_payload_disposition_v1.live_complete();
        REVOKE ALL ON SCHEMA cp6_privacy_tenant_payload_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_privacy_tenant_payload_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cp6_privacy_tenant_payload_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_privacy_tenant_payload_disposition_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.action,v.name,0,t."Id",LOCALTIMESTAMP
        FROM (VALUES('tenant-payload-read','读取租户载荷处置'),('tenant-payload-policy','配置租户载荷保留政策'),('tenant-payload-prepare','申请租户载荷处置'),
          ('tenant-payload-approve','独立批准租户载荷处置'),('tenant-payload-revoke','撤销租户载荷处置批准'),('tenant-payload-execute','实际处置租户载荷'))v(action,name)
        JOIN public."Sys_Menus" m ON m."MenuKey"='privacy-disposition' CROSS JOIN public."Sys_Tenants" t
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.action);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Disposed payload origins and resurrection fences require forward recovery.");
}
