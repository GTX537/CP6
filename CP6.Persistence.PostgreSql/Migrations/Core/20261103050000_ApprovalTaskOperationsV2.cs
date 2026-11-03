using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261103050000_ApprovalTaskOperationsV2")]
public sealed class ApprovalTaskOperationsV2:Migration
{
            protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE cp6_native_tasks_v2.commands(
          id uuid PRIMARY KEY,tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,
          operation text COLLATE "C" NOT NULL CHECK(operation IN('OA_TASK_TRANSFER','OA_TASK_ADDSIGN','OA_TASK_SENDBACK','OA_APPROVER_RESOLVE')),
          resource_id uuid NOT NULL,command_key text COLLATE "C" NOT NULL,digest text NOT NULL,raw_body bytea NOT NULL,expected_version text,
          root_id uuid NOT NULL REFERENCES cp6_approval_start_v2.starts(instance_id),task_id uuid REFERENCES public."Wf_FlowTask"("Id"),
          result_json text,audit_json text,completed boolean NOT NULL DEFAULT false,closed boolean NOT NULL DEFAULT false,
          writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          UNIQUE(tenant_id,environment_id,actor_id,operation,resource_id,command_key));
        CREATE TABLE cp6_native_tasks_v2.app_assignment_changes(
          task_id uuid NOT NULL REFERENCES cp6_approval_tasks_v2.assignments(task_id),revision bigint NOT NULL CHECK(revision>0),
          command_id uuid NOT NULL UNIQUE REFERENCES cp6_native_tasks_v2.commands(id),body text NOT NULL,digest text NOT NULL,
          PRIMARY KEY(task_id,revision),CHECK(encode(sha256(convert_to(body,'UTF8')),'hex')=digest));
        CREATE TABLE cp6_native_tasks_v2.app_add_sign_links(
          task_id uuid PRIMARY KEY REFERENCES cp6_approval_tasks_v2.assignments(task_id) DEFERRABLE INITIALLY DEFERRED,
          parent_task_id uuid NOT NULL REFERENCES cp6_approval_tasks_v2.assignments(task_id),command_id uuid NOT NULL UNIQUE REFERENCES cp6_native_tasks_v2.commands(id),
          source text NOT NULL CHECK(source IN('before','after')),CHECK(task_id<>parent_task_id));
        CREATE TABLE cp6_native_tasks_v2.notification_outbox(
          id uuid PRIMARY KEY REFERENCES public."Wf_Notification"("Id") DEFERRABLE INITIALLY DEFERRED,command_id uuid NOT NULL REFERENCES cp6_native_tasks_v2.commands(id),
          recipient_id uuid NOT NULL,instance_id uuid NOT NULL REFERENCES public."Wf_FlowInstance"("Id"),task_id uuid REFERENCES public."Wf_FlowTask"("Id"),kind text NOT NULL,
          UNIQUE(command_id,kind,task_id,recipient_id));
        CREATE FUNCTION cp6_native_tasks_v2.operation_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            NEW.writer_xid=pg_current_xact_id();
            IF NEW.completed AND NOT NEW.closed THEN RAISE EXCEPTION 'TASK_OPERATION_INCOMPLETE' USING ERRCODE='23514';END IF;
          ELSE
            IF OLD.completed OR OLD.writer_xid<>pg_current_xact_id()
              OR (to_jsonb(OLD)-ARRAY['result_json','audit_json','completed']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['result_json','audit_json','completed'])
              THEN RAISE EXCEPTION 'TASK_OPERATION_IMMUTABLE' USING ERRCODE='23514';END IF;
          END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_native_tasks_v2.operation_fact() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_native_tasks_v2.commands;a cp6_approval_tasks_v2.assignments;prior jsonb;BEGIN
          SELECT * INTO c FROM cp6_native_tasks_v2.commands WHERE id=NEW.command_id AND writer_xid=pg_current_xact_id() AND NOT completed AND NOT closed;
          IF c.id IS NULL THEN RAISE EXCEPTION 'TASK_OPERATION_REQUIRED' USING ERRCODE='23514';END IF;
          IF TG_TABLE_NAME='app_assignment_changes' THEN
            SELECT * INTO a FROM cp6_approval_tasks_v2.assignments WHERE task_id=NEW.task_id;
            SELECT body::jsonb INTO prior FROM cp6_native_tasks_v2.app_assignment_changes WHERE task_id=NEW.task_id ORDER BY revision DESC LIMIT 1;
            prior=COALESCE(prior,a.body::jsonb);
            IF c.operation<>'OA_TASK_TRANSFER' OR c.task_id<>NEW.task_id OR a.tenant_id<>c.tenant_id OR a.environment_id<>c.environment_id
              OR NEW.revision<>(SELECT COALESCE(max(revision),0)+1 FROM cp6_native_tasks_v2.app_assignment_changes WHERE task_id=NEW.task_id)
              OR (NEW.body::jsonb->>'createdPrincipalId')::uuid IS DISTINCT FROM a.principal_id
              OR (NEW.body::jsonb-ARRAY['principalId','candidates','rule','assignedAt','createdPrincipalId','assignmentRevision']) IS DISTINCT FROM (prior-ARRAY['principalId','candidates','rule','assignedAt','createdPrincipalId','assignmentRevision'])
              THEN RAISE EXCEPTION 'TASK_ASSIGNMENT_IDENTITY_REQUIRED' USING ERRCODE='23514';END IF;
          ELSIF TG_TABLE_NAME='app_add_sign_links' THEN
            IF c.operation<>'OA_TASK_ADDSIGN' OR c.task_id<>NEW.parent_task_id
              THEN RAISE EXCEPTION 'TASK_ADDSIGN_COMMAND_REQUIRED' USING ERRCODE='23514';END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_native_tasks_v2.operation_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_native_tasks_v2.commands;BEGIN
          SELECT * INTO c FROM cp6_native_tasks_v2.commands WHERE id=NEW.id;
          IF NOT c.completed OR c.result_json IS NULL OR c.audit_json IS NULL OR c.writer_xid<>pg_current_xact_id()
            OR NOT EXISTS(SELECT FROM cp6_approval_runtime_v2.instances i WHERE i.instance_id=c.root_id AND i.tenant_id=c.tenant_id AND i.environment_id=c.environment_id)
            THEN RAISE EXCEPTION 'TASK_OPERATION_INCOMPLETE' USING ERRCODE='23514';END IF;
          IF c.closed THEN
            IF c.result_json::jsonb->>'outcome' IS DISTINCT FROM 'CLOSED'
              OR EXISTS(SELECT FROM cp6_native_tasks_v2.app_assignment_changes WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_native_tasks_v2.app_add_sign_links WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_native_tasks_v2.notification_outbox WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_native_tasks_v2.pending_resolutions WHERE source_command=c.id OR resolution_command=c.id)
              THEN RAISE EXCEPTION 'TASK_CLOSED_HAS_EFFECTS' USING ERRCODE='23514';END IF;RETURN NULL;
          END IF;
          IF c.operation='OA_TASK_TRANSFER' AND NOT EXISTS(SELECT FROM cp6_native_tasks_v2.app_assignment_changes a JOIN public."Wf_FlowTask" t ON t."Id"=a.task_id
              WHERE a.command_id=c.id AND a.task_id=c.task_id AND t."AssigneeId"=(a.body::jsonb->>'principalId')::uuid AND t."Status"=0)
            OR c.operation='OA_TASK_ADDSIGN' AND NOT EXISTS(SELECT FROM cp6_native_tasks_v2.app_add_sign_links WHERE command_id=c.id AND parent_task_id=c.task_id)
            OR c.operation='OA_TASK_SENDBACK' AND NOT EXISTS(SELECT FROM cp6_native_tasks_v2.notification_outbox WHERE command_id=c.id AND kind='TASK_SENT_BACK')
            OR c.operation='OA_APPROVER_RESOLVE' AND NOT EXISTS(SELECT FROM cp6_native_tasks_v2.pending_resolutions WHERE resolution_command=c.id AND state='RESOLVED')
            THEN RAISE EXCEPTION 'TASK_OPERATION_ATOMIC_REQUIRED' USING ERRCODE='23514';END IF;
          IF EXISTS(SELECT FROM cp6_native_tasks_v2.notification_outbox o LEFT JOIN public."Wf_Notification" n ON n."Id"=o.id
            WHERE o.command_id=c.id AND (n."Id" IS NULL OR n."TenantId"<>c.tenant_id OR n."UserId"<>o.recipient_id OR n."InstanceId"<>o.instance_id OR n."TaskId" IS DISTINCT FROM o.task_id))
            THEN RAISE EXCEPTION 'TASK_NOTIFICATION_ATOMIC_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER operation_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_native_tasks_v2.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER operation_no_delete BEFORE DELETE OR TRUNCATE ON cp6_native_tasks_v2.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER operation_guard BEFORE INSERT OR UPDATE ON cp6_native_tasks_v2.commands FOR EACH ROW EXECUTE FUNCTION cp6_native_tasks_v2.operation_guard();
        CREATE CONSTRAINT TRIGGER operation_complete AFTER INSERT OR UPDATE ON cp6_native_tasks_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_native_tasks_v2.operation_complete();
        CREATE OR REPLACE FUNCTION cp6_approval_start_v2.runtime_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
                DECLARE i uuid;prior uuid;r jsonb;native boolean;root uuid; BEGIN
                  IF TG_OP='TRUNCATE' THEN
                    IF EXISTS(SELECT FROM cp6_approval_start_v2.starts) THEN RAISE EXCEPTION 'APP_DECISION_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
                    RETURN NULL;
                  END IF;
                  IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_FlowInstance' AND to_jsonb(NEW)->>'Creator'='oa.app/2.0'
                    AND to_jsonb(OLD)->>'Creator' IS DISTINCT FROM 'oa.app/2.0' THEN RAISE EXCEPTION 'APP_DECISION_OWNER_REQUIRED' USING ERRCODE='23514';END IF;
                  r=CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
                  i=(CASE WHEN TG_TABLE_NAME='Wf_FlowInstance' THEN r->>'Id' ELSE r->>'InstanceId' END)::uuid;
                  IF TG_OP='UPDATE' THEN prior=(CASE WHEN TG_TABLE_NAME='Wf_FlowInstance' THEN to_jsonb(OLD)->>'Id' ELSE to_jsonb(OLD)->>'InstanceId' END)::uuid; END IF;
                  SELECT root_instance_id INTO root FROM cp6_approval_runtime_v2.instances WHERE instance_id=i;
                  IF root IS NULL THEN SELECT root_instance_id INTO root FROM cp6_approval_runtime_v2.children WHERE instance_id=i;END IF;
                  native=EXISTS(SELECT FROM cp6_approval_runtime_v2.instances WHERE instance_id=i OR instance_id=prior)
                    OR (TG_OP<>'INSERT' AND TG_TABLE_NAME='Wf_FlowInstance' AND to_jsonb(OLD)->>'Creator'='oa.app/2.0');
                  IF native THEN
                    IF TG_OP='DELETE' OR (TG_OP='UPDATE' AND (prior<>i OR to_jsonb(OLD)->>'TenantId' IS DISTINCT FROM r->>'TenantId'))
                      OR (NOT EXISTS(SELECT FROM cp6_approval_decision_v2.commands c WHERE c.instance_id IN(SELECT instance_id FROM cp6_approval_runtime_v2.instances WHERE root_instance_id=root)
                        AND c.tenant_id=(r->>'TenantId')::uuid AND c.writer_xid=pg_current_xact_id() AND NOT c.completed) AND NOT EXISTS(SELECT FROM cp6_approval_withdraw_v2.commands w WHERE w.instance_id=root AND w.tenant_id=(r->>'TenantId')::uuid AND w.active_xid=pg_current_xact_id() AND w.state='RECEIVED') AND NOT EXISTS(SELECT FROM cp6_native_tasks_v2.commands n WHERE n.root_id=root AND n.tenant_id=(r->>'TenantId')::uuid AND n.environment_id=(SELECT environment_id FROM cp6_approval_runtime_v2.instances WHERE instance_id=i) AND n.writer_xid=pg_current_xact_id() AND NOT n.completed AND NOT n.closed))
                      THEN RAISE EXCEPTION 'APP_DECISION_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
                    IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_FlowInstance' AND
                      (to_jsonb(OLD)-ARRAY['Status','CurrentNode','ModifyDate','Modifier','RowVersion','VarsJson']) IS DISTINCT FROM
                      (r-ARRAY['Status','CurrentNode','ModifyDate','Modifier','RowVersion','VarsJson']) THEN
                        RAISE EXCEPTION 'APP_INSTANCE_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
                    IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_FlowTask' AND
                      (to_jsonb(OLD)-ARRAY['Status','Comment','ModifyDate','Modifier','ApprovalRowVersion','AssigneeId']) IS DISTINCT FROM
                      (r-ARRAY['Status','Comment','ModifyDate','Modifier','ApprovalRowVersion','AssigneeId']) THEN
                        RAISE EXCEPTION 'APP_TASK_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
                    IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_FlowTask' AND to_jsonb(OLD)->>'AssigneeId' IS DISTINCT FROM r->>'AssigneeId'
                      AND NOT EXISTS(SELECT FROM cp6_native_tasks_v2.commands n JOIN cp6_native_tasks_v2.app_assignment_changes a ON a.command_id=n.id
                        WHERE n.root_id=root AND n.task_id=(r->>'Id')::uuid AND n.operation='OA_TASK_TRANSFER' AND n.writer_xid=pg_current_xact_id()
                          AND NOT n.completed AND NOT n.closed AND a.body::jsonb->>'principalId'=r->>'AssigneeId')
                      THEN RAISE EXCEPTION 'APP_TRANSFER_OWNER_REQUIRED' USING ERRCODE='23514';END IF;
                    IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_FlowToken' AND
                      ((to_jsonb(OLD)-ARRAY['Status','NodeId','StagePlanJson','ModifyDate','Modifier']) IS DISTINCT FROM
                       (r-ARRAY['Status','NodeId','StagePlanJson','ModifyDate','Modifier'])
                       OR (to_jsonb(OLD)->>'NodeId' IS DISTINCT FROM r->>'NodeId' AND EXISTS(
                         SELECT FROM cp6_approval_runtime_v2.children WHERE parent_token_id=(r->>'Id')::uuid)))
                      THEN RAISE EXCEPTION 'APP_TOKEN_LINEAGE_IMMUTABLE' USING ERRCODE='23514';END IF;
                    IF TG_OP='UPDATE' AND TG_TABLE_NAME IN('Wf_FlowHistory','Wf_FlowData') THEN
                        RAISE EXCEPTION 'APP_RUNTIME_FACT_IMMUTABLE' USING ERRCODE='23514'; END IF;
                  END IF;
                  RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
                END $$;
        CREATE OR REPLACE FUNCTION cp6_approval_tasks_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
                DECLARE instance uuid;f cp6_approval_tasks_v2.assignments;t public."Wf_FlowTask";s cp6_approval_runtime_v2.instances;b jsonb; BEGIN
                  IF TG_TABLE_NAME IN('assignments','children') THEN instance=NEW.instance_id;
                  ELSIF TG_TABLE_NAME='starts' THEN instance=NEW.instance_id;
                  ELSE instance=NEW."InstanceId"; END IF;
                  SELECT * INTO s FROM cp6_approval_runtime_v2.instances WHERE instance_id=instance;
                  IF NOT FOUND THEN
                    IF TG_TABLE_NAME IN('assignments','children') OR EXISTS(SELECT FROM public."Wf_FlowInstance" WHERE "Id"=instance AND "Creator"='oa.app/2.0') THEN
                      RAISE EXCEPTION 'APP_TASK_ASSIGNMENT_REQUIRED' USING ERRCODE='23514'; END IF;
                    RETURN NEW; END IF;
                  IF EXISTS(SELECT FROM cp6_approval_tasks_v2.assignments a WHERE a.instance_id=instance AND NOT EXISTS(
                    SELECT FROM public."Wf_FlowTask" x WHERE x."Id"=a.task_id AND x."InstanceId"=a.instance_id
                      AND x."TenantId"=a.tenant_id AND x."AssigneeId"=COALESCE((SELECT (body::jsonb->>'principalId')::uuid FROM cp6_native_tasks_v2.app_assignment_changes WHERE task_id=a.task_id ORDER BY revision DESC LIMIT 1),a.principal_id) AND x."TokenId"=a.token_id))
                    THEN RAISE EXCEPTION 'APP_TASK_ASSIGNMENT_MISMATCH' USING ERRCODE='23514'; END IF;
                  FOR t IN SELECT * FROM public."Wf_FlowTask" WHERE "InstanceId"=instance LOOP
                    SELECT * INTO f FROM cp6_approval_tasks_v2.assignments WHERE task_id=t."Id";
                    IF NOT FOUND THEN RAISE EXCEPTION 'APP_TASK_ASSIGNMENT_REQUIRED' USING ERRCODE='23514'; END IF;
                    SELECT COALESCE((SELECT body FROM cp6_native_tasks_v2.app_assignment_changes WHERE task_id=f.task_id ORDER BY revision DESC LIMIT 1),f.body) INTO f.body;
                    b=f.body::jsonb;f.principal_id=(b->>'principalId')::uuid;
                    SELECT COALESCE((SELECT digest FROM cp6_native_tasks_v2.app_assignment_changes WHERE task_id=f.task_id ORDER BY revision DESC LIMIT 1),f.digest) INTO f.digest;
                    IF f.tenant_id<>s.tenant_id OR f.environment_id<>s.environment_id OR f.submission_id<>s.submission_id
                      OR f.instance_id<>s.instance_id OR f.flow_version<>s.flow_version OR f.token_id IS DISTINCT FROM t."TokenId"
                      OR f.principal_id<>t."AssigneeId" OR t."TenantId"<>s.tenant_id
                      OR b->>'profile' IS DISTINCT FROM 'oa.app.assignment/2.0'
                      OR b->>'tenantId' IS DISTINCT FROM f.tenant_id::text OR b->>'environmentId' IS DISTINCT FROM f.environment_id::text
                      OR b->>'submissionId' IS DISTINCT FROM f.submission_id::text OR b->>'instanceId' IS DISTINCT FROM f.instance_id::text
                      OR b->>'taskId' IS DISTINCT FROM f.task_id::text OR b->>'tokenId' IS DISTINCT FROM f.token_id::text
                      OR b->>'flowVersionId' IS DISTINCT FROM f.flow_version::text OR b->>'principalId' IS DISTINCT FROM f.principal_id::text
                      OR (b->>'nodeId' COLLATE "C") IS DISTINCT FROM (t."NodeId"::text COLLATE "C")
                      OR (b->>'stageIndex')::integer IS DISTINCT FROM t."StageIndex" OR (b->>'stageRound')::integer IS DISTINCT FROM t."StageRound"
                      OR (b->>'countersign' COLLATE "C") IS DISTINCT FROM (t."Countersign"::text COLLATE "C")
                      OR jsonb_typeof(b->'candidates') IS DISTINCT FROM 'array'
                      OR (b->'candidates' @> jsonb_build_array(f.principal_id)) IS DISTINCT FROM true
                      OR jsonb_typeof(b->'rule') IS DISTINCT FROM 'object'
                      OR f.digest<>encode(sha256(convert_to(f.body,'UTF8')),'hex')
                      THEN RAISE EXCEPTION 'APP_TASK_ASSIGNMENT_MISMATCH' USING ERRCODE='23514'; END IF;
                  END LOOP;
                  RETURN NEW;
                END $$;
        CREATE OR REPLACE FUNCTION cp6_approval_decision_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
                DECLARE c cp6_approval_decision_v2.commands;v cp6_approval_decision_v2.votes;d cp6_approval_decision_v2.decisions;
                  inst public."Wf_FlowInstance";t public."Wf_FlowTask";a cp6_approval_tasks_v2.assignments;root uuid;rootstate integer; BEGIN
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
                  SELECT COALESCE((SELECT body FROM cp6_native_tasks_v2.app_assignment_changes WHERE task_id=c.task_id ORDER BY revision DESC LIMIT 1),a.body) INTO a.body;
                  a.principal_id=(a.body::jsonb->>'principalId')::uuid;
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
                  SELECT root_instance_id INTO root FROM cp6_approval_runtime_v2.instances WHERE instance_id=c.instance_id AND tenant_id=c.tenant_id AND environment_id=c.environment_id;
                  SELECT "Status" INTO rootstate FROM public."Wf_FlowInstance" WHERE "Id"=root;
                  IF root IS NULL THEN RAISE EXCEPTION 'APP_RUNTIME_ROOT_REQUIRED' USING ERRCODE='23514';END IF;
                  SELECT * INTO d FROM cp6_approval_decision_v2.decisions WHERE tenant_id=c.tenant_id AND environment_id=c.environment_id AND instance_id=root;
                  IF TG_TABLE_NAME='decisions' AND NEW.instance_id<>root THEN RAISE EXCEPTION 'APP_ROOT_DECISION_REQUIRED' USING ERRCODE='23514';END IF;
                  IF rootstate IN(1,2) AND (
                    EXISTS(SELECT FROM public."Wf_FlowInstance" x JOIN cp6_approval_runtime_v2.instances r ON r.instance_id=x."Id" WHERE r.root_instance_id=root AND x."Status" NOT IN(1,2,3))
                    OR EXISTS(SELECT FROM public."Wf_FlowToken" x JOIN cp6_approval_runtime_v2.instances r ON r.instance_id=x."InstanceId" WHERE r.root_instance_id=root AND x."Status"=0)
                    OR EXISTS(SELECT FROM public."Wf_FlowTask" x JOIN cp6_approval_runtime_v2.instances r ON r.instance_id=x."InstanceId" WHERE r.root_instance_id=root AND x."Status" IN(0,4)))
                    THEN RAISE EXCEPTION 'APP_ROOT_RUNTIME_INCOMPLETE' USING ERRCODE='23514';END IF;
                  IF rootstate IN(1,2) THEN
                    IF d.id IS NULL OR d.command_id<>c.id OR d.decision<>(CASE WHEN rootstate=1 THEN 'APPROVED' ELSE 'REJECTED' END)
                      OR NOT EXISTS(SELECT FROM cp6_approval_decision_v2.outbox o WHERE o.tenant_id=c.tenant_id AND o.environment_id=c.environment_id
                        AND o.decision_id=d.id AND o.id=d.event_id AND o.submission_id=d.submission_id AND o.payload=d.payload AND o.payload_digest=d.payload_digest)
                      THEN RAISE EXCEPTION 'APP_DECISION_OUTBOX_REQUIRED' USING ERRCODE='23514'; END IF;
                  ELSIF d.id IS NOT NULL THEN RAISE EXCEPTION 'APP_TERMINAL_STATE_MISMATCH' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
                END $$;
        CREATE OR REPLACE FUNCTION cp6_approval_runtime_v2.child_writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_approval_start_v2.starts WHERE instance_id=NEW.root_instance_id)
            AND NOT EXISTS(SELECT FROM cp6_approval_decision_v2.commands c JOIN cp6_approval_runtime_v2.instances r ON r.instance_id=c.instance_id
              WHERE r.root_instance_id=NEW.root_instance_id AND c.tenant_id=NEW.tenant_id AND c.environment_id=NEW.environment_id
                AND c.writer_xid=pg_current_xact_id() AND NOT c.completed)
            AND NOT EXISTS(SELECT FROM cp6_native_tasks_v2.commands c WHERE c.root_id=NEW.root_instance_id AND c.tenant_id=NEW.tenant_id
              AND c.environment_id=NEW.environment_id AND c.writer_xid=pg_current_xact_id() AND NOT c.completed AND NOT c.closed)
            THEN RAISE EXCEPTION 'APP_DECISION_OWNER_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE OR REPLACE FUNCTION cp6_native_tasks_v2.command_matches(k text,c uuid,t uuid,e uuid,r uuid) RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT CASE k
            WHEN 'SFS_COMMAND' THEN EXISTS(SELECT FROM cp6_personal_form_v2.commands x WHERE x.id=c AND x.tenant_id=t AND x.environment_id=e AND x.root_id=r AND x.writer_xid=pg_current_xact_id() AND NOT x.closed)
            WHEN 'APP_START' THEN EXISTS(SELECT FROM cp6_approval_start_v2.starts x WHERE x.engine_start_id=c AND x.tenant_id=t AND x.environment_id=e AND x.instance_id=r AND x.writer_xid=pg_current_xact_id())
            WHEN 'APP_COMMAND' THEN EXISTS(SELECT FROM cp6_approval_decision_v2.commands x JOIN cp6_approval_runtime_v2.instances i ON i.instance_id=x.instance_id
              WHERE x.id=c AND x.tenant_id=t AND x.environment_id=e AND i.root_instance_id=r AND x.writer_xid=pg_current_xact_id())
            WHEN 'APP_OPERATION' THEN EXISTS(SELECT FROM cp6_native_tasks_v2.commands x WHERE x.id=c AND x.tenant_id=t AND x.environment_id=e AND x.root_id=r AND x.writer_xid=pg_current_xact_id() AND NOT x.closed)
            ELSE false END;
        $$;
        ALTER TABLE cp6_native_tasks_v2.pending_resolutions DROP CONSTRAINT pending_resolutions_source_kind_check;
        ALTER TABLE cp6_native_tasks_v2.pending_resolutions ADD CHECK(source_kind IN('SFS_COMMAND','APP_START','APP_COMMAND','APP_OPERATION'));
        CREATE TRIGGER app_assignment_changes_root BEFORE INSERT ON cp6_native_tasks_v2.app_assignment_changes FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER app_assignment_changes_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_native_tasks_v2.app_assignment_changes FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER app_assignment_changes_fact BEFORE INSERT ON cp6_native_tasks_v2.app_assignment_changes FOR EACH ROW EXECUTE FUNCTION cp6_native_tasks_v2.operation_fact();
        CREATE TRIGGER app_add_sign_links_root BEFORE INSERT ON cp6_native_tasks_v2.app_add_sign_links FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER app_add_sign_links_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_native_tasks_v2.app_add_sign_links FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER app_add_sign_links_fact BEFORE INSERT ON cp6_native_tasks_v2.app_add_sign_links FOR EACH ROW EXECUTE FUNCTION cp6_native_tasks_v2.operation_fact();
        CREATE TRIGGER notification_outbox_root BEFORE INSERT ON cp6_native_tasks_v2.notification_outbox FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER notification_outbox_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_native_tasks_v2.notification_outbox FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER notification_outbox_fact BEFORE INSERT ON cp6_native_tasks_v2.notification_outbox FOR EACH ROW EXECUTE FUNCTION cp6_native_tasks_v2.operation_fact();
        ALTER FUNCTION cp6_native_tasks_v2.ready() RENAME TO ready_before_operations;
        CREATE FUNCTION cp6_native_tasks_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_native_tasks_v2.ready_before_operations() AND NOT EXISTS(SELECT FROM (VALUES
        ('cp6_native_tasks_v2.app_assignment_changes','app_assignment_changes_root','cp6_authentication_v1.before_change()',6,false),
        ('cp6_native_tasks_v2.app_assignment_changes','app_assignment_changes_immutable','cp6_scope_authorization_v1.immutable()',58,false),
        ('cp6_native_tasks_v2.app_assignment_changes','app_assignment_changes_fact','cp6_native_tasks_v2.operation_fact()',7,false),
        ('cp6_native_tasks_v2.app_add_sign_links','app_add_sign_links_root','cp6_authentication_v1.before_change()',6,false),
        ('cp6_native_tasks_v2.app_add_sign_links','app_add_sign_links_immutable','cp6_scope_authorization_v1.immutable()',58,false),
        ('cp6_native_tasks_v2.app_add_sign_links','app_add_sign_links_fact','cp6_native_tasks_v2.operation_fact()',7,false),
        ('cp6_native_tasks_v2.notification_outbox','notification_outbox_root','cp6_authentication_v1.before_change()',6,false),
        ('cp6_native_tasks_v2.notification_outbox','notification_outbox_immutable','cp6_scope_authorization_v1.immutable()',58,false),
        ('cp6_native_tasks_v2.notification_outbox','notification_outbox_fact','cp6_native_tasks_v2.operation_fact()',7,false),
        ('cp6_native_tasks_v2.commands','operation_root','cp6_authentication_v1.before_change()',62,false),
        ('cp6_native_tasks_v2.commands','operation_no_delete','cp6_scope_authorization_v1.immutable()',42,false),
        ('cp6_native_tasks_v2.commands','operation_guard','cp6_native_tasks_v2.operation_guard()',23,false),
        ('cp6_native_tasks_v2.commands','operation_complete','cp6_native_tasks_v2.operation_complete()',21,true)) a(rel,name,fn,typ,deferred) WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.rel) AND t.tgname=a.name
             AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0
             AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;

        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_native_tasks_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_native_tasks_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Native task operation receipts require forward recovery.");
}
