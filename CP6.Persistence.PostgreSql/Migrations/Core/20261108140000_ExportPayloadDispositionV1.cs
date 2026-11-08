using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261108140000_ExportPayloadDispositionV1")]
public sealed class ExportPayloadDispositionV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_privacy_export_disposition_v1;
        ALTER TABLE cp6_privacy_export_v1.payloads ADD COLUMN revision uuid NOT NULL DEFAULT gen_random_uuid();
        ALTER TABLE cp6_privacy_disposition_v1.restore_receipts ADD COLUMN writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id();
        CREATE TABLE cp6_privacy_export_disposition_v1.policies(
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,policy_id uuid NOT NULL,
          version uuid NOT NULL UNIQUE,target_tenant uuid NOT NULL,basis_ref uuid NOT NULL,enabled boolean NOT NULL,
          minimum_days integer NOT NULL CHECK(minimum_days BETWEEN 0 AND 36500),require_access_closed boolean NOT NULL,
          owner_id uuid NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE INDEX payload_policy_current ON cp6_privacy_export_disposition_v1.policies(environment_id,actor_tenant,policy_id,sequence DESC);
        CREATE TABLE cp6_privacy_export_disposition_v1.plans(
          id uuid PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,request_ref uuid NOT NULL,requester_id uuid NOT NULL,
          scope_json text NOT NULL,scope_digest text NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TABLE cp6_privacy_export_disposition_v1.events(
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,plan_id uuid NOT NULL REFERENCES cp6_privacy_export_disposition_v1.plans,
          version uuid NOT NULL UNIQUE,previous_version uuid,state text NOT NULL CHECK(state IN('REQUESTED','APPROVED','REJECTED','REVOKED')),
          actor_id uuid NOT NULL,decision_ref uuid,operation_id uuid NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TABLE cp6_privacy_export_disposition_v1.executions(
          plan_id uuid PRIMARY KEY REFERENCES cp6_privacy_export_disposition_v1.plans,approval_version uuid NOT NULL,
          artifact_id uuid NOT NULL UNIQUE,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,operator_id uuid NOT NULL,operation_id uuid NOT NULL,
          scope_json text NOT NULL,receipt_json text NOT NULL,writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
        CREATE TABLE cp6_privacy_export_disposition_v1.tombstones(
          artifact_id uuid PRIMARY KEY,source_plan_id uuid NOT NULL UNIQUE,environment_id uuid NOT NULL,operator_tenant uuid NOT NULL,
          target_tenant uuid NOT NULL,subject_id uuid NOT NULL,policy_id uuid NOT NULL,scope_json text NOT NULL,receipt_json text NOT NULL,
          installed_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
        DO $$ DECLARE n text;BEGIN
          FOREACH n IN ARRAY ARRAY['policies','plans','events','executions','tombstones'] LOOP
            EXECUTE format('CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_export_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.gate()',n);
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_export_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.immutable()',n);
          END LOOP;
          FOREACH n IN ARRAY ARRAY['policies','tombstones'] LOOP
            EXECUTE format('CREATE TRIGGER restore_source_revision AFTER INSERT ON cp6_privacy_export_disposition_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.advance_restore_epoch()',n);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_privacy_export_disposition_v1.live_origin() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p cp6_privacy_export_disposition_v1.plans;e cp6_privacy_export_disposition_v1.events;s jsonb;r jsonb;v uuid;l bigint;BEGIN
          SELECT * INTO p FROM cp6_privacy_export_disposition_v1.plans WHERE id=NEW.plan_id;
          SELECT * INTO e FROM cp6_privacy_export_disposition_v1.events WHERE plan_id=NEW.plan_id ORDER BY sequence DESC LIMIT 1;
          SELECT revision,octet_length(bytes)::bigint INTO v,l FROM cp6_privacy_export_v1.payloads WHERE artifact_id=NEW.artifact_id;
          s=NEW.scope_json::jsonb;r=NEW.receipt_json::jsonb;
          IF p.id IS NULL OR v IS NULL OR NEW.writer_xid<>pg_current_xact_id() OR NEW.scope_json::jsonb IS DISTINCT FROM p.scope_json::jsonb
            OR (e.state,e.version) IS DISTINCT FROM ('APPROVED'::text,NEW.approval_version) OR e.actor_id=p.requester_id
            OR (NEW.environment_id,NEW.actor_tenant,NEW.artifact_id,v,l)
              IS DISTINCT FROM ((s->>'EnvironmentId')::uuid,(s->>'OperatorTenantId')::uuid,(s->>'ArtifactId')::uuid,(s->>'PayloadVersion')::uuid,(s->>'PayloadLength')::bigint)
            OR (r->>'Owner',r->>'OwnerOutcome',r->>'ScopeDigest',(r->>'PlanId')::uuid,(r->>'OperationId')::uuid,(r->>'OperatorId')::uuid,
                (r->>'RequesterId')::uuid,(r->>'ApproverId')::uuid,(r->>'ArtifactId')::uuid,(r->>'BeforeVersion')::uuid,(r->>'RemovedBytes')::bigint)
              IS DISTINCT FROM ('Main.Privacy.ExportPayload.v1','APPLIED',p.scope_digest,NEW.plan_id,NEW.operation_id,NEW.operator_id,p.requester_id,e.actor_id,NEW.artifact_id,v,l)
            THEN RAISE EXCEPTION 'PRIVACY_EXPORT_PAYLOAD_APPROVED_ORIGIN_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER live_origin BEFORE INSERT ON cp6_privacy_export_disposition_v1.executions FOR EACH ROW EXECUTE FUNCTION cp6_privacy_export_disposition_v1.live_origin();
        CREATE FUNCTION cp6_privacy_export_disposition_v1.restore_owns(artifact uuid,source_plan uuid) RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT EXISTS(SELECT FROM cp6_privacy_disposition_v1.restore_receipts r,
            jsonb_array_elements(COALESCE(NULLIF(r.result_json::jsonb->'Units','null'::jsonb),'[]'::jsonb)) u
            WHERE r.writer_xid=pg_current_xact_id() AND u->>'Owner'='Main.Privacy.ExportPayload.v1'
              AND (u->>'ArtifactId')::uuid=artifact AND (u->>'SourcePlanId')::uuid=source_plan);
        $$;
        CREATE FUNCTION cp6_privacy_export_disposition_v1.tomb_origin() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE a cp6_privacy_export_v1.artifacts;s jsonb;BEGIN
          s=NEW.scope_json::jsonb;SELECT * INTO a FROM cp6_privacy_export_v1.artifacts WHERE id=NEW.artifact_id;
          IF NEW.installed_xid<>pg_current_xact_id()
            OR (NEW.environment_id,NEW.operator_tenant,NEW.artifact_id,NEW.target_tenant,NEW.subject_id,NEW.policy_id)
              IS DISTINCT FROM ((s->>'EnvironmentId')::uuid,(s->>'OperatorTenantId')::uuid,(s->>'ArtifactId')::uuid,(s->>'TargetTenantId')::uuid,(s->>'SubjectId')::uuid,(s->>'PolicyId')::uuid)
            OR a.id IS NOT NULL AND ((a.environment_id,a.actor_tenant,a.target_tenant,a.subject_id)
              IS DISTINCT FROM (NEW.environment_id,NEW.operator_tenant,NEW.target_tenant,NEW.subject_id)
              OR convert_from(a.receipt_bytes,'UTF8')::jsonb->>'Sha256' IS DISTINCT FROM s->>'OriginalSha256')
            OR NOT EXISTS(SELECT FROM cp6_privacy_export_disposition_v1.executions x WHERE x.plan_id=NEW.source_plan_id AND x.artifact_id=NEW.artifact_id
              AND x.writer_xid=pg_current_xact_id() AND x.scope_json::jsonb=s AND x.receipt_json::jsonb=NEW.receipt_json::jsonb)
              AND NOT cp6_privacy_export_disposition_v1.restore_owns(NEW.artifact_id,NEW.source_plan_id)
            THEN RAISE EXCEPTION 'PRIVACY_EXPORT_PAYLOAD_TOMBSTONE_ORIGIN_REQUIRED' USING ERRCODE='23514';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER tomb_origin BEFORE INSERT ON cp6_privacy_export_disposition_v1.tombstones FOR EACH ROW EXECUTE FUNCTION cp6_privacy_export_disposition_v1.tomb_origin();
        CREATE FUNCTION cp6_privacy_export_disposition_v1.no_resurrection() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_privacy_export_disposition_v1.tombstones WHERE artifact_id=NEW.artifact_id)
            THEN RAISE EXCEPTION 'PRIVACY_EXPORT_PAYLOAD_DISPOSED_IDENTITY' USING ERRCODE='23514';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER payload_no_resurrection BEFORE INSERT OR UPDATE ON cp6_privacy_export_v1.payloads FOR EACH ROW EXECUTE FUNCTION cp6_privacy_export_disposition_v1.no_resurrection();
        CREATE FUNCTION cp6_privacy_export_disposition_v1.owned_delete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN RAISE EXCEPTION 'PRIVACY_EXPORT_PAYLOAD_DISPOSITION_REQUIRED' USING ERRCODE='23514';END IF;
          IF NOT EXISTS(SELECT FROM cp6_privacy_export_disposition_v1.tombstones t WHERE t.artifact_id=OLD.artifact_id AND
              (EXISTS(SELECT FROM cp6_privacy_export_disposition_v1.executions x WHERE x.plan_id=t.source_plan_id AND x.artifact_id=t.artifact_id
                  AND x.writer_xid=pg_current_xact_id() AND (x.scope_json::jsonb->>'PayloadVersion')::uuid=OLD.revision)
               OR cp6_privacy_export_disposition_v1.restore_owns(t.artifact_id,t.source_plan_id)))
            THEN RAISE EXCEPTION 'PRIVACY_EXPORT_PAYLOAD_DISPOSITION_REQUIRED' USING ERRCODE='23514';END IF;RETURN OLD;
        END $$;
        CREATE TRIGGER payload_owned_delete BEFORE DELETE ON cp6_privacy_export_v1.payloads FOR EACH ROW EXECUTE FUNCTION cp6_privacy_export_disposition_v1.owned_delete();
        CREATE TRIGGER payload_no_truncate BEFORE TRUNCATE ON cp6_privacy_export_v1.payloads FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_export_disposition_v1.owned_delete();
        CREATE FUNCTION cp6_privacy_export_disposition_v1.live_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_privacy_export_disposition_v1.tombstones t WHERE t.artifact_id=NEW.artifact_id AND t.source_plan_id=NEW.plan_id AND t.receipt_json::jsonb=NEW.receipt_json::jsonb)
            OR EXISTS(SELECT FROM cp6_privacy_export_v1.payloads WHERE artifact_id=NEW.artifact_id)
            OR EXISTS(SELECT FROM cp6_privacy_export_v1.artifacts WHERE id=NEW.artifact_id AND revoked_at IS NULL)
            OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.actor_tenant AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.operator_id
              AND o.owner='Main.Privacy.ExportPayload.v1/'||NEW.environment_id::text||'/'||NEW.operator_id::text AND o.state='COMMITTED'
              AND convert_from(o.result_bytes,'UTF8')::jsonb->'Execution'=NEW.receipt_json::jsonb)
            THEN RAISE EXCEPTION 'PRIVACY_EXPORT_PAYLOAD_EFFECT_AND_RECEIPT_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER live_complete AFTER INSERT ON cp6_privacy_export_disposition_v1.executions DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION cp6_privacy_export_disposition_v1.live_complete();
        CREATE FUNCTION cp6_privacy_export_disposition_v1.restore_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE u jsonb;BEGIN
          FOR u IN SELECT value FROM jsonb_array_elements(COALESCE(NULLIF(NEW.result_json::jsonb->'Units','null'::jsonb),'[]'::jsonb)) LOOP
            IF u->>'Owner'='Main.Privacy.ExportPayload.v1' AND
              (NOT EXISTS(SELECT FROM cp6_privacy_export_disposition_v1.tombstones t WHERE t.artifact_id=(u->>'ArtifactId')::uuid AND t.source_plan_id=(u->>'SourcePlanId')::uuid)
               OR EXISTS(SELECT FROM cp6_privacy_export_v1.payloads WHERE artifact_id=(u->>'ArtifactId')::uuid)
               OR EXISTS(SELECT FROM cp6_privacy_export_v1.artifacts WHERE id=(u->>'ArtifactId')::uuid AND revoked_at IS NULL))
              THEN RAISE EXCEPTION 'PRIVACY_EXPORT_PAYLOAD_RESTORE_EFFECT_REQUIRED' USING ERRCODE='23514';END IF;
          END LOOP;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER payload_restore_complete AFTER INSERT ON cp6_privacy_disposition_v1.restore_receipts DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION cp6_privacy_export_disposition_v1.restore_complete();
        CREATE FUNCTION cp6_privacy_export_disposition_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('cp6_privacy_export_v1.payloads','authority_gate','cp6_privacy_export_v1.gate()',62,false),
            ('cp6_privacy_export_v1.payloads','payload_no_rewrite','cp6_privacy_export_v1.immutable()',18,false),
            ('cp6_privacy_export_v1.payloads','payload_no_resurrection','cp6_privacy_export_disposition_v1.no_resurrection()',23,false),
            ('cp6_privacy_export_v1.payloads','payload_owned_delete','cp6_privacy_export_disposition_v1.owned_delete()',11,false),
            ('cp6_privacy_export_v1.payloads','payload_no_truncate','cp6_privacy_export_disposition_v1.owned_delete()',34,false),
            ('cp6_privacy_export_v1.artifacts','authority_gate','cp6_privacy_export_v1.gate()',62,false),
            ('cp6_privacy_export_v1.artifacts','artifact_change','cp6_privacy_export_v1.artifact_change()',27,false),
            ('cp6_privacy_export_v1.artifacts','artifact_no_truncate','cp6_privacy_export_v1.immutable()',34,false),
            ('cp6_privacy_export_disposition_v1.policies','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_export_disposition_v1.policies','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_export_disposition_v1.policies','restore_source_revision','cp6_privacy_disposition_v1.advance_restore_epoch()',4,false),
            ('cp6_privacy_export_disposition_v1.plans','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_export_disposition_v1.plans','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_export_disposition_v1.events','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_export_disposition_v1.events','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_export_disposition_v1.executions','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_export_disposition_v1.executions','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_export_disposition_v1.tombstones','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_export_disposition_v1.tombstones','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_export_disposition_v1.tombstones','restore_source_revision','cp6_privacy_disposition_v1.advance_restore_epoch()',4,false),
            ('cp6_privacy_disposition_v1.restore_receipts','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_disposition_v1.restore_receipts','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_export_disposition_v1.executions','live_origin','cp6_privacy_export_disposition_v1.live_origin()',7,false),
            ('cp6_privacy_export_disposition_v1.executions','live_complete','cp6_privacy_export_disposition_v1.live_complete()',5,true),
            ('cp6_privacy_export_disposition_v1.tombstones','tomb_origin','cp6_privacy_export_disposition_v1.tomb_origin()',7,false),
            ('cp6_privacy_disposition_v1.restore_receipts','payload_restore_complete','cp6_privacy_export_disposition_v1.restore_complete()',5,true)) a(tab,name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tab) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
              AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgattr=''::int2vector AND t.tgqual IS NULL AND t.tgnargs=0
              AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        REVOKE ALL ON SCHEMA cp6_privacy_export_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_privacy_export_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cp6_privacy_export_disposition_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_privacy_export_disposition_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.action,v.name,0,t."Id",LOCALTIMESTAMP
        FROM (VALUES('payload-read','读取导出载荷处置'),('payload-policy','配置载荷保留政策'),('payload-prepare','申请导出载荷处置'),
          ('payload-approve','独立批准导出载荷处置'),('payload-revoke','撤销载荷处置批准'),('payload-execute','实际处置导出载荷'))v(action,name)
        JOIN public."Sys_Menus" m ON m."MenuKey"='privacy-disposition' CROSS JOIN public."Sys_Tenants" t
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.action);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Disposed payload origins and resurrection fences require forward recovery.");
}
