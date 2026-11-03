using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261103045000_PendingApproverResolutionsV2")]
public sealed class PendingApproverResolutionsV2:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_native_tasks_v2;
        REVOKE ALL ON SCHEMA cp6_native_tasks_v2 FROM PUBLIC;
        ALTER TABLE cp6_approval_start_v2.starts ADD COLUMN writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id();
        CREATE TABLE cp6_native_tasks_v2.pending_resolutions(
          id uuid PRIMARY KEY,tenant_id uuid NOT NULL,environment_id uuid NOT NULL,root_id uuid NOT NULL,
          instance_id uuid NOT NULL REFERENCES public."Wf_FlowInstance"("Id"),execution_cycle_id uuid NOT NULL,
          token_id uuid NOT NULL REFERENCES public."Wf_FlowToken"("Id"),node_id text COLLATE "C" NOT NULL,
          stage_index integer NOT NULL,stage_round integer NOT NULL,flow_version uuid NOT NULL,route_policy_ref text COLLATE "C" NOT NULL,
          slot_json text NOT NULL,source_kind text NOT NULL CHECK(source_kind IN('SFS_COMMAND','APP_START','APP_COMMAND')),source_command uuid NOT NULL,
          instance_version bytea NOT NULL CHECK(octet_length(instance_version)=8),state text NOT NULL DEFAULT 'PENDING' CHECK(state IN('PENDING','RESOLVED','CANCELLED')),
          row_version uuid NOT NULL DEFAULT gen_random_uuid(),resolution_kind text,resolution_command uuid,assignment_evidence text,
          created_at timestamptz NOT NULL DEFAULT clock_timestamp(),writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),
          UNIQUE(instance_id,execution_cycle_id,node_id,token_id,stage_index,stage_round),
          CHECK((state='PENDING' AND resolution_kind IS NULL AND resolution_command IS NULL AND assignment_evidence IS NULL)
            OR(state='RESOLVED' AND resolution_kind IS NOT NULL AND resolution_command IS NOT NULL AND assignment_evidence IS NOT NULL)
            OR(state='CANCELLED' AND resolution_kind IS NOT NULL AND resolution_command IS NOT NULL AND assignment_evidence IS NULL)));
        CREATE INDEX pending_resolutions_scope ON cp6_native_tasks_v2.pending_resolutions(tenant_id,environment_id,root_id,state);
        CREATE FUNCTION cp6_native_tasks_v2.command_matches(k text,c uuid,t uuid,e uuid,r uuid) RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT CASE k
            WHEN 'SFS_COMMAND' THEN EXISTS(SELECT FROM cp6_personal_form_v2.commands x WHERE x.id=c AND x.tenant_id=t AND x.environment_id=e AND x.root_id=r AND x.writer_xid=pg_current_xact_id() AND NOT x.closed)
            WHEN 'APP_START' THEN EXISTS(SELECT FROM cp6_approval_start_v2.starts x WHERE x.engine_start_id=c AND x.tenant_id=t AND x.environment_id=e AND x.instance_id=r AND x.writer_xid=pg_current_xact_id())
            WHEN 'APP_COMMAND' THEN EXISTS(SELECT FROM cp6_approval_decision_v2.commands x JOIN cp6_approval_runtime_v2.instances i ON i.instance_id=x.instance_id
              WHERE x.id=c AND x.tenant_id=t AND x.environment_id=e AND i.root_instance_id=r AND x.writer_xid=pg_current_xact_id())
            ELSE false END;
        $$;
        CREATE FUNCTION cp6_native_tasks_v2.pending_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN NEW.writer_xid=pg_current_xact_id();
          ELSE
            IF OLD.state<>'PENDING' OR NEW.state='PENDING'
              OR (to_jsonb(OLD)-ARRAY['state','row_version','resolution_kind','resolution_command','assignment_evidence']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['state','row_version','resolution_kind','resolution_command','assignment_evidence'])
              OR NOT cp6_native_tasks_v2.command_matches(NEW.resolution_kind,NEW.resolution_command,NEW.tenant_id,NEW.environment_id,NEW.root_id)
              THEN RAISE EXCEPTION 'PENDING_RESOLUTION_COMMAND_REQUIRED' USING ERRCODE='23514';END IF;
          END IF;
          NEW.row_version=gen_random_uuid();RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_native_tasks_v2.pending_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p cp6_native_tasks_v2.pending_resolutions;i public."Wf_FlowInstance";k public."Wf_FlowToken";a jsonb;BEGIN
          SELECT * INTO p FROM cp6_native_tasks_v2.pending_resolutions WHERE id=NEW.id;
          SELECT * INTO i FROM public."Wf_FlowInstance" WHERE "Id"=p.instance_id;
          SELECT * INTO k FROM public."Wf_FlowToken" WHERE "Id"=p.token_id;
          IF i."Id" IS NULL OR k."Id" IS NULL OR (i."TenantId",i."FlowDefVersionId",k."InstanceId",k."TenantId") IS DISTINCT FROM (p.tenant_id,p.flow_version,p.instance_id,p.tenant_id)
            OR TG_OP='INSERT' AND (NOT cp6_native_tasks_v2.command_matches(p.source_kind,p.source_command,p.tenant_id,p.environment_id,p.root_id)
              OR p.writer_xid<>pg_current_xact_id() OR p.state='PENDING' AND (i."Status"<>4 OR k."Status"<>0 OR (k."NodeId"::text COLLATE "C")<>p.node_id))
            THEN RAISE EXCEPTION 'PENDING_RESOLUTION_SLOT_REQUIRED' USING ERRCODE='23514';END IF;
          IF p.state='CANCELLED' AND k."Status"=0 THEN RAISE EXCEPTION 'PENDING_SLOT_STILL_ACTIVE' USING ERRCODE='23514';END IF;
          IF p.state='RESOLVED' THEN
            a=p.assignment_evidence::jsonb;
            IF jsonb_array_length(a->'targets') NOT BETWEEN 1 AND 100 OR jsonb_array_length(a->'tasks')<>jsonb_array_length(a->'targets')
              OR a->>'routePolicyRef' IS DISTINCT FROM p.route_policy_ref
              OR p.slot_json::jsonb#>>'{policy,mode}' IS DISTINCT FROM 'MANUAL_ASSIGNMENT'
              OR EXISTS(SELECT FROM jsonb_array_elements_text(a->'tasks') task WHERE NOT EXISTS(SELECT FROM public."Wf_FlowTask" t
                WHERE t."Id"=task::uuid AND t."TenantId"=p.tenant_id AND t."InstanceId"=p.instance_id AND (t."NodeId"::text COLLATE "C")=p.node_id
                  AND t."TokenId"=p.token_id AND t."StageIndex"=p.stage_index AND t."StageRound"=p.stage_round
                  AND (a->'targets') ? t."AssigneeId"::text AND t."Status"=0))
              THEN RAISE EXCEPTION 'PENDING_ASSIGNMENT_ATOMIC_REQUIRED' USING ERRCODE='23514';END IF;
          END IF;RETURN NULL;
        END $$;
        CREATE TRIGGER pending_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_native_tasks_v2.pending_resolutions FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER pending_no_delete BEFORE DELETE OR TRUNCATE ON cp6_native_tasks_v2.pending_resolutions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER pending_guard BEFORE INSERT OR UPDATE ON cp6_native_tasks_v2.pending_resolutions FOR EACH ROW EXECUTE FUNCTION cp6_native_tasks_v2.pending_guard();
        CREATE CONSTRAINT TRIGGER pending_complete AFTER INSERT OR UPDATE ON cp6_native_tasks_v2.pending_resolutions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_native_tasks_v2.pending_complete();
        CREATE FUNCTION cp6_native_tasks_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT current_setting('session_replication_role')='origin' AND NOT EXISTS(SELECT FROM (VALUES
            ('pending_root','cp6_authentication_v1.before_change()',62,false),('pending_no_delete','cp6_scope_authorization_v1.immutable()',42,false),
            ('pending_guard','cp6_native_tasks_v2.pending_guard()',23,false),('pending_complete','cp6_native_tasks_v2.pending_complete()',21,true)) a(name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_native_tasks_v2.pending_resolutions'::regclass AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
              AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_native_tasks_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_native_tasks_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Pending resolution history requires forward recovery.");
}
