using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261108160000_SecurityFieldDispositionV1")]
public sealed class SecurityFieldDispositionV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_privacy_security_fields_v1;
        ALTER TABLE public."Sys_SecurityLogs" ADD COLUMN privacy_revision uuid NOT NULL DEFAULT gen_random_uuid();
        CREATE TABLE cp6_privacy_security_fields_v1.policies(
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,policy_id uuid NOT NULL,
          version uuid UNIQUE NOT NULL,target_tenant uuid NOT NULL,basis_ref uuid NOT NULL,enabled boolean NOT NULL,not_before timestamptz NOT NULL,owner_id uuid NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE INDEX security_policy_current ON cp6_privacy_security_fields_v1.policies(environment_id,actor_tenant,policy_id,sequence DESC);
        CREATE TABLE cp6_privacy_security_fields_v1.plans(
          id uuid PRIMARY KEY,environment_id uuid NOT NULL,actor_tenant uuid NOT NULL,request_ref uuid NOT NULL,requester_id uuid NOT NULL,scope_json text NOT NULL,scope_digest text NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE TABLE cp6_privacy_security_fields_v1.events(
          sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,plan_id uuid NOT NULL REFERENCES cp6_privacy_security_fields_v1.plans,version uuid UNIQUE NOT NULL,
          previous_version uuid,state text NOT NULL CHECK(state IN('REQUESTED','APPROVED','REJECTED','REVOKED')),actor_id uuid NOT NULL,decision_ref uuid,operation_id uuid NOT NULL,at timestamptz NOT NULL DEFAULT clock_timestamp(),UNIQUE(plan_id,previous_version));
        CREATE TABLE cp6_privacy_security_fields_v1.executions(
          plan_id uuid PRIMARY KEY REFERENCES cp6_privacy_security_fields_v1.plans,approval_version uuid NOT NULL,log_id uuid NOT NULL UNIQUE,environment_id uuid NOT NULL,
          actor_tenant uuid NOT NULL,operator_id uuid NOT NULL,operation_id uuid NOT NULL,scope_json text NOT NULL,receipt_json text NOT NULL,writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
        CREATE TABLE cp6_privacy_security_fields_v1.tombstones(
          log_id uuid PRIMARY KEY,source_plan_id uuid NOT NULL UNIQUE,environment_id uuid NOT NULL,operator_tenant uuid NOT NULL,target_tenant uuid NOT NULL,
          subject_id uuid NOT NULL,policy_id uuid NOT NULL,scope_json text NOT NULL,receipt_json text NOT NULL,installed_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
        DO $$ DECLARE n text;BEGIN
          FOREACH n IN ARRAY ARRAY['policies','plans','events','executions','tombstones'] LOOP
            EXECUTE format('CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_security_fields_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.gate()',n);
            EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_privacy_security_fields_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.immutable()',n);
          END LOOP;
          FOREACH n IN ARRAY ARRAY['policies','tombstones'] LOOP
            EXECUTE format('CREATE TRIGGER restore_source_revision AFTER INSERT ON cp6_privacy_security_fields_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.advance_restore_epoch()',n);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_privacy_security_fields_v1.field_digest(name text,ip text) RETURNS text LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
          SELECT encode(sha256(convert_to(jsonb_build_array(name,ip)::text,'UTF8')),'hex');
        $$;
        CREATE FUNCTION cp6_privacy_security_fields_v1.restore_after(log uuid) RETURNS uuid LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT (u->>'AfterVersion')::uuid FROM cp6_privacy_disposition_v1.restore_receipts r,
            jsonb_array_elements(COALESCE(NULLIF(r.result_json::jsonb->'Units','null'::jsonb),'[]'::jsonb)) u
            WHERE r.writer_xid=pg_current_xact_id() AND u->>'Owner'='Main.SecurityLog.Fields.v1' AND (u->>'LogId')::uuid=log LIMIT 1;
        $$;
        CREATE FUNCTION cp6_privacy_security_fields_v1.restore_owns(log uuid,source_plan uuid) RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT EXISTS(SELECT FROM cp6_privacy_disposition_v1.restore_receipts r,
            jsonb_array_elements(COALESCE(NULLIF(r.result_json::jsonb->'Units','null'::jsonb),'[]'::jsonb)) u
            WHERE r.writer_xid=pg_current_xact_id() AND u->>'Owner'='Main.SecurityLog.Fields.v1' AND (u->>'LogId')::uuid=log AND (u->>'SourcePlanId')::uuid=source_plan);
        $$;
        CREATE FUNCTION cp6_privacy_security_fields_v1.native_row() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE t cp6_privacy_security_fields_v1.tombstones;s jsonb;after_id uuid;BEGIN
          SELECT * INTO t FROM cp6_privacy_security_fields_v1.tombstones WHERE log_id=NEW."Id" OR TG_OP='UPDATE' AND log_id=OLD."Id" LIMIT 1;
          IF t.log_id IS NOT NULL THEN
            s=t.scope_json::jsonb;
            IF (NEW."Id",NEW."TenantId",NEW."UserId",NEW."EventType",NEW."CreatedAt")
                IS DISTINCT FROM (t.log_id,t.target_tenant,t.subject_id,(s->>'EventType')::integer,(s->>'OriginalCreatedAt')::timestamp)
              OR NEW."UserName" IS NOT NULL OR NEW."ClientIp" IS NOT NULL
              THEN RAISE EXCEPTION 'PRIVACY_SECURITY_FIELDS_DISPOSED_IDENTITY' USING ERRCODE='23514';END IF;
          END IF;
          IF TG_OP='UPDATE' THEN
            SELECT (receipt_json::jsonb->>'AfterVersion')::uuid INTO after_id FROM cp6_privacy_security_fields_v1.executions WHERE log_id=OLD."Id" AND writer_xid=pg_current_xact_id();
            IF after_id IS NULL THEN after_id=cp6_privacy_security_fields_v1.restore_after(OLD."Id");END IF;
            IF after_id IS NULL AND EXISTS(SELECT FROM (VALUES(OLD."TenantId",OLD."UserId"),(NEW."TenantId",NEW."UserId"))v(tenant,subject)
              JOIN LATERAL(SELECT active FROM cp6_privacy_disposition_v1.holds
                WHERE environment_id=(SELECT environment_id FROM cp6_scope_authorization_v1.environment WHERE singleton)
                  AND target_tenant=v.tenant AND subject_id=v.subject ORDER BY sequence DESC LIMIT 1)h ON h.active)
              THEN RAISE EXCEPTION 'PRIVACY_SECURITY_FIELDS_ACTIVE_HOLD' USING ERRCODE='23514';END IF;
            IF (OLD."UserName" IS NOT NULL AND NEW."UserName" IS NULL OR OLD."ClientIp" IS NOT NULL AND NEW."ClientIp" IS NULL) AND after_id IS NULL
              THEN RAISE EXCEPTION 'PRIVACY_SECURITY_FIELDS_DISPOSITION_REQUIRED' USING ERRCODE='23514';END IF;
            IF after_id IS NOT NULL THEN
              IF NEW."UserName" IS NOT NULL OR NEW."ClientIp" IS NOT NULL OR NEW.privacy_revision<>after_id
                THEN RAISE EXCEPTION 'PRIVACY_SECURITY_FIELDS_EXACT_EFFECT_REQUIRED' USING ERRCODE='23514';END IF;
            ELSE NEW.privacy_revision=gen_random_uuid();END IF;
          END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER privacy_fields_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Sys_SecurityLogs" FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_disposition_v1.gate();
        CREATE TRIGGER privacy_fields_row BEFORE INSERT OR UPDATE ON public."Sys_SecurityLogs" FOR EACH ROW EXECUTE FUNCTION cp6_privacy_security_fields_v1.native_row();
        CREATE FUNCTION cp6_privacy_security_fields_v1.delete_hold() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE held boolean;env uuid;BEGIN
          SELECT environment_id INTO env FROM cp6_scope_authorization_v1.environment WHERE singleton;
          IF TG_OP='TRUNCATE' THEN
            SELECT EXISTS(SELECT FROM public."Sys_SecurityLogs" l JOIN LATERAL(
              SELECT active FROM cp6_privacy_disposition_v1.holds WHERE environment_id=env AND target_tenant=l."TenantId" AND subject_id=l."UserId" ORDER BY sequence DESC LIMIT 1)h ON h.active) INTO held;
          ELSE
            SELECT active INTO held FROM cp6_privacy_disposition_v1.holds WHERE environment_id=env AND target_tenant=OLD."TenantId" AND subject_id=OLD."UserId" ORDER BY sequence DESC LIMIT 1;
          END IF;
          IF held THEN RAISE EXCEPTION 'PRIVACY_SECURITY_FIELDS_ACTIVE_HOLD' USING ERRCODE='23514';END IF;RETURN OLD;
        END $$;
        CREATE TRIGGER privacy_fields_delete_hold BEFORE DELETE ON public."Sys_SecurityLogs" FOR EACH ROW EXECUTE FUNCTION cp6_privacy_security_fields_v1.delete_hold();
        CREATE TRIGGER privacy_fields_truncate_hold BEFORE TRUNCATE ON public."Sys_SecurityLogs" FOR EACH STATEMENT EXECUTE FUNCTION cp6_privacy_security_fields_v1.delete_hold();
        CREATE FUNCTION cp6_privacy_security_fields_v1.live_origin() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p cp6_privacy_security_fields_v1.plans;e cp6_privacy_security_fields_v1.events;l public."Sys_SecurityLogs";s jsonb;r jsonb;BEGIN
          SELECT * INTO p FROM cp6_privacy_security_fields_v1.plans WHERE id=NEW.plan_id;
          SELECT * INTO e FROM cp6_privacy_security_fields_v1.events WHERE plan_id=NEW.plan_id ORDER BY sequence DESC LIMIT 1;
          SELECT * INTO l FROM public."Sys_SecurityLogs" WHERE "Id"=NEW.log_id;s=NEW.scope_json::jsonb;r=NEW.receipt_json::jsonb;
          IF p.id IS NULL OR l."Id" IS NULL OR l."UserName" IS NULL AND l."ClientIp" IS NULL OR NEW.writer_xid<>pg_current_xact_id()
            OR NEW.scope_json::jsonb IS DISTINCT FROM p.scope_json::jsonb OR (e.state,e.version) IS DISTINCT FROM ('APPROVED'::text,NEW.approval_version) OR e.actor_id=p.requester_id
            OR (NEW.environment_id,NEW.actor_tenant,NEW.log_id,l."TenantId",l."UserId",l.privacy_revision)
              IS DISTINCT FROM ((s->>'EnvironmentId')::uuid,(s->>'OperatorTenantId')::uuid,(s->>'LogId')::uuid,(s->>'TargetTenantId')::uuid,(s->>'SubjectId')::uuid,(s->>'TargetVersion')::uuid)
            OR (r->>'Owner',r->>'OwnerOutcome',r->>'ScopeDigest',(r->>'PlanId')::uuid,(r->>'OperationId')::uuid,(r->>'OperatorId')::uuid,(r->>'RequesterId')::uuid,(r->>'ApproverId')::uuid,
                (r->>'LogId')::uuid,(r->>'BeforeVersion')::uuid,(r->>'ClearedValueCount')::integer)
              IS DISTINCT FROM ('Main.SecurityLog.Fields.v1','APPLIED',p.scope_digest,NEW.plan_id,NEW.operation_id,NEW.operator_id,p.requester_id,e.actor_id,NEW.log_id,l.privacy_revision,(l."UserName" IS NOT NULL)::integer+(l."ClientIp" IS NOT NULL)::integer)
            OR (r->>'AfterVersion')::uuid IS NULL THEN RAISE EXCEPTION 'PRIVACY_SECURITY_FIELDS_APPROVED_ORIGIN_REQUIRED' USING ERRCODE='23514';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER live_origin BEFORE INSERT ON cp6_privacy_security_fields_v1.executions FOR EACH ROW EXECUTE FUNCTION cp6_privacy_security_fields_v1.live_origin();
        CREATE FUNCTION cp6_privacy_security_fields_v1.tomb_origin() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE s jsonb;BEGIN
          s=NEW.scope_json::jsonb;
          IF NEW.installed_xid<>pg_current_xact_id() OR (NEW.environment_id,NEW.operator_tenant,NEW.log_id,NEW.target_tenant,NEW.subject_id,NEW.policy_id)
              IS DISTINCT FROM ((s->>'EnvironmentId')::uuid,(s->>'OperatorTenantId')::uuid,(s->>'LogId')::uuid,(s->>'TargetTenantId')::uuid,(s->>'SubjectId')::uuid,(s->>'PolicyId')::uuid)
            OR NOT EXISTS(SELECT FROM cp6_privacy_security_fields_v1.executions x WHERE x.plan_id=NEW.source_plan_id AND x.log_id=NEW.log_id AND x.writer_xid=pg_current_xact_id()
              AND x.scope_json::jsonb=s AND x.receipt_json::jsonb=NEW.receipt_json::jsonb) AND NOT cp6_privacy_security_fields_v1.restore_owns(NEW.log_id,NEW.source_plan_id)
            THEN RAISE EXCEPTION 'PRIVACY_SECURITY_FIELDS_TOMBSTONE_ORIGIN_REQUIRED' USING ERRCODE='23514';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER tomb_origin BEFORE INSERT ON cp6_privacy_security_fields_v1.tombstones FOR EACH ROW EXECUTE FUNCTION cp6_privacy_security_fields_v1.tomb_origin();
        CREATE FUNCTION cp6_privacy_security_fields_v1.live_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_privacy_security_fields_v1.tombstones WHERE log_id=NEW.log_id AND source_plan_id=NEW.plan_id AND receipt_json::jsonb=NEW.receipt_json::jsonb)
            OR NOT EXISTS(SELECT FROM public."Sys_SecurityLogs" l WHERE l."Id"=NEW.log_id AND l."UserName" IS NULL AND l."ClientIp" IS NULL AND l.privacy_revision=(NEW.receipt_json::jsonb->>'AfterVersion')::uuid)
            OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.actor_tenant AND o.actor_id=NEW.operator_id AND o.operation_id=NEW.operation_id
              AND o.owner='Main.SecurityLog.Fields.v1/'||NEW.environment_id::text||'/'||NEW.operator_id::text AND o.state='COMMITTED' AND convert_from(o.result_bytes,'UTF8')::jsonb->'Execution'=NEW.receipt_json::jsonb)
            THEN RAISE EXCEPTION 'PRIVACY_SECURITY_FIELDS_EFFECT_AND_RECEIPT_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER live_complete AFTER INSERT ON cp6_privacy_security_fields_v1.executions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_privacy_security_fields_v1.live_complete();
        CREATE FUNCTION cp6_privacy_security_fields_v1.restore_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE u jsonb;BEGIN
          FOR u IN SELECT value FROM jsonb_array_elements(COALESCE(NULLIF(NEW.result_json::jsonb->'Units','null'::jsonb),'[]'::jsonb)) LOOP
            IF u->>'Owner'='Main.SecurityLog.Fields.v1' AND
              (NOT EXISTS(SELECT FROM cp6_privacy_security_fields_v1.tombstones t WHERE t.log_id=(u->>'LogId')::uuid AND t.source_plan_id=(u->>'SourcePlanId')::uuid)
               OR EXISTS(SELECT FROM public."Sys_SecurityLogs" l WHERE l."Id"=(u->>'LogId')::uuid AND (l."UserName" IS NOT NULL OR l."ClientIp" IS NOT NULL OR l.privacy_revision IS DISTINCT FROM (u->>'AfterVersion')::uuid)))
              THEN RAISE EXCEPTION 'PRIVACY_SECURITY_FIELDS_RESTORE_EFFECT_REQUIRED' USING ERRCODE='23514';END IF;
          END LOOP;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER security_fields_restore_complete AFTER INSERT ON cp6_privacy_disposition_v1.restore_receipts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_privacy_security_fields_v1.restore_complete();
        CREATE FUNCTION cp6_privacy_security_fields_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('public."Sys_SecurityLogs"','privacy_fields_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('public."Sys_SecurityLogs"','privacy_fields_row','cp6_privacy_security_fields_v1.native_row()',23,false),
            ('public."Sys_SecurityLogs"','privacy_fields_delete_hold','cp6_privacy_security_fields_v1.delete_hold()',11,false),
            ('public."Sys_SecurityLogs"','privacy_fields_truncate_hold','cp6_privacy_security_fields_v1.delete_hold()',34,false),
            ('cp6_privacy_security_fields_v1.policies','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_security_fields_v1.policies','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_security_fields_v1.policies','restore_source_revision','cp6_privacy_disposition_v1.advance_restore_epoch()',4,false),
            ('cp6_privacy_security_fields_v1.plans','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_security_fields_v1.plans','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_security_fields_v1.events','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_security_fields_v1.events','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_security_fields_v1.executions','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_security_fields_v1.executions','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_security_fields_v1.tombstones','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_security_fields_v1.tombstones','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_security_fields_v1.tombstones','restore_source_revision','cp6_privacy_disposition_v1.advance_restore_epoch()',4,false),
            ('cp6_privacy_disposition_v1.restore_receipts','authority_gate','cp6_privacy_disposition_v1.gate()',62,false),
            ('cp6_privacy_disposition_v1.restore_receipts','immutable','cp6_privacy_disposition_v1.immutable()',58,false),
            ('cp6_privacy_security_fields_v1.executions','live_origin','cp6_privacy_security_fields_v1.live_origin()',7,false),
            ('cp6_privacy_security_fields_v1.executions','live_complete','cp6_privacy_security_fields_v1.live_complete()',5,true),
            ('cp6_privacy_security_fields_v1.tombstones','tomb_origin','cp6_privacy_security_fields_v1.tomb_origin()',7,false),
            ('cp6_privacy_disposition_v1.restore_receipts','security_fields_restore_complete','cp6_privacy_security_fields_v1.restore_complete()',5,true)) a(tab,name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tab) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
              AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgattr=''::int2vector AND t.tgqual IS NULL AND t.tgnargs=0 AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        REVOKE ALL ON SCHEMA cp6_privacy_security_fields_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_privacy_security_fields_v1 FROM PUBLIC;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA cp6_privacy_security_fields_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_privacy_security_fields_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.action,v.name,0,t."Id",LOCALTIMESTAMP FROM
          (VALUES('security-read','读取安全事件字段处置'),('security-policy','配置安全字段政策'),('security-prepare','申请安全字段处置'),('security-approve','独立批准安全字段处置'),('security-revoke','撤销安全字段计划'),('security-execute','执行安全字段清理'))v(action,name)
          JOIN public."Sys_Menus" m ON m."MenuKey"='privacy-disposition' CROSS JOIN public."Sys_Tenants" t
          WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.action);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Security field tombstones require forward recovery.");
}
