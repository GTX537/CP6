using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261028160000_ApprovalDecisionsV2")]
public sealed class ApprovalDecisionsV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_approval_decision_v2;
        CREATE SEQUENCE cp6_approval_decision_v2.task_version AS bigint MINVALUE 1 NO CYCLE;
        -- This is a new optimistic concurrency token, not a reconstructed historical vote.
        ALTER TABLE public."Wf_FlowTask" ADD COLUMN "ApprovalRowVersion" bytea NOT NULL
          DEFAULT int8send(nextval('cp6_approval_decision_v2.task_version')) CHECK(octet_length("ApprovalRowVersion")=8);
        CREATE FUNCTION cp6_approval_decision_v2.task_version() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN NEW."ApprovalRowVersion"=int8send(nextval('cp6_approval_decision_v2.task_version')); RETURN NEW; END $$;
        CREATE TRIGGER approval_task_version BEFORE INSERT OR UPDATE ON public."Wf_FlowTask"
          FOR EACH ROW EXECUTE FUNCTION cp6_approval_decision_v2.task_version();

        CREATE TABLE cp6_approval_decision_v2.commands(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,instance_id uuid NOT NULL,task_id uuid NOT NULL,
          actor_id uuid NOT NULL,request_body text NOT NULL,request_digest text NOT NULL,
          writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),completed boolean NOT NULL DEFAULT false,
          result_json text,audit_json text,occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,environment_id,id),UNIQUE(tenant_id,environment_id,task_id),
          FOREIGN KEY(instance_id) REFERENCES public."Wf_FlowInstance"("Id"),
          FOREIGN KEY(task_id) REFERENCES public."Wf_FlowTask"("Id"),
          CHECK(encode(sha256(convert_to(request_body,'UTF8')),'hex')=request_digest),
          CHECK(NOT completed OR (result_json IS NOT NULL AND audit_json IS NOT NULL)));
        CREATE TABLE cp6_approval_decision_v2.votes(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,command_id uuid NOT NULL,
          instance_id uuid NOT NULL,task_id uuid NOT NULL,principal_id uuid NOT NULL,actual_id uuid NOT NULL,
          approved boolean NOT NULL,body text NOT NULL,digest text NOT NULL,occurred_at timestamptz NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),UNIQUE(tenant_id,environment_id,task_id),
          FOREIGN KEY(tenant_id,environment_id,command_id) REFERENCES cp6_approval_decision_v2.commands,
          FOREIGN KEY(task_id) REFERENCES cp6_approval_tasks_v2.assignments(task_id),
          CHECK(encode(sha256(convert_to(body,'UTF8')),'hex')=digest));
        CREATE TABLE cp6_approval_decision_v2.decisions(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,command_id uuid NOT NULL,
          instance_id uuid NOT NULL,submission_id uuid NOT NULL,event_id uuid NOT NULL,
          decision text NOT NULL CHECK(decision IN('APPROVED','REJECTED')),
          evidence_json text NOT NULL,evidence_digest text NOT NULL,payload bytea NOT NULL,payload_digest text NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),UNIQUE(tenant_id,environment_id,instance_id),
          UNIQUE(tenant_id,environment_id,event_id),
          FOREIGN KEY(tenant_id,environment_id,command_id) REFERENCES cp6_approval_decision_v2.commands,
          CHECK(encode(sha256(convert_to(evidence_json,'UTF8')),'hex')=evidence_digest),
          CHECK(encode(sha256(payload),'hex')=payload_digest));
        CREATE TABLE cp6_approval_decision_v2.outbox(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,id uuid NOT NULL,decision_id uuid NOT NULL,
          submission_id uuid NOT NULL,consumer_owner text COLLATE "C" NOT NULL,sequence bigint NOT NULL CHECK(sequence=1),
          payload bytea NOT NULL,payload_digest text NOT NULL,
          PRIMARY KEY(tenant_id,environment_id,id),UNIQUE(tenant_id,environment_id,decision_id,consumer_owner),
          FOREIGN KEY(tenant_id,environment_id,decision_id) REFERENCES cp6_approval_decision_v2.decisions,
          CHECK(encode(sha256(payload),'hex')=payload_digest));

        CREATE FUNCTION cp6_approval_decision_v2.command_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.writer_xid<>pg_current_xact_id() OR NEW.completed THEN
              RAISE EXCEPTION 'APP_DECISION_WRITER_REQUIRED' USING ERRCODE='23514'; END IF;
          ELSIF TG_OP='UPDATE' THEN
            IF OLD.completed OR NOT NEW.completed OR OLD.writer_xid<>pg_current_xact_id()
              OR (to_jsonb(OLD)-ARRAY['completed','result_json','audit_json']) IS DISTINCT FROM
                 (to_jsonb(NEW)-ARRAY['completed','result_json','audit_json']) THEN
              RAISE EXCEPTION 'APP_COMMAND_IMMUTABLE' USING ERRCODE='23514'; END IF;
          ELSE RAISE EXCEPTION 'APP_COMMAND_IMMUTABLE' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER decision_command_guard BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_approval_decision_v2.commands
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER decision_command_row BEFORE INSERT OR UPDATE OR DELETE ON cp6_approval_decision_v2.commands
          FOR EACH ROW EXECUTE FUNCTION cp6_approval_decision_v2.command_guard();
        CREATE TRIGGER decision_command_truncate BEFORE TRUNCATE ON cp6_approval_decision_v2.commands
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_approval_decision_v2.command_guard();
        DO $$ DECLARE tbl text; BEGIN
          FOREACH tbl IN ARRAY ARRAY['votes','decisions','outbox'] LOOP
            EXECUTE format('CREATE TRIGGER decision_root BEFORE INSERT ON cp6_approval_decision_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
            EXECUTE format('CREATE TRIGGER decision_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_approval_decision_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',tbl);
          END LOOP;
        END $$;

        -- Preserve the manifest/function identity used by TxE; authorize only this actual
        -- transaction's incomplete command for this exact native instance and tenant.
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
              OR NOT EXISTS(SELECT FROM cp6_approval_decision_v2.commands c WHERE c.instance_id=i
                AND c.tenant_id=(r->>'TenantId')::uuid AND c.writer_xid=pg_current_xact_id() AND NOT c.completed)
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

        CREATE FUNCTION cp6_approval_decision_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_approval_decision_v2.commands;v cp6_approval_decision_v2.votes;d cp6_approval_decision_v2.decisions;
          inst public."Wf_FlowInstance";t public."Wf_FlowTask";a cp6_approval_tasks_v2.assignments; BEGIN
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
        CREATE CONSTRAINT TRIGGER decision_command_complete AFTER INSERT OR UPDATE ON cp6_approval_decision_v2.commands
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_decision_v2.complete();
        CREATE CONSTRAINT TRIGGER decision_vote_complete AFTER INSERT ON cp6_approval_decision_v2.votes
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_decision_v2.complete();
        CREATE CONSTRAINT TRIGGER decision_terminal_complete AFTER INSERT ON cp6_approval_decision_v2.decisions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_decision_v2.complete();
        CREATE FUNCTION cp6_approval_decision_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_approval_tasks_v2.ready() AND NOT EXISTS(SELECT FROM (VALUES
            ('public."Wf_FlowTask"','approval_task_version','cp6_approval_decision_v2.task_version()',23,false),
            ('cp6_approval_decision_v2.commands','decision_command_guard','cp6_authentication_v1.before_change()',62,false),
            ('cp6_approval_decision_v2.commands','decision_command_row','cp6_approval_decision_v2.command_guard()',31,false),
            ('cp6_approval_decision_v2.commands','decision_command_truncate','cp6_approval_decision_v2.command_guard()',34,false),
            ('cp6_approval_decision_v2.commands','decision_command_complete','cp6_approval_decision_v2.complete()',21,true),
            ('cp6_approval_decision_v2.votes','decision_vote_complete','cp6_approval_decision_v2.complete()',5,true),
            ('cp6_approval_decision_v2.decisions','decision_terminal_complete','cp6_approval_decision_v2.complete()',5,true)
          ) a(tbl,name,fn,typ,deferred) WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tbl) AND t.tgname=a.name
            AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
            AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred))
          AND NOT EXISTS(SELECT FROM (VALUES('votes'),('decisions'),('outbox')) a(tbl)
            CROSS JOIN(VALUES('decision_root','cp6_authentication_v1.before_change()',6),('decision_immutable','cp6_scope_authorization_v1.immutable()',58)) b(name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_approval_decision_v2.'||a.tbl)
              AND t.tgname=b.name AND t.tgfoid=to_regprocedure(b.fn) AND t.tgtype=b.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward recovery only; original votes and decisions are permanent.");
}
