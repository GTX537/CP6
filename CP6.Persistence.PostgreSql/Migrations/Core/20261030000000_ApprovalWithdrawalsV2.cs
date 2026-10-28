using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261030000000_ApprovalWithdrawalsV2")]
public sealed class ApprovalWithdrawalsV2:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_approval_withdraw_v2;
        REVOKE ALL ON SCHEMA cp6_approval_withdraw_v2 FROM PUBLIC;
        CREATE SEQUENCE cp6_approval_withdraw_v2.version AS bigint MINVALUE 1 NO CYCLE;
        CREATE TABLE cp6_approval_withdraw_v2.registrations(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,principal_id uuid NOT NULL,owner text COLLATE "C" NOT NULL,fact_owner text COLLATE "C" NOT NULL,
          revision uuid NOT NULL,enabled boolean NOT NULL,can_query boolean NOT NULL,can_relay boolean NOT NULL,evidence_mode text NOT NULL CHECK(evidence_mode IN('REAL','DEMO')),
          PRIMARY KEY(environment_id,tenant_id,principal_id),UNIQUE(environment_id,tenant_id,principal_id,owner));
        CREATE TABLE cp6_approval_withdraw_v2.subject_grants(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,principal_id uuid NOT NULL,owner text COLLATE "C" NOT NULL,submission_id uuid NOT NULL,
          revision uuid NOT NULL,enabled boolean NOT NULL,can_query boolean NOT NULL,can_relay boolean NOT NULL,
          PRIMARY KEY(environment_id,tenant_id,principal_id,owner,submission_id),
          FOREIGN KEY(environment_id,tenant_id,principal_id,owner) REFERENCES cp6_approval_withdraw_v2.registrations(environment_id,tenant_id,principal_id,owner),
          FOREIGN KEY(tenant_id,environment_id,owner,submission_id) REFERENCES cp6_approval_intake_v2.submissions(tenant_id,environment_id,owner,id));
        CREATE TABLE cp6_approval_withdraw_v2.submission_versions(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,owner text COLLATE "C" NOT NULL,submission_id uuid NOT NULL,
          row_version bytea NOT NULL DEFAULT int8send(nextval('cp6_approval_withdraw_v2.version')) CHECK(octet_length(row_version)=8),
          PRIMARY KEY(environment_id,tenant_id,owner,submission_id),
          FOREIGN KEY(tenant_id,environment_id,owner,submission_id) REFERENCES cp6_approval_intake_v2.submissions(tenant_id,environment_id,owner,id));
        INSERT INTO cp6_approval_withdraw_v2.submission_versions(environment_id,tenant_id,owner,submission_id)
          SELECT environment_id,tenant_id,owner,id FROM cp6_approval_intake_v2.submissions;
        CREATE FUNCTION cp6_approval_withdraw_v2.bump() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE sid uuid;own text;BEGIN
          IF TG_TABLE_SCHEMA='public' THEN
            UPDATE cp6_approval_withdraw_v2.submission_versions v SET row_version=int8send(nextval('cp6_approval_withdraw_v2.version'))
              FROM cp6_approval_start_v2.starts s WHERE s.instance_id=(CASE WHEN TG_TABLE_NAME='Wf_FlowInstance' THEN to_jsonb(NEW)->>'Id' ELSE to_jsonb(NEW)->>'InstanceId' END)::uuid
                AND (v.environment_id,v.tenant_id,v.owner,v.submission_id)=(s.environment_id,s.tenant_id,s.owner,s.submission_id);
            RETURN NEW;
          ELSIF TG_TABLE_SCHEMA='cp6_approval_intake_v2' THEN sid=NEW.id;own=NEW.owner;
          ELSIF TG_TABLE_SCHEMA='cp6_approval_start_v2' THEN sid=NEW.submission_id;own=NEW.owner;
          ELSE sid=NEW.submission_id;own=convert_from(NEW.payload,'UTF8')::jsonb->>'consumerOwner';
          END IF;
          INSERT INTO cp6_approval_withdraw_v2.submission_versions(environment_id,tenant_id,owner,submission_id)
            VALUES(NEW.environment_id,NEW.tenant_id,own,sid)
            ON CONFLICT(environment_id,tenant_id,owner,submission_id) DO UPDATE SET row_version=int8send(nextval('cp6_approval_withdraw_v2.version'));
          RETURN NEW;
        END $$;
        CREATE TRIGGER withdraw_submission_version AFTER INSERT ON cp6_approval_intake_v2.submissions FOR EACH ROW EXECUTE FUNCTION cp6_approval_withdraw_v2.bump();
        CREATE TRIGGER withdraw_start_version AFTER INSERT ON cp6_approval_start_v2.starts FOR EACH ROW EXECUTE FUNCTION cp6_approval_withdraw_v2.bump();
        CREATE TRIGGER withdraw_decision_version AFTER INSERT ON cp6_approval_decision_v2.decisions FOR EACH ROW EXECUTE FUNCTION cp6_approval_withdraw_v2.bump();
        CREATE TRIGGER withdraw_instance_version AFTER UPDATE ON public."Wf_FlowInstance" FOR EACH ROW EXECUTE FUNCTION cp6_approval_withdraw_v2.bump();
        CREATE TRIGGER withdraw_task_version AFTER INSERT OR UPDATE ON public."Wf_FlowTask" FOR EACH ROW EXECUTE FUNCTION cp6_approval_withdraw_v2.bump();
        CREATE FUNCTION cp6_approval_withdraw_v2.version_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND (to_jsonb(OLD)-'row_version') IS DISTINCT FROM (to_jsonb(NEW)-'row_version')
            THEN RAISE EXCEPTION 'WITHDRAW_SUBMISSION_IDENTITY_IMMUTABLE';END IF;
          NEW.row_version=int8send(nextval('cp6_approval_withdraw_v2.version'));RETURN NEW;
        END $$;
        CREATE TRIGGER withdraw_version_guard BEFORE INSERT OR UPDATE ON cp6_approval_withdraw_v2.submission_versions FOR EACH ROW EXECUTE FUNCTION cp6_approval_withdraw_v2.version_guard();
        CREATE TABLE cp6_approval_withdraw_v2.commands(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,owner text COLLATE "C" NOT NULL,id uuid NOT NULL,
          owner_command_id uuid NOT NULL,outbox_id uuid NOT NULL,submission_id uuid NOT NULL,instance_id uuid,
          payload bytea NOT NULL,digest text NOT NULL,raw bytea NOT NULL,raw_digest text NOT NULL,
          state text NOT NULL CHECK(state IN('RECEIVED','BLOCKED','WITHDRAWN','TERMINAL_CONFLICT')),active_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),
          response_json text,decision_id uuid,row_version bytea NOT NULL DEFAULT int8send(nextval('cp6_approval_withdraw_v2.version')) CHECK(octet_length(row_version)=8),
          PRIMARY KEY(environment_id,tenant_id,owner,id),UNIQUE(environment_id,tenant_id,owner,owner_command_id),UNIQUE(environment_id,tenant_id,owner,outbox_id),
          FOREIGN KEY(tenant_id,environment_id,owner,submission_id) REFERENCES cp6_approval_intake_v2.submissions(tenant_id,environment_id,owner,id),
          FOREIGN KEY(instance_id) REFERENCES public."Wf_FlowInstance"("Id"),
          CHECK(encode(sha256(payload),'hex')=digest),CHECK(encode(sha256(raw),'hex')=raw_digest));
        CREATE TABLE cp6_approval_withdraw_v2.attempts(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,owner text COLLATE "C" NOT NULL,command_id uuid NOT NULL,id uuid NOT NULL,
          state text NOT NULL CHECK(state IN('BLOCKED','WITHDRAWN','TERMINAL_CONFLICT')),result_json text NOT NULL,audit_json text NOT NULL,
          at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(environment_id,tenant_id,id),
          FOREIGN KEY(environment_id,tenant_id,owner,command_id) REFERENCES cp6_approval_withdraw_v2.commands(environment_id,tenant_id,owner,id));
        ALTER TABLE cp6_approval_decision_v2.decisions ALTER COLUMN command_id DROP NOT NULL;
        ALTER TABLE cp6_approval_decision_v2.decisions ADD COLUMN withdraw_command_id uuid;
        ALTER TABLE cp6_approval_decision_v2.decisions ADD COLUMN withdraw_owner text COLLATE "C";
        ALTER TABLE cp6_approval_decision_v2.decisions DROP CONSTRAINT decisions_decision_check;
        ALTER TABLE cp6_approval_decision_v2.decisions ADD CONSTRAINT decisions_decision_check CHECK(
          decision IN('APPROVED','REJECTED') AND command_id IS NOT NULL AND withdraw_command_id IS NULL AND withdraw_owner IS NULL
          OR decision='WITHDRAWN' AND command_id IS NULL AND withdraw_command_id IS NOT NULL AND withdraw_owner IS NOT NULL);
        ALTER TABLE cp6_approval_decision_v2.decisions ADD CONSTRAINT decision_withdraw_original
          FOREIGN KEY(environment_id,tenant_id,withdraw_owner,withdraw_command_id) REFERENCES cp6_approval_withdraw_v2.commands(environment_id,tenant_id,owner,id);
        CREATE FUNCTION cp6_approval_withdraw_v2.command_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP IN('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'WITHDRAW_COMMAND_PERMANENT';END IF;
          IF TG_OP='INSERT' THEN
            IF NEW.state<>'RECEIVED' OR NEW.active_xid<>pg_current_xact_id() OR NEW.response_json IS NOT NULL THEN RAISE EXCEPTION 'WITHDRAW_COMMAND_WRITER_REQUIRED';END IF;
          ELSE
            IF OLD.state IN('WITHDRAWN','TERMINAL_CONFLICT') OR NEW.active_xid<>pg_current_xact_id()
              OR (to_jsonb(OLD)-ARRAY['state','active_xid','response_json','decision_id','row_version','instance_id']) IS DISTINCT FROM
                 (to_jsonb(NEW)-ARRAY['state','active_xid','response_json','decision_id','row_version','instance_id'])
              OR OLD.state='BLOCKED' AND NEW.state<>'RECEIVED'
              OR OLD.state='RECEIVED' AND (OLD.active_xid<>pg_current_xact_id() OR NEW.state='RECEIVED')
              THEN RAISE EXCEPTION 'WITHDRAW_COMMAND_IMMUTABLE';END IF;
          END IF;
          NEW.row_version=int8send(nextval('cp6_approval_withdraw_v2.version'));RETURN NEW;
        END $$;
        CREATE TRIGGER withdraw_command_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_approval_withdraw_v2.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER withdraw_command_guard BEFORE INSERT OR UPDATE OR DELETE ON cp6_approval_withdraw_v2.commands FOR EACH ROW EXECUTE FUNCTION cp6_approval_withdraw_v2.command_guard();
        CREATE TRIGGER withdraw_command_truncate BEFORE TRUNCATE ON cp6_approval_withdraw_v2.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_approval_withdraw_v2.command_guard();
        CREATE TRIGGER withdraw_version_root BEFORE INSERT OR UPDATE ON cp6_approval_withdraw_v2.submission_versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER withdraw_version_permanent BEFORE DELETE OR TRUNCATE ON cp6_approval_withdraw_v2.submission_versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER withdraw_attempt_root BEFORE INSERT ON cp6_approval_withdraw_v2.attempts FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER withdraw_attempt_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_approval_withdraw_v2.attempts FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        DO $$ DECLARE tbl text;BEGIN FOREACH tbl IN ARRAY ARRAY['registrations','subject_grants'] LOOP
          EXECUTE format('CREATE TRIGGER withdraw_authority BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_approval_withdraw_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change()',tbl);
        END LOOP;END $$;
        CREATE FUNCTION cp6_approval_withdraw_v2.verify_decision(t uuid,e uuid,did uuid) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE d cp6_approval_decision_v2.decisions;w cp6_approval_withdraw_v2.commands;p jsonb;f jsonb;BEGIN
          SELECT * INTO d FROM cp6_approval_decision_v2.decisions WHERE tenant_id=t AND environment_id=e AND id=did;
          SELECT * INTO w FROM cp6_approval_withdraw_v2.commands WHERE tenant_id=t AND environment_id=e AND owner=d.withdraw_owner AND id=d.withdraw_command_id;
          p=convert_from(d.payload,'UTF8')::jsonb;f=convert_from(w.payload,'UTF8')::jsonb->'fact';
          IF d.id IS NULL OR w.id IS NULL OR w.state<>'WITHDRAWN' OR w.active_xid<>pg_current_xact_id()
            OR d.decision<>'WITHDRAWN' OR d.command_id IS NOT NULL OR d.submission_id<>w.submission_id OR d.instance_id<>w.instance_id
            OR p->>'decisionMode' IS DISTINCT FROM 'WITHDRAWAL' OR p->'terminalActor' IS DISTINCT FROM f->'actor' OR p->>'reason' IS DISTINCT FROM f->>'reason'
            OR p->'subject' IS DISTINCT FROM f->'subject' OR p#>'{policy,ref}' IS DISTINCT FROM f->'policyRef'
            OR p->>'decisionEvidenceSetRef' IS NOT NULL OR p->>'decisionEvidenceDigest' IS NOT NULL OR p->'satisfiedRequirementIds' IS DISTINCT FROM '[]'::jsonb
            OR d.evidence_json::jsonb->>'profile' IS DISTINCT FROM 'oa.app.withdrawal-evidence/2.0'
            OR d.evidence_json::jsonb->'command' IS DISTINCT FROM convert_from(w.payload,'UTF8')::jsonb
            OR d.evidence_json::jsonb->>'decisionId' IS DISTINCT FROM d.id::text
            OR NOT EXISTS(SELECT FROM public."Wf_FlowInstance" i WHERE i."Id"=d.instance_id AND i."TenantId"=t AND i."Creator"='oa.app/2.0' AND i."Status"=3)
            OR EXISTS(SELECT FROM public."Wf_FlowTask" x WHERE x."InstanceId"=d.instance_id AND x."Status" IN(0,4))
            OR EXISTS(SELECT FROM public."Wf_FlowToken" x WHERE x."InstanceId"=d.instance_id AND x."Status"=0)
            OR EXISTS(SELECT FROM public."Wf_FlowFormTo" x WHERE x."InstanceId"=d.instance_id AND x."Status"=0)
            OR NOT EXISTS(SELECT FROM public."Wf_FlowHistory" h WHERE h."InstanceId"=d.instance_id AND h."TenantId"=t AND h."Action"='withdraw'
              AND h."ActorId"::text=f#>>'{actor,actualHandlerId}')
            OR NOT EXISTS(SELECT FROM cp6_approval_decision_v2.outbox o WHERE o.tenant_id=t AND o.environment_id=e AND o.decision_id=did
              AND o.id=d.event_id AND o.submission_id=d.submission_id AND o.consumer_owner=w.owner AND o.payload=d.payload AND o.payload_digest=d.payload_digest)
            THEN RAISE EXCEPTION 'WITHDRAW_DECISION_EVIDENCE_REQUIRED';END IF;
        END $$;
        CREATE FUNCTION cp6_approval_withdraw_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE w cp6_approval_withdraw_v2.commands;p jsonb;a jsonb;BEGIN
          SELECT * INTO w FROM cp6_approval_withdraw_v2.commands WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND owner=NEW.owner AND id=NEW.id;
          p=convert_from(w.payload,'UTF8')::jsonb;a=w.response_json::jsonb;
          IF w.state='RECEIVED' OR w.response_json IS NULL OR w.active_xid<>pg_current_xact_id()
            OR p->>'commandId' IS DISTINCT FROM w.id::text OR p->>'ownerOutboxId' IS DISTINCT FROM w.outbox_id::text
            OR p#>>'{fact,ownerCommandId}' IS DISTINCT FROM w.owner_command_id::text OR p->>'submissionId' IS DISTINCT FROM w.submission_id::text
            OR a->>'state' IS DISTINCT FROM w.state OR a->>'commandId' IS DISTINCT FROM w.id::text OR a->>'decisionId' IS DISTINCT FROM w.decision_id::text
            OR NOT EXISTS(SELECT FROM cp6_approval_withdraw_v2.attempts x WHERE x.environment_id=w.environment_id AND x.tenant_id=w.tenant_id
              AND x.owner=w.owner AND x.command_id=w.id AND x.state=w.state AND x.result_json=w.response_json)
            THEN RAISE EXCEPTION 'WITHDRAW_COMMAND_RESULT_REQUIRED';END IF;
          IF w.state='WITHDRAWN' THEN PERFORM cp6_approval_withdraw_v2.verify_decision(w.tenant_id,w.environment_id,w.decision_id);
          ELSIF w.state='TERMINAL_CONFLICT' AND NOT EXISTS(SELECT FROM cp6_approval_decision_v2.decisions d WHERE d.environment_id=w.environment_id
            AND d.tenant_id=w.tenant_id AND d.submission_id=w.submission_id AND d.id=w.decision_id AND convert_from(d.payload,'UTF8')::jsonb->'resultRef'=a->'decisionRef')
            THEN RAISE EXCEPTION 'WITHDRAW_CONFLICT_ORIGINAL_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER withdraw_command_complete AFTER INSERT OR UPDATE ON cp6_approval_withdraw_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_withdraw_v2.complete();
        CREATE FUNCTION cp6_approval_withdraw_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_approval_decision_v2.ready() AND NOT EXISTS(SELECT FROM (VALUES
            ('commands','withdraw_command_root','cp6_authentication_v1.before_change()',62,false),
            ('commands','withdraw_command_guard','cp6_approval_withdraw_v2.command_guard()',31,false),
            ('commands','withdraw_command_truncate','cp6_approval_withdraw_v2.command_guard()',34,false),
            ('commands','withdraw_command_complete','cp6_approval_withdraw_v2.complete()',21,true),
            ('attempts','withdraw_attempt_root','cp6_authentication_v1.before_change()',6,false),
            ('attempts','withdraw_attempt_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('submission_versions','withdraw_version_root','cp6_authentication_v1.before_change()',22,false),
            ('submission_versions','withdraw_version_permanent','cp6_scope_authorization_v1.immutable()',42,false),
            ('submission_versions','withdraw_version_guard','cp6_approval_withdraw_v2.version_guard()',23,false),
            ('registrations','withdraw_authority','cp6_scope_authorization_v1.before_authority_change()',62,false),
            ('subject_grants','withdraw_authority','cp6_scope_authorization_v1.before_authority_change()',62,false)) a(tbl,name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_approval_withdraw_v2.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred))
            AND NOT EXISTS(SELECT FROM (VALUES('cp6_approval_intake_v2.submissions','withdraw_submission_version',5),('cp6_approval_start_v2.starts','withdraw_start_version',5),('cp6_approval_decision_v2.decisions','withdraw_decision_version',5),
              ('public."Wf_FlowInstance"','withdraw_instance_version',17),('public."Wf_FlowTask"','withdraw_task_version',21)) a(tbl,name,typ)
              WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tbl) AND t.tgname=a.name AND t.tgfoid='cp6_approval_withdraw_v2.bump()'::regprocedure AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        CREATE OR REPLACE FUNCTION cp6_approval_start_v2.runtime_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE i uuid;prior uuid;r jsonb;native boolean; BEGIN
          IF TG_OP='TRUNCATE' THEN
            IF EXISTS(SELECT FROM cp6_approval_start_v2.starts) THEN RAISE EXCEPTION 'APP_DECISION_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
            RETURN NULL;
          END IF;
          r=CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
          i=(CASE WHEN TG_TABLE_NAME='Wf_FlowInstance' THEN r->>'Id' ELSE r->>'InstanceId' END)::uuid;
          IF TG_OP='UPDATE' THEN prior=(CASE WHEN TG_TABLE_NAME='Wf_FlowInstance' THEN to_jsonb(OLD)->>'Id' ELSE to_jsonb(OLD)->>'InstanceId' END)::uuid; END IF;
          native=EXISTS(SELECT FROM cp6_approval_start_v2.starts WHERE instance_id=i OR instance_id=prior)
            OR (TG_OP<>'INSERT' AND TG_TABLE_NAME='Wf_FlowInstance' AND to_jsonb(OLD)->>'Creator'='oa.app/2.0');
          IF native THEN
            IF TG_OP='DELETE' OR (TG_OP='UPDATE' AND (prior<>i OR to_jsonb(OLD)->>'TenantId' IS DISTINCT FROM r->>'TenantId'))
              OR (NOT EXISTS(SELECT FROM cp6_approval_decision_v2.commands c WHERE c.instance_id=i
                AND c.tenant_id=(r->>'TenantId')::uuid AND c.writer_xid=pg_current_xact_id() AND NOT c.completed) AND NOT EXISTS(SELECT FROM cp6_approval_withdraw_v2.commands w WHERE w.instance_id=i AND w.tenant_id=(r->>'TenantId')::uuid AND w.active_xid=pg_current_xact_id() AND w.state='RECEIVED'))
              THEN RAISE EXCEPTION 'APP_DECISION_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
            IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_FlowInstance' AND
              (to_jsonb(OLD)-ARRAY['Status','CurrentNode','ModifyDate','Modifier','RowVersion']) IS DISTINCT FROM
              (r-ARRAY['Status','CurrentNode','ModifyDate','Modifier','RowVersion']) THEN
                RAISE EXCEPTION 'APP_INSTANCE_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
            IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_FlowTask' AND
              (to_jsonb(OLD)-ARRAY['Status','Comment','ModifyDate','Modifier','ApprovalRowVersion']) IS DISTINCT FROM
              (r-ARRAY['Status','Comment','ModifyDate','Modifier','ApprovalRowVersion']) THEN
                RAISE EXCEPTION 'APP_TASK_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
            IF TG_OP='UPDATE' AND TG_TABLE_NAME IN('Wf_FlowHistory','Wf_FlowData') THEN
                RAISE EXCEPTION 'APP_RUNTIME_FACT_IMMUTABLE' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
        END $$;

        CREATE OR REPLACE FUNCTION cp6_approval_decision_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_approval_decision_v2.commands;v cp6_approval_decision_v2.votes;d cp6_approval_decision_v2.decisions;
          inst public."Wf_FlowInstance";t public."Wf_FlowTask";a cp6_approval_tasks_v2.assignments; BEGIN
          IF TG_TABLE_NAME='decisions' AND (to_jsonb(NEW)->>'withdraw_command_id') IS NOT NULL THEN
            PERFORM cp6_approval_withdraw_v2.verify_decision(NEW.tenant_id,NEW.environment_id,NEW.id);RETURN NEW;END IF;
          SELECT * INTO c FROM cp6_approval_decision_v2.commands WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id
            AND id=(CASE WHEN TG_TABLE_NAME='commands' THEN to_jsonb(NEW)->>'id' ELSE to_jsonb(NEW)->>'command_id' END)::uuid;
          IF c.id IS NULL OR NOT c.completed OR c.writer_xid<>pg_current_xact_id() THEN
            RAISE EXCEPTION 'APP_DECISION_COMMAND_INCOMPLETE' USING ERRCODE='23514'; END IF;
          SELECT * INTO v FROM cp6_approval_decision_v2.votes WHERE tenant_id=c.tenant_id AND environment_id=c.environment_id AND command_id=c.id;
          SELECT * INTO inst FROM public."Wf_FlowInstance" WHERE "Id"=c.instance_id;
          SELECT * INTO t FROM public."Wf_FlowTask" WHERE "Id"=c.task_id;
          SELECT * INTO a FROM cp6_approval_tasks_v2.assignments WHERE task_id=c.task_id;
          IF v.id IS NULL OR inst."Creator"<>'oa.app/2.0' OR inst."TenantId"<>c.tenant_id
            OR t."InstanceId"<>c.instance_id OR t."TenantId"<>c.tenant_id OR t."AssigneeId"<>v.principal_id
            OR t."Status"<>(CASE WHEN v.approved THEN 1 ELSE 2 END) OR v.actual_id<>c.actor_id OR v.task_id<>c.task_id
            OR v.instance_id<>c.instance_id OR a.environment_id<>c.environment_id OR a.principal_id<>v.principal_id
            OR (v.body::jsonb->>'taskId')::uuid IS DISTINCT FROM c.task_id
            OR (v.body::jsonb->>'id')::uuid IS DISTINCT FROM v.id
            OR (v.body::jsonb->>'commandId')::uuid IS DISTINCT FROM c.id
            OR (v.body::jsonb->>'instanceId')::uuid IS DISTINCT FROM c.instance_id
            OR (v.body::jsonb->>'approved')::boolean IS DISTINCT FROM v.approved
            OR v.body::jsonb->'authority'->'assignment' IS DISTINCT FROM a.body::jsonb
            OR (v.body::jsonb->'authority'->>'actualHandlerId')::uuid IS DISTINCT FROM c.actor_id
            OR (v.body::jsonb->'authority'->>'principalId')::uuid IS DISTINCT FROM a.principal_id
            OR NOT EXISTS(SELECT FROM public."Wf_FlowHistory" h WHERE h."InstanceId"=c.instance_id AND h."TenantId"=c.tenant_id
              AND h."NodeId"=t."NodeId" AND h."ActorId"=c.actor_id AND h."Action"=CASE WHEN v.approved THEN 'approve' ELSE 'reject' END)
            THEN RAISE EXCEPTION 'APP_VOTE_EVIDENCE_REQUIRED' USING ERRCODE='23514'; END IF;
          IF EXISTS(SELECT FROM public."Wf_FlowTask" x WHERE x."InstanceId"=c.instance_id AND x."Status" IN(1,2)
            AND NOT EXISTS(SELECT FROM cp6_approval_decision_v2.votes y WHERE y.task_id=x."Id" AND y.tenant_id=c.tenant_id AND y.environment_id=c.environment_id))
            THEN RAISE EXCEPTION 'APP_VOTE_EVIDENCE_REQUIRED' USING ERRCODE='23514'; END IF;
          SELECT * INTO d FROM cp6_approval_decision_v2.decisions WHERE tenant_id=c.tenant_id AND environment_id=c.environment_id AND instance_id=c.instance_id;
          IF inst."Status" IN(1,2) THEN
            IF d.id IS NULL OR d.command_id<>c.id OR d.decision<>(CASE WHEN inst."Status"=1 THEN 'APPROVED' ELSE 'REJECTED' END)
              OR NOT EXISTS(SELECT FROM cp6_approval_decision_v2.outbox o WHERE o.tenant_id=c.tenant_id AND o.environment_id=c.environment_id
                AND o.decision_id=d.id AND o.id=d.event_id AND o.submission_id=d.submission_id AND o.payload=d.payload AND o.payload_digest=d.payload_digest)
              THEN RAISE EXCEPTION 'APP_DECISION_OUTBOX_REQUIRED' USING ERRCODE='23514'; END IF;
          ELSIF d.id IS NOT NULL THEN RAISE EXCEPTION 'APP_TERMINAL_STATE_MISMATCH' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Forward recovery preserves withdrawal facts and decisions.");
}
