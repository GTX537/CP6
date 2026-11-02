using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)),Migration("20261102210000_WorkflowReadFactsV2")]
public sealed class WorkflowReadFactsV2:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_workflow_read_v2;
        REVOKE ALL ON SCHEMA cp6_workflow_read_v2 FROM PUBLIC;
        CREATE TABLE cp6_workflow_read_v2.commands(
          id uuid PRIMARY KEY,tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,
          kind text COLLATE "C" NOT NULL CHECK(kind IN('TASK','CC')),target_id uuid NOT NULL,
          command_key text COLLATE "C" NOT NULL CHECK(command_key ~ '^[A-Za-z0-9_.-]{1,100}$'),
          digest text,raw_body bytea,result_json text NOT NULL,closed boolean NOT NULL,
          writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          UNIQUE(tenant_id,environment_id,actor_id,kind,target_id,command_key),
          CHECK((closed AND digest IS NULL AND raw_body IS NULL) OR (NOT closed AND digest IS NOT NULL AND raw_body IS NOT NULL)));
        CREATE TABLE cp6_workflow_read_v2.facts(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,instance_id uuid NOT NULL REFERENCES public."Wf_FlowInstance"("Id"),
          kind text COLLATE "C" NOT NULL CHECK(kind IN('TASK','CC')),target_id uuid NOT NULL,
          principal_id uuid NOT NULL,actual_id uuid NOT NULL,first_read_at timestamptz NOT NULL,first_command_key text COLLATE "C" NOT NULL,
          command_id uuid NOT NULL UNIQUE REFERENCES cp6_workflow_read_v2.commands(id),
          PRIMARY KEY(tenant_id,environment_id,kind,target_id));
        CREATE INDEX workflow_read_instances ON cp6_workflow_read_v2.facts(tenant_id,environment_id,instance_id);
        CREATE FUNCTION cp6_workflow_read_v2.fact_writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_workflow_read_v2.commands; principal uuid;inst uuid;t uuid; BEGIN
          SELECT * INTO c FROM cp6_workflow_read_v2.commands WHERE id=NEW.command_id;
          IF c.closed OR c.writer_xid<>pg_current_xact_id() OR
            (c.tenant_id,c.environment_id,c.kind,c.target_id,c.actor_id,c.command_key)
              IS DISTINCT FROM (NEW.tenant_id,NEW.environment_id,NEW.kind,NEW.target_id,NEW.actual_id,NEW.first_command_key)
            THEN RAISE EXCEPTION 'INBOX_READ_COMMAND_REQUIRED' USING ERRCODE='23514'; END IF;
          IF NEW.kind='TASK' THEN SELECT "InstanceId","AssigneeId","TenantId" INTO inst,principal,t FROM public."Wf_FlowTask" WHERE "Id"=NEW.target_id;
          ELSE SELECT "InstanceId","RecipientId","TenantId" INTO inst,principal,t FROM public."Wf_FlowCc" WHERE "Id"=NEW.target_id; END IF;
          IF (inst,principal,t) IS DISTINCT FROM (NEW.instance_id,NEW.principal_id,NEW.tenant_id)
            OR NOT (EXISTS(SELECT FROM cp6_personal_form_v2.instances x WHERE x.instance_id=inst AND x.tenant_id=t AND x.environment_id=NEW.environment_id)
              OR EXISTS(SELECT FROM cp6_approval_runtime_v2.instances x WHERE x.instance_id=inst AND x.tenant_id=t AND x.environment_id=NEW.environment_id))
            THEN RAISE EXCEPTION 'INBOX_READ_TARGET_MISMATCH' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_workflow_read_v2.command_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE f cp6_workflow_read_v2.facts;b jsonb; BEGIN
          b=NEW.result_json::jsonb;
          IF b->>'commandId' IS DISTINCT FROM NEW.command_key OR b->>'state' IS DISTINCT FROM (CASE WHEN NEW.closed THEN 'CLOSED' ELSE 'READ' END)
            THEN RAISE EXCEPTION 'INBOX_READ_RESULT_MISMATCH' USING ERRCODE='23514'; END IF;
          IF NOT NEW.closed THEN
            SELECT * INTO f FROM cp6_workflow_read_v2.facts WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND kind=NEW.kind AND target_id=NEW.target_id;
            IF f.command_id IS NULL OR (b->'result'->>'targetId')::uuid IS DISTINCT FROM f.target_id
              OR b->'result'->>'kind' IS DISTINCT FROM f.kind
              OR (b->'result'->>'principalId')::uuid IS DISTINCT FROM f.principal_id
              OR (b->'result'->>'actualUserId')::uuid IS DISTINCT FROM f.actual_id
              OR (b->'result'->>'firstReadAtUtc')::timestamptz IS DISTINCT FROM f.first_read_at
              OR b->'result'->>'firstCommandId' IS DISTINCT FROM f.first_command_key
              THEN RAISE EXCEPTION 'INBOX_READ_FACT_REQUIRED' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER read_command_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_read_v2.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER read_command_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_read_v2.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER read_command_complete AFTER INSERT ON cp6_workflow_read_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_workflow_read_v2.command_complete();
        CREATE TRIGGER read_fact_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_read_v2.facts FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER read_fact_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_workflow_read_v2.facts FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER read_fact_writer BEFORE INSERT ON cp6_workflow_read_v2.facts FOR EACH ROW EXECUTE FUNCTION cp6_workflow_read_v2.fact_writer();
        CREATE FUNCTION cp6_workflow_read_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT current_setting('session_replication_role')='origin' AND cp6_scope_authorization_v1.writers_ready()
            AND NOT EXISTS(SELECT FROM (VALUES
              ('commands','read_command_root','cp6_authentication_v1.before_change()',62,false),
              ('commands','read_command_immutable','cp6_scope_authorization_v1.immutable()',58,false),
              ('commands','read_command_complete','cp6_workflow_read_v2.command_complete()',5,true),
              ('facts','read_fact_root','cp6_authentication_v1.before_change()',62,false),
              ('facts','read_fact_immutable','cp6_scope_authorization_v1.immutable()',58,false),
              ('facts','read_fact_writer','cp6_workflow_read_v2.fact_writer()',7,false)) e(tab,name,fn,kind,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_workflow_read_v2.'||e.tab) AND t.tgname=e.name
              AND t.tgfoid=to_regprocedure(e.fn) AND t.tgenabled='O' AND t.tgtype=e.kind AND t.tgdeferrable=e.deferred AND t.tginitdeferred=e.deferred));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_workflow_read_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_workflow_read_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Read facts and command recovery require forward recovery.");
}
