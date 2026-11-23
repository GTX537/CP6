using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261123083000_OaConnectorResultsV1")]
public sealed class OaConnectorResultsV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_native_tasks_v2.commands DROP CONSTRAINT commands_operation_check;
        ALTER TABLE cp6_native_tasks_v2.commands ADD CHECK (operation IN(
            'OA_TASK_TRANSFER','OA_TASK_ADDSIGN','OA_TASK_SENDBACK','OA_APPROVER_RESOLVE','OA_TASK_TIMEOUT','OA_CONNECTOR_RESUME'));
        ALTER TABLE cp6_approval_decision_v2.decisions ADD COLUMN resume_command_id uuid
            UNIQUE REFERENCES cp6_native_tasks_v2.commands(id);
        ALTER TABLE cp6_approval_decision_v2.decisions DROP CONSTRAINT decisions_decision_check;
        ALTER TABLE cp6_approval_decision_v2.decisions ADD CONSTRAINT decisions_decision_check CHECK(
            decision IN('APPROVED','REJECTED') AND num_nonnulls(command_id,resume_command_id)=1
                AND withdraw_command_id IS NULL AND withdraw_owner IS NULL
            OR decision='WITHDRAWN' AND command_id IS NULL AND resume_command_id IS NULL
                AND withdraw_command_id IS NOT NULL AND withdraw_owner IS NOT NULL);
        CREATE TABLE cp6_oa_conn_v1.result_inbox (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, job_id uuid NOT NULL, id uuid NOT NULL,
            service_registration text NOT NULL, schema_ref text NOT NULL, proof_ref text NOT NULL,
            payload bytea NOT NULL CHECK(octet_length(payload) BETWEEN 1 AND 1048576),
            payload_sha256 text NOT NULL CHECK(payload_sha256=encode(sha256(payload),'hex')),
            mapping_sha256 text NOT NULL CHECK(mapping_sha256 ~ '^[0-9a-f]{64}$'),
            received_attempt integer NOT NULL CHECK(received_attempt>0),
            received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,environment_id,job_id),
            UNIQUE(tenant_id,environment_id,job_id,id),
            FOREIGN KEY(tenant_id,environment_id,job_id) REFERENCES cp6_oa_conn_v1.dispatch_admissions);
        CREATE TABLE cp6_oa_conn_v1.result_quarantine (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, job_id uuid NOT NULL,
            payload_sha256 text NOT NULL CHECK(payload_sha256 ~ '^[0-9a-f]{64}$'),
            payload bytea CHECK(octet_length(payload) BETWEEN 1 AND 1048576 AND payload_sha256=encode(sha256(payload),'hex')),
            reason text NOT NULL CHECK(reason IN('RESULT_IDENTITY_MISMATCH','RESULT_SCHEMA_INVALID','RESULT_CONFLICT','RESULT_SIZE_INVALID')),
            received_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,environment_id,job_id,payload_sha256,reason),
            FOREIGN KEY(tenant_id,environment_id,job_id) REFERENCES cp6_oa_conn_v1.dispatch_admissions);
        CREATE TABLE cp6_oa_conn_v1.result_applications (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, job_id uuid NOT NULL, result_id uuid NOT NULL,
            receipt_id uuid, decision_id uuid, attempt integer CHECK(attempt>0), output_vars jsonb,
            state text NOT NULL CHECK(state IN('APPLIED','STALE_TOKEN')),
            applied_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,environment_id,job_id),
            UNIQUE(tenant_id,environment_id,job_id,receipt_id),
            FOREIGN KEY(tenant_id,environment_id,job_id,result_id) REFERENCES cp6_oa_conn_v1.result_inbox(tenant_id,environment_id,job_id,id),
            FOREIGN KEY(tenant_id,environment_id,decision_id) REFERENCES cp6_approval_decision_v2.decisions,
            CHECK((state='APPLIED')=(receipt_id IS NOT NULL AND attempt IS NOT NULL AND output_vars IS NOT NULL AND jsonb_typeof(output_vars)='object')),
            CHECK(state='APPLIED' OR (receipt_id IS NULL AND decision_id IS NULL AND attempt IS NULL AND output_vars IS NULL)));
        CREATE VIEW cp6_oa_conn_v1.pending_result_jobs WITH (security_invoker=true) AS
            SELECT r.tenant_id,r.environment_id,r.job_id,r.received_at
            FROM cp6_oa_conn_v1.result_inbox r
            WHERE NOT EXISTS (
                SELECT FROM cp6_oa_conn_v1.result_applications a
                WHERE (a.tenant_id,a.environment_id,a.job_id)=(r.tenant_id,r.environment_id,r.job_id));
        CREATE INDEX approval_receipt_decision_projection ON cp6_approval_receipt_v2.streams(tenant_id,environment_id,decision_id);
        CREATE TABLE cp6_oa_conn_v1.result_outbox (
            tenant_id uuid NOT NULL, environment_id uuid NOT NULL, job_id uuid NOT NULL, receipt_id uuid NOT NULL,
            occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
            PRIMARY KEY(tenant_id,environment_id,job_id),
            FOREIGN KEY(tenant_id,environment_id,job_id,receipt_id) REFERENCES cp6_oa_conn_v1.result_applications(tenant_id,environment_id,job_id,receipt_id));
        DO $$
        DECLARE rel text;
        BEGIN
            FOREACH rel IN ARRAY ARRAY['result_inbox','result_quarantine','result_applications','result_outbox'] LOOP
                EXECUTE format('CREATE TRIGGER result_root BEFORE INSERT ON cp6_oa_conn_v1.%I
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',rel);
                EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_oa_conn_v1.%I
                    FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable()',rel);
            END LOOP;
        END $$;
        CREATE FUNCTION cp6_oa_conn_v1.app_job_transition(old_job jsonb,new_job jsonb) RETURNS boolean
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE before_state integer; after_state integer; before_attempt integer; after_attempt integer;
            old_until timestamptz; locks_same boolean; current_at timestamptz; pin jsonb;
        BEGIN
            -- 只放行本作业的队列元数据。流程、token 和原动作快照仍由 APP Writer 保护。
            pin=cp6_oa_conn_v1.action_ref(old_job->>'ActionRefJson')->'connectorPin';
            IF old_job->>'Kind' NOT IN ('webApi','deferredWebApiStart')
                OR jsonb_typeof(pin) IS DISTINCT FROM 'object'
                OR (old_job-ARRAY['Status','AttemptCount','LockedBy','LockedAtUtc','LockExpiresAtUtc','NextAttemptAtUtc',
                    'LastError','CompletedAtUtc','RowVersion','ModifyDate'])
                  IS DISTINCT FROM
                  (new_job-ARRAY['Status','AttemptCount','LockedBy','LockedAtUtc','LockExpiresAtUtc','NextAttemptAtUtc',
                    'LastError','CompletedAtUtc','RowVersion','ModifyDate'])
            THEN RETURN false; END IF;
            before_state=(old_job->>'Status')::integer; after_state=(new_job->>'Status')::integer;
            before_attempt=(old_job->>'AttemptCount')::integer; after_attempt=(new_job->>'AttemptCount')::integer;
            old_until=(old_job->>'LockExpiresAtUtc')::timestamptz;
            current_at=clock_timestamp();
            locks_same=(old_job->'LockedBy',old_job->'LockedAtUtc',old_job->'LockExpiresAtUtc')
                 IS NOT DISTINCT FROM (new_job->'LockedBy',new_job->'LockedAtUtc',new_job->'LockExpiresAtUtc');
            IF before_state=0 AND after_state=1 THEN
                RETURN before_attempt=after_attempt AND new_job->>'CompletedAtUtc' IS NULL
                  AND length(new_job->>'LockedBy')>0 AND (new_job->>'LockExpiresAtUtc')::timestamptz>current_at
                  AND (new_job->>'LockExpiresAtUtc')::timestamptz=(new_job->>'LockedAtUtc')::timestamptz+interval '300 seconds'
                  AND ((new_job->>'NextAttemptAtUtc')::timestamptz<=current_at OR EXISTS (
                    SELECT FROM cp6_oa_conn_v1.pending_result_jobs r
                    JOIN cp6_approval_runtime_v2.instances i
                      ON (i.tenant_id,i.environment_id)=(r.tenant_id,r.environment_id)
                    WHERE r.tenant_id=(old_job->>'TenantId')::uuid AND r.job_id=(old_job->>'Id')::uuid
                      AND i.instance_id=(old_job->>'InstanceId')::uuid AND r.received_at<=current_at))
                  AND (old_job->'NextAttemptAtUtc',old_job->'LastError') IS NOT DISTINCT FROM (new_job->'NextAttemptAtUtc',new_job->'LastError');
            ELSIF before_state=1 AND after_state=1 THEN
                RETURN locks_same AND old_until>current_at AND after_attempt=before_attempt+1 AND new_job->>'CompletedAtUtc' IS NULL
                  AND (old_job->'NextAttemptAtUtc',old_job->'LastError') IS NOT DISTINCT FROM (new_job->'NextAttemptAtUtc',new_job->'LastError');
            ELSIF before_state=1 AND after_state=0 THEN
                RETURN new_job->>'LockedBy' IS NULL AND new_job->>'LockedAtUtc' IS NULL AND new_job->>'LockExpiresAtUtc' IS NULL
                  AND new_job->>'CompletedAtUtc' IS NULL AND (
                    old_until<current_at AND before_attempt=after_attempt
                      AND (old_job->'NextAttemptAtUtc',old_job->'LastError') IS NOT DISTINCT FROM (new_job->'NextAttemptAtUtc',new_job->'LastError')
                    OR old_until>current_at AND after_attempt IN (before_attempt,before_attempt-1) AND after_attempt>=0
                      AND new_job->>'LastError' ~ '^[A-Z_]{1,80}$');
            ELSIF before_state=1 AND after_state=2 THEN
                RETURN locks_same AND old_until>current_at AND before_attempt=after_attempt
                  AND new_job->>'CompletedAtUtc' IS NOT NULL AND new_job->>'LastError' IS NULL
                  AND EXISTS(SELECT FROM cp6_oa_conn_v1.result_applications a
                    JOIN cp6_native_tasks_v2.commands n ON (n.tenant_id,n.environment_id,n.resource_id,n.id)=
                       (a.tenant_id,a.environment_id,a.job_id,a.receipt_id)
                    WHERE a.tenant_id=(old_job->>'TenantId')::uuid AND a.job_id=(old_job->>'Id')::uuid
                      AND a.state='APPLIED' AND a.attempt=before_attempt
                      AND n.operation='OA_CONNECTOR_RESUME' AND n.completed AND n.writer_xid=pg_current_xact_id());
            ELSIF before_state=1 AND after_state=4 THEN
                RETURN locks_same AND old_until>current_at AND before_attempt=after_attempt AND new_job->>'CompletedAtUtc' IS NOT NULL
                  AND NOT EXISTS(SELECT FROM public."Wf_FlowInstance" i JOIN public."Wf_FlowToken" k ON k."InstanceId"=i."Id"
                    WHERE i."TenantId"=(old_job->>'TenantId')::uuid AND i."Id"=(old_job->>'InstanceId')::uuid AND i."Status"=0
                      AND k."TenantId"=i."TenantId" AND k."Id"=(old_job->>'TokenId')::uuid AND k."NodeId"=old_job->>'NodeId' AND k."Status"=0);
            END IF;
            RETURN false;
        END $$;
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
                    IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_ServiceJob' AND cp6_oa_conn_v1.app_job_transition(to_jsonb(OLD),r)
                      THEN RETURN NEW; END IF;
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
        CREATE FUNCTION cp6_oa_conn_v1.verify_app_resume(t uuid,e uuid,did uuid) RETURNS void
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE d cp6_approval_decision_v2.decisions; p jsonb; BEGIN
            SELECT * INTO STRICT d FROM cp6_approval_decision_v2.decisions WHERE tenant_id=t AND environment_id=e AND id=did;
            p=convert_from(d.payload,'UTF8')::jsonb;
            IF NOT EXISTS (
                SELECT FROM cp6_native_tasks_v2.commands n
                JOIN cp6_oa_conn_v1.result_applications a ON (a.tenant_id,a.environment_id,a.job_id,a.receipt_id,a.decision_id)=
                    (n.tenant_id,n.environment_id,n.resource_id,n.id,d.id)
                JOIN cp6_oa_conn_v1.dispatch_admissions f ON (f.tenant_id,f.environment_id,f.job_id)=(a.tenant_id,a.environment_id,a.job_id)
                JOIN cp6_approval_runtime_v2.instances s ON (s.tenant_id,s.environment_id,s.instance_id)=(f.tenant_id,f.environment_id,f.instance_id)
                JOIN public."Wf_FlowInstance" i ON i."Id"=s.root_instance_id AND i."TenantId"=t
                WHERE n.id=d.resume_command_id AND n.completed AND NOT n.closed AND n.writer_xid=pg_current_xact_id()
                  AND n.operation='OA_CONNECTOR_RESUME' AND n.root_id=d.instance_id AND s.root_instance_id=d.instance_id
                  AND s.submission_id=d.submission_id AND n.tenant_id=t AND n.environment_id=e AND a.state='APPLIED'
                  AND i."Status"=CASE d.decision WHEN 'APPROVED' THEN 1 ELSE 2 END)
                OR p->>'decisionId' IS DISTINCT FROM d.id::text OR p->>'instanceId' IS DISTINCT FROM d.instance_id::text
                OR p->>'submissionId' IS DISTINCT FROM d.submission_id::text OR p->>'decision' IS DISTINCT FROM d.decision
                OR p->>'decisionMode' IS DISTINCT FROM 'HUMAN'
                OR p->'terminalActor'->>'executionId' IS DISTINCT FROM d.resume_command_id::text
                OR p->'terminalActor'->>'kind' IS DISTINCT FROM 'SYSTEM_EXECUTION'
                OR p->'terminalActor'->>'trigger' IS DISTINCT FROM 'RESUME'
                OR NOT EXISTS (
                    SELECT FROM cp6_approval_decision_v2.votes v
                    JOIN cp6_approval_runtime_v2.instances s ON (s.tenant_id,s.environment_id,s.instance_id)=(v.tenant_id,v.environment_id,v.instance_id)
                    WHERE (s.tenant_id,s.environment_id,s.root_instance_id)=(t,e,d.instance_id) AND v.approved=(d.decision='APPROVED'))
                OR EXISTS (
                    SELECT FROM cp6_approval_runtime_v2.instances s JOIN public."Wf_FlowInstance" i ON i."Id"=s.instance_id
                    WHERE s.root_instance_id=d.instance_id AND i."Status" NOT IN (1,2,3))
                OR EXISTS (
                    SELECT FROM cp6_approval_runtime_v2.instances s JOIN public."Wf_FlowToken" k ON k."InstanceId"=s.instance_id
                    WHERE s.root_instance_id=d.instance_id AND k."Status"=0)
                OR EXISTS (
                    SELECT FROM cp6_approval_runtime_v2.instances s JOIN public."Wf_FlowTask" k ON k."InstanceId"=s.instance_id
                    WHERE s.root_instance_id=d.instance_id AND k."Status" IN (0,4))
                OR NOT EXISTS (
                    SELECT FROM cp6_approval_decision_v2.outbox o WHERE (o.tenant_id,o.environment_id,o.decision_id,o.id,o.submission_id)=
                        (t,e,d.id,d.event_id,d.submission_id) AND o.payload=d.payload AND o.payload_digest=d.payload_digest)
            THEN RAISE EXCEPTION 'APP_RESUME_DECISION_INCOMPLETE' USING ERRCODE='23514'; END IF;
        END $$;
        CREATE OR REPLACE FUNCTION cp6_approval_decision_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
                DECLARE c cp6_approval_decision_v2.commands;v cp6_approval_decision_v2.votes;d cp6_approval_decision_v2.decisions;
                  inst public."Wf_FlowInstance";t public."Wf_FlowTask";a cp6_approval_tasks_v2.assignments;root uuid;rootstate integer; BEGIN
                  IF TG_TABLE_NAME='decisions' AND (to_jsonb(NEW)->>'withdraw_command_id') IS NOT NULL THEN
                    PERFORM cp6_approval_withdraw_v2.verify_decision(NEW.tenant_id,NEW.environment_id,NEW.id);RETURN NEW;END IF;
                  IF TG_TABLE_NAME='decisions' AND (to_jsonb(NEW)->>'resume_command_id') IS NOT NULL THEN
                    PERFORM cp6_oa_conn_v1.verify_app_resume(NEW.tenant_id,NEW.environment_id,NEW.id);RETURN NEW;END IF;
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
        CREATE FUNCTION cp6_oa_conn_v1.verify_result_application() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
            IF NEW.state='APPLIED' AND NOT EXISTS (
                SELECT FROM cp6_oa_conn_v1.result_outbox o
                JOIN public."Wf_ServiceJob" j ON j."TenantId"=o.tenant_id AND j."Id"=o.job_id
                WHERE (o.tenant_id,o.environment_id,o.job_id,o.receipt_id)=(NEW.tenant_id,NEW.environment_id,NEW.job_id,NEW.receipt_id)
                  AND j."Status"=2 AND j."AttemptCount"=NEW.attempt)
            THEN RAISE EXCEPTION 'CONNECTOR_RESUME_INCOMPLETE' USING ERRCODE='23514'; END IF;
            RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER result_application_complete AFTER INSERT ON cp6_oa_conn_v1.result_applications
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_oa_conn_v1.verify_result_application();
        CREATE FUNCTION cp6_oa_conn_v1.result_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
            SELECT cp6_oa_conn_v1.dispatch_ready() AND EXISTS (
                SELECT FROM pg_trigger WHERE tgrelid='cp6_oa_conn_v1.result_applications'::regclass
                  AND tgname='result_application_complete' AND tgfoid='cp6_oa_conn_v1.verify_result_application()'::regprocedure
                  AND tgenabled IN ('O','A') AND tgtype=5 AND tgdeferrable AND tginitdeferred AND NOT tgisinternal)
            AND NOT EXISTS (
                SELECT FROM unnest(ARRAY['result_inbox','result_quarantine','result_applications','result_outbox']) rel
                WHERE NOT EXISTS (SELECT FROM pg_trigger WHERE tgrelid=to_regclass('cp6_oa_conn_v1.'||rel)
                  AND tgname='immutable' AND tgfoid='cp6_scope_authorization_v1.immutable()'::regprocedure
                  AND tgenabled IN ('O','A') AND tgtype=58 AND NOT tgisinternal)
                OR NOT EXISTS (SELECT FROM pg_trigger WHERE tgrelid=to_regclass('cp6_oa_conn_v1.'||rel)
                  AND tgname='result_root' AND tgfoid='cp6_authentication_v1.before_change()'::regprocedure
                  AND tgenabled IN ('O','A') AND tgtype=6 AND NOT tgisinternal))
        $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("已接收连接器结果原件与回执，迁移仅支持前滚。");
}
