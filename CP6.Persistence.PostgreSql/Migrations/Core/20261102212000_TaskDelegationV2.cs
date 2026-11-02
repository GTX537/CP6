using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)),Migration("20261102212000_TaskDelegationV2")]
public sealed class TaskDelegationV2:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_task_delegation_v2;
        REVOKE ALL ON SCHEMA cp6_task_delegation_v2 FROM PUBLIC;
        CREATE TABLE cp6_task_delegation_v2.commands(
          id uuid PRIMARY KEY,tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,
          operation text COLLATE "C" NOT NULL CHECK(operation IN('CREATE','REVISE','REVOKE')),
          resource text COLLATE "C" NOT NULL,command_key text COLLATE "C" NOT NULL,digest text,raw_body bytea,
          grant_id uuid,result_json text NOT NULL,closed boolean NOT NULL,writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          UNIQUE(tenant_id,environment_id,actor_id,operation,resource,command_key),
          CHECK((closed AND digest IS NULL AND raw_body IS NULL AND grant_id IS NULL) OR (NOT closed AND digest IS NOT NULL AND raw_body IS NOT NULL AND grant_id IS NOT NULL)));
        CREATE TABLE cp6_task_delegation_v2.heads(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,grantor_id uuid NOT NULL,
          revision bigint NOT NULL CHECK(revision>0),PRIMARY KEY(tenant_id,environment_id,id));
        CREATE TABLE cp6_task_delegation_v2.revisions(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,grant_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          version uuid NOT NULL UNIQUE,body text NOT NULL,command_id uuid NOT NULL UNIQUE REFERENCES cp6_task_delegation_v2.commands(id),
          PRIMARY KEY(tenant_id,environment_id,grant_id,revision),
          FOREIGN KEY(tenant_id,environment_id,grant_id) REFERENCES cp6_task_delegation_v2.heads(tenant_id,environment_id,id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_task_delegation_v2.heads ADD CONSTRAINT task_grant_current_revision FOREIGN KEY(tenant_id,environment_id,id,revision)
          REFERENCES cp6_task_delegation_v2.revisions(tenant_id,environment_id,grant_id,revision) DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_task_delegation_v2.revision_writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_task_delegation_v2.commands;b jsonb; BEGIN
          SELECT * INTO c FROM cp6_task_delegation_v2.commands WHERE id=NEW.command_id;b=NEW.body::jsonb;
          IF c.id IS NULL OR c.closed OR c.writer_xid<>pg_current_xact_id() OR
            (c.tenant_id,c.environment_id,c.grant_id) IS DISTINCT FROM (NEW.tenant_id,NEW.environment_id,NEW.grant_id)
            OR (b->>'grantId')::uuid IS DISTINCT FROM NEW.grant_id OR (b->>'version')::uuid IS DISTINCT FROM NEW.version
            OR (b->>'revision')::bigint IS DISTINCT FROM NEW.revision OR (b->>'grantorId')::uuid IS DISTINCT FROM c.actor_id
            OR c.result_json::jsonb->'result' IS DISTINCT FROM b
            THEN RAISE EXCEPTION 'GRANT_COMMAND_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_task_delegation_v2.head_writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r cp6_task_delegation_v2.revisions;c cp6_task_delegation_v2.commands; BEGIN
          SELECT * INTO r FROM cp6_task_delegation_v2.revisions WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND grant_id=NEW.id AND revision=NEW.revision;
          SELECT * INTO c FROM cp6_task_delegation_v2.commands WHERE id=r.command_id;
          IF c.id IS NULL OR c.closed OR c.writer_xid<>pg_current_xact_id() OR c.actor_id<>NEW.grantor_id
            OR (TG_OP='INSERT' AND (NEW.revision<>1 OR c.operation<>'CREATE'))
            THEN RAISE EXCEPTION 'GRANT_REVISION_REQUIRED' USING ERRCODE='23514'; END IF;
          IF TG_OP='UPDATE' AND ((NEW.tenant_id,NEW.environment_id,NEW.id,NEW.grantor_id) IS DISTINCT FROM (OLD.tenant_id,OLD.environment_id,OLD.id,OLD.grantor_id)
            OR NEW.revision<>OLD.revision+1 OR c.operation NOT IN('REVISE','REVOKE'))
            THEN RAISE EXCEPTION 'GRANT_REVISION_CONFLICT' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_task_delegation_v2.command_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NEW.result_json::jsonb->>'commandId' IS DISTINCT FROM NEW.command_key OR NEW.result_json::jsonb->>'state' IS DISTINCT FROM (CASE WHEN NEW.closed THEN 'CLOSED' ELSE 'SUCCEEDED' END)
            OR (NOT NEW.closed AND NOT EXISTS(SELECT FROM cp6_task_delegation_v2.revisions r JOIN cp6_task_delegation_v2.heads h
              ON (h.tenant_id,h.environment_id,h.id,h.revision)=(r.tenant_id,r.environment_id,r.grant_id,r.revision) WHERE r.command_id=NEW.id))
            THEN RAISE EXCEPTION 'GRANT_RESULT_INCOMPLETE' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER grant_command_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_task_delegation_v2.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER grant_command_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_task_delegation_v2.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER grant_command_complete AFTER INSERT ON cp6_task_delegation_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_task_delegation_v2.command_complete();
        CREATE TRIGGER grant_revision_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_task_delegation_v2.revisions FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER grant_revision_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_task_delegation_v2.revisions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER grant_revision_writer BEFORE INSERT ON cp6_task_delegation_v2.revisions FOR EACH ROW EXECUTE FUNCTION cp6_task_delegation_v2.revision_writer();
        CREATE TRIGGER grant_head_authority BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_task_delegation_v2.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change();
        CREATE TRIGGER grant_head_no_delete BEFORE DELETE OR TRUNCATE ON cp6_task_delegation_v2.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER grant_head_writer BEFORE INSERT OR UPDATE ON cp6_task_delegation_v2.heads FOR EACH ROW EXECUTE FUNCTION cp6_task_delegation_v2.head_writer();
        ALTER TABLE cp6_workflow_read_v2.facts ADD COLUMN grant_id uuid,ADD COLUMN grant_revision bigint;
        ALTER TABLE cp6_workflow_read_v2.facts ADD CONSTRAINT read_fact_grant_identity CHECK(
          (actual_id=principal_id AND grant_id IS NULL AND grant_revision IS NULL) OR (actual_id<>principal_id AND grant_id IS NOT NULL AND grant_revision IS NOT NULL));
        ALTER TABLE cp6_workflow_read_v2.facts ADD CONSTRAINT read_fact_grant_revision FOREIGN KEY(tenant_id,environment_id,grant_id,grant_revision)
          REFERENCES cp6_task_delegation_v2.revisions(tenant_id,environment_id,grant_id,revision);
        CREATE FUNCTION cp6_task_delegation_v2.read_grant() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb;i public."Wf_FlowInstance";biz text; BEGIN
          IF NEW.actual_id=NEW.principal_id THEN RETURN NEW; END IF;
          SELECT r.body::jsonb INTO b FROM cp6_task_delegation_v2.revisions r JOIN cp6_task_delegation_v2.heads h
            ON (h.tenant_id,h.environment_id,h.id,h.revision)=(r.tenant_id,r.environment_id,r.grant_id,r.revision)
            WHERE r.tenant_id=NEW.tenant_id AND r.environment_id=NEW.environment_id AND r.grant_id=NEW.grant_id AND r.revision=NEW.grant_revision;
          SELECT * INTO i FROM public."Wf_FlowInstance" WHERE "Id"=NEW.instance_id AND "TenantId"=NEW.tenant_id;
          SELECT convert_from(s.payload,'UTF8')::jsonb->'subject'->>'bizType' INTO biz
            FROM cp6_approval_runtime_v2.instances x JOIN cp6_approval_intake_v2.submissions s
            ON (s.tenant_id,s.environment_id,s.owner,s.id)=(x.tenant_id,x.environment_id,x.owner,x.submission_id)
            WHERE x.instance_id=NEW.instance_id AND x.tenant_id=NEW.tenant_id AND x.environment_id=NEW.environment_id;
          IF NOT FOUND THEN biz=i."BizType"; END IF;
          IF b IS NULL OR (b->>'grantorId')::uuid<>NEW.principal_id OR (b->>'delegateId')::uuid<>NEW.actual_id OR NOT (b->>'enabled')::boolean
            OR NEW.first_read_at<(b->>'fromUtc')::timestamptz OR NEW.first_read_at>=(b->>'toUtc')::timestamptz
            OR NOT (b->'scope'->'actionKinds' ? 'TASK_READ')
            OR (b->'scope'->'flowKeys'<>'null'::jsonb AND NOT (b->'scope'->'flowKeys' ? i."FlowKey"))
            OR (b->'scope'->'bizTypes'<>'null'::jsonb AND (biz IS NULL OR NOT (b->'scope'->'bizTypes' ? biz)))
            THEN RAISE EXCEPTION 'READ_GRANT_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_task_delegation_v2.read_result() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE f cp6_workflow_read_v2.facts;b jsonb; BEGIN
          IF NEW.closed THEN RETURN NULL; END IF;b=NEW.result_json::jsonb->'result';
          SELECT * INTO f FROM cp6_workflow_read_v2.facts WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND kind=NEW.kind AND target_id=NEW.target_id;
          IF (b->>'grantId')::uuid IS DISTINCT FROM f.grant_id OR (b->>'grantRevision')::bigint IS DISTINCT FROM f.grant_revision
            THEN RAISE EXCEPTION 'READ_GRANT_RESULT_MISMATCH' USING ERRCODE='23514'; END IF;RETURN NULL;
        END $$;
        CREATE TRIGGER read_fact_grant_v2 BEFORE INSERT ON cp6_workflow_read_v2.facts FOR EACH ROW EXECUTE FUNCTION cp6_task_delegation_v2.read_grant();
        CREATE CONSTRAINT TRIGGER read_result_grant_v2 AFTER INSERT ON cp6_workflow_read_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_task_delegation_v2.read_result();
        CREATE FUNCTION cp6_task_delegation_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT current_setting('session_replication_role')='origin' AND cp6_scope_authorization_v1.writers_ready()
            AND EXISTS(SELECT FROM pg_trigger WHERE tgrelid='cp6_workflow_read_v2.facts'::regclass AND tgname='read_fact_grant_v2'
              AND tgfoid='cp6_task_delegation_v2.read_grant()'::regprocedure AND tgenabled='O' AND tgtype=7)
            AND EXISTS(SELECT FROM pg_trigger WHERE tgrelid='cp6_workflow_read_v2.commands'::regclass AND tgname='read_result_grant_v2'
              AND tgfoid='cp6_task_delegation_v2.read_result()'::regprocedure AND tgenabled='O' AND tgtype=5 AND tgdeferrable AND tginitdeferred)
            AND NOT EXISTS(SELECT FROM (VALUES
              ('commands','grant_command_root','cp6_authentication_v1.before_change()',62,false),
              ('commands','grant_command_immutable','cp6_scope_authorization_v1.immutable()',58,false),
              ('commands','grant_command_complete','cp6_task_delegation_v2.command_complete()',5,true),
              ('revisions','grant_revision_root','cp6_authentication_v1.before_change()',62,false),
              ('revisions','grant_revision_immutable','cp6_scope_authorization_v1.immutable()',58,false),
              ('revisions','grant_revision_writer','cp6_task_delegation_v2.revision_writer()',7,false),
              ('heads','grant_head_authority','cp6_scope_authorization_v1.before_authority_change()',62,false),
              ('heads','grant_head_no_delete','cp6_scope_authorization_v1.immutable()',42,false),
              ('heads','grant_head_writer','cp6_task_delegation_v2.head_writer()',23,false)) e(tab,name,fn,kind,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_task_delegation_v2.'||e.tab) AND t.tgname=e.name
              AND t.tgfoid=to_regprocedure(e.fn) AND t.tgenabled='O' AND t.tgtype=e.kind AND t.tgdeferrable=e.deferred AND t.tginitdeferred=e.deferred));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_task_delegation_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_task_delegation_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Delegation revisions require forward recovery.");
}
