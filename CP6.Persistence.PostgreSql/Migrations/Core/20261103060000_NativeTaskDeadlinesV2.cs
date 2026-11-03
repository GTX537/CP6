using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261103060000_NativeTaskDeadlinesV2")]
public sealed class NativeTaskDeadlinesV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_native_timeout_v2;
        ALTER TABLE cp6_native_tasks_v2.commands DROP CONSTRAINT commands_operation_check;
        ALTER TABLE cp6_native_tasks_v2.commands ADD CHECK(operation IN('OA_TASK_TRANSFER','OA_TASK_ADDSIGN','OA_TASK_SENDBACK','OA_APPROVER_RESOLVE','OA_TASK_TIMEOUT'));
        CREATE TABLE cp6_native_timeout_v2.registrations(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,service_id uuid NOT NULL,host_key text COLLATE "C" NOT NULL,
          revision uuid NOT NULL DEFAULT gen_random_uuid(),enabled boolean NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,service_id),CHECK(service_id<>'00000000-0000-0000-0000-000000000000'::uuid),CHECK(length(host_key)>0));
        CREATE TABLE cp6_native_timeout_v2.deadlines(
          task_id uuid NOT NULL REFERENCES public."Wf_FlowTask"("Id") DEFERRABLE INITIALLY DEFERRED,
          revision bigint NOT NULL CHECK(revision>0),tenant_id uuid NOT NULL,environment_id uuid NOT NULL,
          instance_id uuid NOT NULL,root_id uuid NOT NULL,context_kind text NOT NULL CHECK(context_kind IN('SFS','APP')),
          created_at timestamptz NOT NULL,due_at timestamptz,policy_json text NOT NULL,
          writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),PRIMARY KEY(task_id,revision));
        CREATE TABLE cp6_native_timeout_v2.pending(
          task_id uuid PRIMARY KEY,revision bigint NOT NULL,occurrence bigint NOT NULL CHECK(occurrence>0),
          next_due timestamptz,state text NOT NULL CHECK(state IN('READY','CLAIMED','COMPLETED','BLOCKED_POLICY','BLOCKED_TARGET','INVALID_POLICY','SKIPPED_ALREADY_HANDLED')),
          FOREIGN KEY(task_id,revision) REFERENCES cp6_native_timeout_v2.deadlines(task_id,revision));
        CREATE INDEX timeout_pending_due ON cp6_native_timeout_v2.pending(next_due,task_id) WHERE state IN('READY','CLAIMED');
        CREATE TABLE cp6_native_timeout_v2.claims(
          execution_id uuid PRIMARY KEY,task_id uuid NOT NULL,revision bigint NOT NULL,occurrence bigint NOT NULL,
          epoch bigint NOT NULL CHECK(epoch>0),claim_id uuid NOT NULL,service_id uuid NOT NULL,registration_revision uuid NOT NULL,
          lease_until timestamptz NOT NULL,UNIQUE(task_id,revision,occurrence),
          FOREIGN KEY(task_id,revision) REFERENCES cp6_native_timeout_v2.deadlines(task_id,revision));
        CREATE TABLE cp6_native_timeout_v2.results(
          execution_id uuid PRIMARY KEY REFERENCES cp6_native_timeout_v2.claims(execution_id),body text NOT NULL,
          command_id uuid UNIQUE,recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id());
        CREATE TABLE cp6_native_timeout_v2.observations(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,service_id uuid NOT NULL,last_scan timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,service_id));

        CREATE FUNCTION cp6_native_timeout_v2.registration_changed() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN NEW.revision=gen_random_uuid();RETURN NEW;END $$;
        CREATE TRIGGER registration_changed BEFORE UPDATE ON cp6_native_timeout_v2.registrations FOR EACH ROW EXECUTE FUNCTION cp6_native_timeout_v2.registration_changed();
        CREATE FUNCTION cp6_native_timeout_v2.deadline_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE t public."Wf_FlowTask";v uuid;p jsonb;n jsonb;r uuid;e uuid;k text;BEGIN
          SELECT * INTO t FROM public."Wf_FlowTask" WHERE "Id"=NEW.task_id;
          IF NEW.context_kind='SFS' THEN
            SELECT flow_version,root_id,environment_id INTO v,r,e FROM cp6_personal_form_v2.instances WHERE instance_id=NEW.instance_id AND tenant_id=NEW.tenant_id;
          ELSE
            SELECT flow_version,root_instance_id,environment_id INTO v,r,e FROM cp6_approval_runtime_v2.instances WHERE instance_id=NEW.instance_id AND tenant_id=NEW.tenant_id;
          END IF;
          p=NEW.policy_json::jsonb;
          SELECT x INTO n FROM cp6_workflow_authoring_v2.versions f,
            jsonb_array_elements((f.snapshot_json::jsonb->'fields'->>'SchemaJson')::jsonb->'nodes') x
            WHERE f.id=v AND f.tenant_id=NEW.tenant_id AND f.environment_id=e AND x->>'id'=t."NodeId";
          IF t."Id" IS NULL OR t."TenantId"<>NEW.tenant_id OR t."InstanceId"<>NEW.instance_id
            OR (r,e,v,t."NodeId") IS DISTINCT FROM (NEW.root_id,NEW.environment_id,(p->>'flowVersionId')::uuid,p->>'nodeId')
            OR n IS NULL OR (p->'hours',lower(p->>'action'),p->'escalateTo') IS DISTINCT FROM (COALESCE(n->'timeoutHours','null'::jsonb),lower(btrim(n->>'timeoutAction')),COALESCE(n->'escalateTo','null'::jsonb))
            OR NEW.writer_xid<>pg_current_xact_id()
            OR NOT (EXISTS(SELECT FROM cp6_personal_form_v2.commands c WHERE c.root_id=r AND c.tenant_id=NEW.tenant_id AND c.environment_id=e AND c.writer_xid=pg_current_xact_id() AND c.completed AND NOT c.closed)
              OR EXISTS(SELECT FROM cp6_approval_start_v2.starts c WHERE c.instance_id=r AND c.tenant_id=NEW.tenant_id AND c.environment_id=e AND c.writer_xid=pg_current_xact_id())
              OR EXISTS(SELECT FROM cp6_approval_decision_v2.commands c JOIN cp6_approval_runtime_v2.instances i ON i.instance_id=c.instance_id WHERE i.root_instance_id=r AND c.writer_xid=pg_current_xact_id() AND c.completed)
              OR EXISTS(SELECT FROM cp6_native_tasks_v2.commands c WHERE c.root_id=r AND c.tenant_id=NEW.tenant_id AND c.environment_id=e AND c.writer_xid=pg_current_xact_id() AND c.completed AND NOT c.closed))
            THEN RAISE EXCEPTION 'NATIVE_DEADLINE_OWNER_REQUIRED' USING ERRCODE='23514';END IF;
          IF NEW.due_at IS NOT NULL AND (NEW.due_at<>NEW.created_at+make_interval(hours=>(p->>'hours')::int) OR (p->>'hours')::int<=0)
            THEN RAISE EXCEPTION 'NATIVE_DEADLINE_DURATION_INVALID' USING ERRCODE='23514';END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER deadline_guard AFTER INSERT ON cp6_native_timeout_v2.deadlines DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_native_timeout_v2.deadline_guard();
        CREATE FUNCTION cp6_native_timeout_v2.result_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_native_timeout_v2.claims;d cp6_native_timeout_v2.deadlines;p jsonb;BEGIN
          SELECT * INTO c FROM cp6_native_timeout_v2.claims WHERE execution_id=NEW.execution_id;
          SELECT * INTO d FROM cp6_native_timeout_v2.deadlines WHERE task_id=c.task_id AND revision=c.revision;
          p=NEW.body::jsonb;
          IF NEW.writer_xid<>pg_current_xact_id() OR c.epoch IS DISTINCT FROM (p->>'claimEpoch')::bigint OR c.lease_until<=clock_timestamp()
            OR (c.task_id,c.revision,c.occurrence,c.service_id,c.execution_id)
            IS DISTINCT FROM ((p->>'taskId')::uuid,(p->>'revision')::bigint,(p->>'occurrence')::bigint,(p->>'serviceId')::uuid,(p->>'executionId')::uuid)
            OR NOT EXISTS(SELECT FROM cp6_native_timeout_v2.registrations r WHERE r.tenant_id=d.tenant_id AND r.environment_id=d.environment_id
              AND r.service_id=c.service_id AND r.revision=c.registration_revision AND r.enabled)
            THEN RAISE EXCEPTION 'NATIVE_TIMEOUT_AUTHORITY_REQUIRED' USING ERRCODE='23514';END IF;
          IF NEW.command_id IS NOT NULL AND NOT (
            EXISTS(SELECT FROM cp6_personal_form_v2.commands x WHERE x.id=NEW.command_id AND x.task_id=c.task_id AND x.actor_id=c.service_id AND x.root_id=d.root_id AND x.completed AND x.writer_xid=pg_current_xact_id())
            OR EXISTS(SELECT FROM cp6_native_tasks_v2.commands x WHERE x.id=NEW.command_id AND x.task_id=c.task_id AND x.actor_id=c.service_id AND x.root_id=d.root_id AND x.completed AND x.writer_xid=pg_current_xact_id()))
            THEN RAISE EXCEPTION 'NATIVE_TIMEOUT_EFFECT_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER result_guard AFTER INSERT ON cp6_native_timeout_v2.results DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_native_timeout_v2.result_guard();
        CREATE FUNCTION cp6_native_timeout_v2.command_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE x jsonb;BEGIN
          IF TG_TABLE_SCHEMA='cp6_personal_form_v2' THEN SELECT to_jsonb(c) INTO x FROM cp6_personal_form_v2.commands c WHERE id=NEW.id;
          ELSE SELECT to_jsonb(c) INTO x FROM cp6_native_tasks_v2.commands c WHERE id=NEW.id;END IF;
          IF x->>'operation' NOT IN('FORM_TIMEOUT','OA_TASK_TIMEOUT')
            AND NOT COALESCE((x->>'audit_json')::jsonb->>'evidenceKind'='SYSTEM_EXECUTION' AND (x->>'audit_json')::jsonb->>'trigger'='TIMEOUT',false) THEN RETURN NULL;END IF;
          IF NOT EXISTS(SELECT FROM cp6_native_timeout_v2.results r JOIN cp6_native_timeout_v2.claims c ON c.execution_id=r.execution_id
            JOIN cp6_native_timeout_v2.deadlines d ON d.task_id=c.task_id AND d.revision=c.revision
            WHERE r.command_id=NEW.id AND r.writer_xid=pg_current_xact_id() AND (d.tenant_id,d.environment_id,d.root_id,c.task_id,c.service_id)
              =((x->>'tenant_id')::uuid,(x->>'environment_id')::uuid,(x->>'root_id')::uuid,(x->>'task_id')::uuid,(x->>'actor_id')::uuid))
            OR EXISTS(SELECT FROM cp6_personal_form_v2.votes WHERE command_id=NEW.id)
            OR EXISTS(SELECT FROM cp6_approval_decision_v2.votes WHERE command_id=NEW.id)
            THEN RAISE EXCEPTION 'TIMEOUT_COMMAND_RESULT_ATOMIC_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER timeout_command_guard AFTER INSERT OR UPDATE ON cp6_personal_form_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_native_timeout_v2.command_guard();
        CREATE CONSTRAINT TRIGGER timeout_command_guard AFTER INSERT OR UPDATE ON cp6_native_tasks_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_native_timeout_v2.command_guard();
        DO $$ DECLARE t text;BEGIN
          FOREACH t IN ARRAY ARRAY['registrations','deadlines','pending','claims','results','observations'] LOOP
            EXECUTE format('CREATE TRIGGER timeout_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_native_timeout_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',t);
          END LOOP;
          FOREACH t IN ARRAY ARRAY['deadlines','results'] LOOP
            EXECUTE format('CREATE TRIGGER timeout_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_native_timeout_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',t);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_native_timeout_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_native_tasks_v2.ready() AND NOT EXISTS(SELECT FROM (VALUES
            ('registrations','timeout_root','cp6_authentication_v1.before_change()',62),
            ('registrations','registration_changed','cp6_native_timeout_v2.registration_changed()',19),
            ('deadlines','timeout_root','cp6_authentication_v1.before_change()',62),
            ('deadlines','timeout_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('pending','timeout_root','cp6_authentication_v1.before_change()',62),
            ('claims','timeout_root','cp6_authentication_v1.before_change()',62),
            ('results','timeout_root','cp6_authentication_v1.before_change()',62),
            ('results','timeout_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('observations','timeout_root','cp6_authentication_v1.before_change()',62)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_native_timeout_v2.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND NOT t.tgdeferrable))
          AND NOT EXISTS(SELECT FROM (VALUES('deadlines','deadline_guard','cp6_native_timeout_v2.deadline_guard()'),('results','result_guard','cp6_native_timeout_v2.result_guard()')) a(tbl,name,fn)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_native_timeout_v2.'||a.tbl) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
              AND t.tgtype=5 AND t.tgenabled IN('O','A') AND t.tgdeferrable AND t.tginitdeferred AND t.tgqual IS NULL AND t.tgnargs=0))
          AND NOT EXISTS(SELECT FROM (VALUES('cp6_personal_form_v2.commands'),('cp6_native_tasks_v2.commands')) a(tbl)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tbl) AND t.tgname='timeout_command_guard'
              AND t.tgfoid=to_regprocedure('cp6_native_timeout_v2.command_guard()') AND t.tgtype=21 AND t.tgenabled IN('O','A')
              AND t.tgdeferrable AND t.tginitdeferred AND t.tgqual IS NULL AND t.tgnargs=0));
        $$;
        REVOKE ALL ON SCHEMA cp6_native_timeout_v2 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_native_timeout_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_native_timeout_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward-only native timeout evidence.");
}
