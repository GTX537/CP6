using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261123065100_WorkflowDeadlineEvidenceV1")]
public sealed class WorkflowDeadlineEvidenceV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_work_calendar_v1.recalculation_previews (
            tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,actor_id uuid NOT NULL,
            expires_at timestamptz NOT NULL,body jsonb NOT NULL,
            PRIMARY KEY(tenant_id,environment_id,id));
        CREATE TABLE cp6_work_calendar_v1.recalculations (
            tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,preview_id uuid NOT NULL,
            task_id uuid NOT NULL,actor_id uuid NOT NULL,from_revision bigint NOT NULL,to_revision bigint NOT NULL,
            writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),
            PRIMARY KEY(tenant_id,environment_id,id),UNIQUE(task_id,to_revision),CHECK(to_revision=from_revision+1),
            FOREIGN KEY(tenant_id,environment_id,preview_id) REFERENCES cp6_work_calendar_v1.recalculation_previews,
            FOREIGN KEY(task_id,from_revision) REFERENCES cp6_native_timeout_v2.deadlines,
            FOREIGN KEY(task_id,to_revision) REFERENCES cp6_native_timeout_v2.deadlines DEFERRABLE INITIALLY DEFERRED);
        DO $$ DECLARE t text;BEGIN
            FOREACH t IN ARRAY ARRAY['recalculation_previews','recalculations'] LOOP
                EXECUTE format('CREATE TRIGGER authority_gate BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
                    ON cp6_work_calendar_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',t);
                EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_work_calendar_v1.%I
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',t);
            END LOOP;
        END $$;
        ALTER TABLE cp6_native_timeout_v2.claims ADD COLUMN task_version bytea,ADD COLUMN instance_version bytea,
            ADD CHECK((task_version IS NULL)=(instance_version IS NULL));

        CREATE OR REPLACE FUNCTION cp6_native_timeout_v2.deadline_guard() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE t public."Wf_FlowTask";v uuid;p jsonb;n jsonb;r uuid;e uuid;BEGIN
            SELECT * INTO t FROM public."Wf_FlowTask" WHERE "Id"=NEW.task_id;
            IF NEW.context_kind='SFS' THEN
                SELECT flow_version,root_id,environment_id INTO v,r,e FROM cp6_personal_form_v2.instances
                  WHERE instance_id=NEW.instance_id AND tenant_id=NEW.tenant_id;
            ELSE
                SELECT flow_version,root_instance_id,environment_id INTO v,r,e FROM cp6_approval_runtime_v2.instances
                  WHERE instance_id=NEW.instance_id AND tenant_id=NEW.tenant_id;
            END IF;
            p=NEW.policy_json::jsonb;
            SELECT x INTO n FROM cp6_workflow_authoring_v2.versions f,
                jsonb_array_elements((f.snapshot_json::jsonb->'fields'->>'SchemaJson')::jsonb->'nodes') x
                WHERE f.id=v AND f.tenant_id=NEW.tenant_id AND f.environment_id=e AND x->>'id'=t."NodeId";
            IF t."Id" IS NULL OR t."TenantId"<>NEW.tenant_id OR t."InstanceId"<>NEW.instance_id
              OR (r,e,v,t."NodeId") IS DISTINCT FROM (NEW.root_id,NEW.environment_id,(p->>'flowVersionId')::uuid,p->>'nodeId')
              OR n IS NULL OR (p->'hours',lower(p->>'action'),p->'escalateTo',p->'workday'->>'workdays',
                    (p->'workday'->>'localTime')::time,p->'workday'->>'ambiguousOffsetMinutes')
                IS DISTINCT FROM (coalesce(n->'timeoutHours','null'::jsonb),lower(nullif(btrim(n->>'timeoutAction'),'')),
                    coalesce(n->'escalateTo','null'::jsonb),n->'timeoutWorkday'->>'workdays',
                    (n->'timeoutWorkday'->>'localTime')::time,n->'timeoutWorkday'->>'ambiguousOffsetMinutes')
              OR NEW.writer_xid<>pg_current_xact_id()
              OR NOT (
                EXISTS(SELECT FROM cp6_personal_form_v2.commands c WHERE c.root_id=r AND c.tenant_id=NEW.tenant_id
                    AND c.environment_id=e AND c.writer_xid=pg_current_xact_id() AND c.completed AND NOT c.closed)
                OR EXISTS(SELECT FROM cp6_approval_start_v2.starts c WHERE c.instance_id=r AND c.tenant_id=NEW.tenant_id
                    AND c.environment_id=e AND c.writer_xid=pg_current_xact_id())
                OR EXISTS(SELECT FROM cp6_approval_decision_v2.commands c JOIN cp6_approval_runtime_v2.instances i
                    ON i.instance_id=c.instance_id WHERE i.root_instance_id=r AND c.writer_xid=pg_current_xact_id() AND c.completed)
                OR EXISTS(SELECT FROM cp6_native_tasks_v2.commands c WHERE c.root_id=r AND c.tenant_id=NEW.tenant_id
                    AND c.environment_id=e AND c.writer_xid=pg_current_xact_id() AND c.completed AND NOT c.closed)
                OR EXISTS(SELECT FROM cp6_work_calendar_v1.recalculations a
                    JOIN cp6_work_calendar_v1.recalculation_previews w ON (w.tenant_id,w.environment_id,w.id)=(a.tenant_id,a.environment_id,a.preview_id)
                    JOIN cp6_main_owner_v1.operations o ON o.tenant_id=a.tenant_id AND o.operation_id=a.id
                      AND o.owner='OA_WORK_CALENDAR' AND o.contract='OA_DEADLINE_RECALCULATE/1' AND o.actor_id=a.actor_id AND o.state='COMMITTED'
                    JOIN cp6_native_timeout_v2.deadlines d ON (d.task_id,d.revision)=(a.task_id,a.from_revision)
                    JOIN cp6_native_timeout_v2.pending h ON (h.task_id,h.revision)=(a.task_id,a.to_revision)
                    WHERE (a.tenant_id,a.environment_id,a.task_id,a.to_revision,w.actor_id,d.created_at)
                        =(NEW.tenant_id,NEW.environment_id,NEW.task_id,NEW.revision,a.actor_id,NEW.created_at)
                      AND a.writer_xid=pg_current_xact_id()))
            THEN RAISE EXCEPTION 'NATIVE_DEADLINE_OWNER_REQUIRED' USING ERRCODE='23514';END IF;
            IF p->>'basis'='WORKDAY_LOCAL' AND
              ((p->>'calendarId')::uuid IS DISTINCT FROM NEW.tenant_id OR NOT coalesce(
                p->>'calendarRevision'='0' OR EXISTS(SELECT FROM cp6_work_calendar_v1.revisions c WHERE c.tenant_id=NEW.tenant_id
                    AND c.revision=(p->>'calendarRevision')::bigint),false))
            THEN RAISE EXCEPTION 'NATIVE_DEADLINE_CALENDAR_REQUIRED' USING ERRCODE='23514';END IF;
            IF NEW.due_at IS NOT NULL AND p->>'basis'='UTC_DURATION'
              AND ((p->>'hours')::int IS NULL OR (p->>'hours')::int<=0 OR NEW.due_at<>NEW.created_at+make_interval(hours=>(p->>'hours')::int))
            THEN RAISE EXCEPTION 'NATIVE_DEADLINE_DURATION_INVALID' USING ERRCODE='23514';END IF;
            IF (p->>'timeZoneRulesVersion' IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_work_calendar_v1.time_zone_rules z
                WHERE (z.zone_id,z.version)=(p->>'timeZoneId',p->>'timeZoneRulesVersion')))
              OR (NEW.due_at IS NOT NULL AND
                (p->>'timeZoneRulesVersion' IS NULL OR NEW.due_at IS DISTINCT FROM
                    (((p->>'localDueAt')::timestamp-make_interval(mins=>(p->>'offsetMinutes')::int)) AT TIME ZONE 'UTC')))
            THEN RAISE EXCEPTION 'NATIVE_DEADLINE_TIME_ZONE_EVIDENCE_REQUIRED' USING ERRCODE='23514';END IF;
            RETURN NULL;
        END $$;

        CREATE FUNCTION cp6_native_timeout_v2.result_versions() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF NEW.command_id IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_native_timeout_v2.claims c WHERE c.execution_id=NEW.execution_id
              AND c.task_version=decode(NEW.body::jsonb->>'originalTaskVersion','base64')
              AND c.instance_version=decode(NEW.body::jsonb->>'originalInstanceVersion','base64'))
            THEN RAISE EXCEPTION 'NATIVE_TIMEOUT_CLAIM_VERSIONS_REQUIRED' USING ERRCODE='23514';END IF;
            RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER result_versions AFTER INSERT ON cp6_native_timeout_v2.results
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_native_timeout_v2.result_versions();

        CREATE FUNCTION cp6_work_calendar_v1.recalculation_ready() RETURNS boolean
        LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT NOT EXISTS(SELECT FROM (VALUES('recalculation_previews'),('recalculations')) a(tbl)
                CROSS JOIN (VALUES('immutable','cp6_scope_authorization_v1.immutable()',58),('authority_gate','cp6_authentication_v1.before_change()',62)) b(name,fn,kind)
                WHERE NOT EXISTS(SELECT FROM pg_trigger WHERE tgrelid=to_regclass('cp6_work_calendar_v1.'||a.tbl)
                    AND tgname=b.name AND tgfoid=to_regprocedure(b.fn) AND tgtype=b.kind
                    AND tgenabled IN ('O','A') AND NOT tgisinternal))
        $$;
        CREATE FUNCTION cp6_native_timeout_v2.claim_versions_ready() RETURNS boolean
        LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT EXISTS(SELECT FROM pg_trigger WHERE tgrelid='cp6_native_timeout_v2.results'::regclass
                AND tgname='result_versions' AND tgfoid=to_regprocedure('cp6_native_timeout_v2.result_versions()')
                AND tgtype=5 AND tgenabled IN ('O','A') AND tgdeferrable AND tginitdeferred AND tgqual IS NULL AND tgnargs=0)
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_work_calendar_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_work_calendar_v1 FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_native_timeout_v2.result_versions(),cp6_native_timeout_v2.claim_versions_ready() FROM PUBLIC;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("截止与重算凭证只支持前滚。");
}
