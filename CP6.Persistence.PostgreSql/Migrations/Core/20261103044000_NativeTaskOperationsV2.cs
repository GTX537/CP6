using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261103044000_NativeTaskOperationsV2")]
public sealed class NativeTaskOperationsV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_personal_form_v2.execution_cycles(
          id uuid PRIMARY KEY,root_id uuid NOT NULL REFERENCES cp6_personal_form_v2.instances(instance_id) DEFERRABLE INITIALLY DEFERRED,
          cycle_number bigint NOT NULL CHECK(cycle_number>0),command_id uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.commands(id),
          return_revision bigint,created_at timestamptz NOT NULL DEFAULT clock_timestamp(),UNIQUE(root_id,cycle_number),UNIQUE(root_id,return_revision));
        -- Exact native creation facts identify the initial cycle. No legacy Draft or guessed return record is fabricated.
        INSERT INTO cp6_personal_form_v2.execution_cycles(id,root_id,cycle_number,command_id)
          SELECT root_id,root_id,1,command_id FROM cp6_personal_form_v2.submissions WHERE root_id IS NOT NULL;
        CREATE TABLE cp6_personal_form_v2.returns(
          id uuid PRIMARY KEY,instance_id uuid NOT NULL REFERENCES cp6_personal_form_v2.instances(instance_id),revision bigint NOT NULL CHECK(revision>0),
          command_id uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.commands(id),execution_cycle_id uuid NOT NULL REFERENCES cp6_personal_form_v2.execution_cycles(id),
          form_version uuid NOT NULL,flow_version uuid NOT NULL,form_data_version bytea NOT NULL CHECK(octet_length(form_data_version)=8),
          snapshot_json text NOT NULL,snapshot_digest text NOT NULL,reason text NOT NULL,actual_id uuid NOT NULL,
          returned_at timestamptz NOT NULL DEFAULT clock_timestamp(),UNIQUE(instance_id,revision),UNIQUE(instance_id,execution_cycle_id));
        CREATE TABLE cp6_personal_form_v2.returned_history(
          command_id uuid PRIMARY KEY REFERENCES cp6_personal_form_v2.commands(id),instance_id uuid NOT NULL,return_revision bigint NOT NULL,
          before_json text NOT NULL,after_json text NOT NULL,form_data_version bytea NOT NULL,instance_version bytea NOT NULL,
          FOREIGN KEY(instance_id,return_revision) REFERENCES cp6_personal_form_v2.returns(instance_id,revision),
          CHECK(octet_length(form_data_version)=8 AND octet_length(instance_version)=8));
        CREATE TABLE cp6_personal_form_v2.assignment_changes(
          task_id uuid NOT NULL REFERENCES cp6_personal_form_v2.assignments(task_id),revision bigint NOT NULL CHECK(revision>0),
          command_id uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.commands(id),body text NOT NULL,PRIMARY KEY(task_id,revision));
        CREATE TABLE cp6_personal_form_v2.add_sign_links(
          task_id uuid PRIMARY KEY REFERENCES cp6_personal_form_v2.assignments(task_id),parent_task_id uuid NOT NULL REFERENCES cp6_personal_form_v2.assignments(task_id),
          command_id uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.commands(id),source text NOT NULL CHECK(source IN('before','after')),CHECK(task_id<>parent_task_id));
        CREATE FUNCTION cp6_personal_form_v2.assignment_change_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r jsonb;c cp6_personal_form_v2.commands;a cp6_personal_form_v2.assignments;BEGIN
          r=to_jsonb(NEW);SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=(r->>'command_id')::uuid AND writer_xid=pg_current_xact_id() AND NOT completed;
          SELECT * INTO a FROM cp6_personal_form_v2.assignments WHERE task_id=c.task_id;
          IF c.id IS NULL OR a.task_id IS NULL OR TG_TABLE_NAME='assignment_changes' AND (c.operation<>'OA_TASK_TRANSFER' OR NEW.task_id<>c.task_id)
             OR TG_TABLE_NAME='add_sign_links' AND (c.operation<>'OA_TASK_ADDSIGN' OR (r->>'parent_task_id')::uuid<>c.task_id)
             THEN RAISE EXCEPTION 'TASK_ASSIGNMENT_COMMAND_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_personal_form_v2.returned_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r jsonb;c cp6_personal_form_v2.commands;BEGIN
          r=to_jsonb(NEW);SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=(r->>'command_id')::uuid AND writer_xid=pg_current_xact_id() AND NOT completed;
          IF c.id IS NULL OR c.root_id IS DISTINCT FROM COALESCE((r->>'root_id')::uuid,(r->>'instance_id')::uuid)
             OR TG_TABLE_NAME='returns' AND (c.operation<>'OA_TASK_SENDBACK' OR c.actor_id<>(r->>'actual_id')::uuid)
             OR TG_TABLE_NAME='returned_history' AND c.operation NOT IN('SFS_RETURNED_EDIT','SFS_RETURNED_RESUBMIT')
             OR TG_TABLE_NAME='execution_cycles' AND c.operation NOT IN('FORM_DIRECT_SUBMIT','FORM_DRAFT_SUBMIT','SFS_RETURNED_RESUBMIT')
             THEN RAISE EXCEPTION 'RETURNED_COMMAND_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_personal_form_v2.returned_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;r public."Wf_FlowInstance";f public."Wf_FormData";h cp6_personal_form_v2.returned_history;BEGIN
          SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.id;
          IF c.closed THEN
            IF EXISTS(SELECT FROM cp6_personal_form_v2.returns WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_personal_form_v2.execution_cycles WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_personal_form_v2.returned_history WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_personal_form_v2.assignment_changes WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_personal_form_v2.add_sign_links WHERE command_id=c.id)
              THEN RAISE EXCEPTION 'RETURNED_CLOSED_HAS_EFFECTS' USING ERRCODE='23514';END IF;RETURN NULL;
          END IF;
          IF c.operation='OA_TASK_TRANSFER' AND (NOT EXISTS(SELECT FROM cp6_personal_form_v2.assignment_changes a JOIN public."Wf_FlowTask" t ON t."Id"=a.task_id
            WHERE a.command_id=c.id AND a.task_id=c.task_id AND (a.body::jsonb->>'principalId')::uuid=t."AssigneeId")
            OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.outbox WHERE command_id=c.id AND kind='TASK_TRANSFERRED'))
            THEN RAISE EXCEPTION 'TRANSFER_ATOMIC_RESULT_REQUIRED' USING ERRCODE='23514';END IF;
          IF c.operation='OA_TASK_ADDSIGN' AND NOT EXISTS(SELECT FROM cp6_personal_form_v2.add_sign_links a JOIN cp6_personal_form_v2.assignments t ON t.task_id=a.task_id
            WHERE a.command_id=c.id AND a.parent_task_id=c.task_id AND t.command_id=c.id)
            THEN RAISE EXCEPTION 'ADDSIGN_ATOMIC_RESULT_REQUIRED' USING ERRCODE='23514';END IF;
          IF c.operation IN('SFS_RETURNED_EDIT','SFS_RETURNED_RESUBMIT') THEN
            SELECT * INTO r FROM public."Wf_FlowInstance" WHERE "Id"=c.root_id;
            SELECT * INTO f FROM public."Wf_FormData" WHERE "Id"=c.form_data_id;
            SELECT * INTO h FROM cp6_personal_form_v2.returned_history WHERE command_id=c.id;
            IF h.command_id IS NULL OR h.instance_id<>c.root_id OR h.after_json IS DISTINCT FROM f."DataJson"
              OR f."DataJson" IS DISTINCT FROM r."VarsJson" OR h.form_data_version IS DISTINCT FROM f."RowVersion" OR h.instance_version IS DISTINCT FROM r."RowVersion"
              OR f."SubmittedBy" IS DISTINCT FROM c.actor_id OR r."StarterId" IS DISTINCT FROM c.actor_id
              OR c.operation='SFS_RETURNED_EDIT' AND (r."Status"<>5 OR EXISTS(SELECT FROM cp6_personal_form_v2.execution_cycles WHERE command_id=c.id))
              OR c.operation='SFS_RETURNED_RESUBMIT' AND (r."Status"=5 OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.execution_cycles WHERE command_id=c.id AND root_id=c.root_id AND return_revision=h.return_revision))
              THEN RAISE EXCEPTION 'RETURNED_ATOMIC_RESULT_REQUIRED' USING ERRCODE='23514';END IF;
          END IF;
          IF c.operation='OA_TASK_SENDBACK' THEN
            SELECT * INTO r FROM public."Wf_FlowInstance" WHERE "Id"=c.root_id;
            IF r."Status"=5 AND NOT EXISTS(SELECT FROM cp6_personal_form_v2.returns WHERE command_id=c.id AND instance_id=c.root_id)
              OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.outbox WHERE command_id=c.id AND kind='TASK_SENT_BACK')
              THEN RAISE EXCEPTION 'SENDBACK_ATOMIC_RESULT_REQUIRED' USING ERRCODE='23514';END IF;
          END IF;RETURN NULL;
        END $$;
        CREATE OR REPLACE FUNCTION cp6_personal_form_v2.runtime_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r jsonb;prior jsonb;i uuid;root uuid;t uuid;e uuid;data uuid;managed boolean;BEGIN
         IF TG_OP='TRUNCATE' THEN
           IF EXISTS(SELECT FROM cp6_personal_form_v2.submissions) THEN RAISE EXCEPTION 'FORM_RUNTIME_OWNER_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
         END IF;
         r=CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
                 IF TG_OP='UPDATE' THEN
                   prior=to_jsonb(OLD);
                   IF TG_TABLE_NAME NOT IN('Wf_FormData','Wf_FlowInstance') AND prior->>'InstanceId' IS DISTINCT FROM r->>'InstanceId'
                     AND EXISTS(SELECT FROM cp6_personal_form_v2.instances WHERE instance_id=(prior->>'InstanceId')::uuid)
                     THEN RAISE EXCEPTION 'FORM_RUNTIME_IDENTITY_IMMUTABLE' USING ERRCODE='23514';END IF;
                 END IF;
         IF TG_TABLE_NAME='Wf_FormData' THEN
           SELECT s.root_id,s.tenant_id,s.environment_id,s.form_data_id INTO root,t,e,data FROM cp6_personal_form_v2.submissions s WHERE s.form_data_id=(r->>'Id')::uuid;
           managed=data IS NOT NULL OR EXISTS(SELECT FROM cp6_form_authoring_v2.versions v WHERE v.tenant_id=(r->>'TenantId')::uuid AND v.id=(r->>'FormDefVersionId')::uuid);
         ELSE
           i=(CASE WHEN TG_TABLE_NAME='Wf_FlowInstance' THEN r->>'Id' ELSE r->>'InstanceId' END)::uuid;
           SELECT x.root_id,x.tenant_id,x.environment_id,x.form_data_id INTO root,t,e,data FROM cp6_personal_form_v2.instances x WHERE x.instance_id=i;
           managed=data IS NOT NULL OR TG_TABLE_NAME='Wf_FlowInstance' AND (r->>'Creator'='oa.form/2.0' OR prior->>'Creator'='oa.form/2.0'
             OR EXISTS(SELECT FROM cp6_personal_form_v2.submissions s WHERE s.form_data_id=(r->>'FormDataId')::uuid)
             OR r->>'Creator' IS DISTINCT FROM 'oa.app/2.0' AND EXISTS(SELECT FROM cp6_workflow_authoring_v2.versions v WHERE v.id=(r->>'FlowDefVersionId')::uuid AND v.tenant_id=(r->>'TenantId')::uuid));
           IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_FlowInstance' AND prior->>'Creator' IS DISTINCT FROM r->>'Creator'
              AND (prior->>'Creator'='oa.form/2.0' OR r->>'Creator'='oa.form/2.0') THEN RAISE EXCEPTION 'FORM_RUNTIME_IDENTITY_IMMUTABLE' USING ERRCODE='23514';END IF;
         END IF;
         IF managed THEN
           IF TG_OP='DELETE' OR data IS NULL OR (r->>'TenantId')::uuid<>t OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.commands c
             WHERE c.tenant_id=t AND c.environment_id=e AND c.form_data_id=data AND c.writer_xid=pg_current_xact_id() AND NOT c.completed)
             THEN RAISE EXCEPTION 'FORM_RUNTIME_OWNER_REQUIRED' USING ERRCODE='23514';END IF;
           IF TG_OP='UPDATE' THEN
             IF TG_TABLE_NAME='Wf_FormData' AND (NOT EXISTS(SELECT FROM cp6_personal_form_v2.commands c
                 WHERE c.tenant_id=t AND c.environment_id=e AND c.form_data_id=data AND c.root_id=root AND c.operation IN('SFS_RETURNED_EDIT','SFS_RETURNED_RESUBMIT')
                   AND c.writer_xid=pg_current_xact_id() AND NOT c.completed)
                 OR (prior-ARRAY['DataJson','ModifyDate','Modifier','RowVersion']) IS DISTINCT FROM (r-ARRAY['DataJson','ModifyDate','Modifier','RowVersion']))
               OR TG_TABLE_NAME IN('Wf_FlowHistory','Wf_FlowData')
               OR TG_TABLE_NAME='Wf_FlowInstance' AND (prior-ARRAY['Status','CurrentNode','ModifyDate','Modifier','RowVersion','VarsJson']) IS DISTINCT FROM (r-ARRAY['Status','CurrentNode','ModifyDate','Modifier','RowVersion','VarsJson'])
               OR TG_TABLE_NAME='Wf_FlowTask' AND ((prior-ARRAY['Status','Comment','ModifyDate','Modifier','ApprovalRowVersion','AssigneeId']) IS DISTINCT FROM (r-ARRAY['Status','Comment','ModifyDate','Modifier','ApprovalRowVersion','AssigneeId'])
                 OR prior->>'AssigneeId' IS DISTINCT FROM r->>'AssigneeId' AND NOT EXISTS(SELECT FROM cp6_personal_form_v2.assignment_changes a JOIN cp6_personal_form_v2.commands c ON c.id=a.command_id
                   WHERE a.task_id=(r->>'Id')::uuid AND c.operation='OA_TASK_TRANSFER' AND c.writer_xid=pg_current_xact_id() AND NOT c.completed AND a.body::jsonb->>'principalId'=r->>'AssigneeId'))
               OR TG_TABLE_NAME='Wf_FlowToken' AND ((prior-ARRAY['Status','NodeId','StagePlanJson','ModifyDate','Modifier']) IS DISTINCT FROM (r-ARRAY['Status','NodeId','StagePlanJson','ModifyDate','Modifier'])
                 OR prior->>'NodeId' IS DISTINCT FROM r->>'NodeId' AND EXISTS(SELECT FROM cp6_personal_form_v2.instances WHERE parent_token_id=(r->>'Id')::uuid))
               THEN RAISE EXCEPTION 'FORM_RUNTIME_IDENTITY_IMMUTABLE' USING ERRCODE='23514';END IF;
           END IF;
         END IF;RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
        END $$;
        CREATE TRIGGER returned_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.execution_cycles FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER returned_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.execution_cycles FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER returned_guard BEFORE INSERT ON cp6_personal_form_v2.execution_cycles FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.returned_guard();
        CREATE TRIGGER returned_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.returns FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER returned_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.returns FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER returned_guard BEFORE INSERT ON cp6_personal_form_v2.returns FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.returned_guard();
        CREATE TRIGGER returned_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.returned_history FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER returned_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.returned_history FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER returned_guard BEFORE INSERT ON cp6_personal_form_v2.returned_history FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.returned_guard();
        CREATE CONSTRAINT TRIGGER returned_complete AFTER INSERT OR UPDATE ON cp6_personal_form_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.returned_complete();
        CREATE OR REPLACE FUNCTION cp6_personal_form_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;s cp6_personal_form_v2.submissions;d cp6_personal_form_v2.drafts;r public."Wf_FlowInstance";BEGIN
         SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=NEW.id;
         IF NOT c.completed OR c.result_json IS NULL OR c.audit_json IS NULL OR c.writer_xid<>pg_current_xact_id()
            THEN RAISE EXCEPTION 'FORM_COMMAND_INCOMPLETE' USING ERRCODE='23514';END IF;
         IF c.closed THEN
           IF c.draft_id IS NOT NULL OR c.form_data_id IS NOT NULL OR c.root_id IS NOT NULL OR c.task_id IS NOT NULL
              OR c.result_json::jsonb->>'outcome' IS DISTINCT FROM 'CLOSED'
              OR EXISTS(SELECT FROM cp6_personal_form_v2.history WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_personal_form_v2.submissions WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_personal_form_v2.assignments WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_personal_form_v2.votes WHERE command_id=c.id)
              OR EXISTS(SELECT FROM cp6_personal_form_v2.outbox WHERE command_id=c.id)
             THEN RAISE EXCEPTION 'FORM_CLOSED_COMMAND_HAS_EFFECTS' USING ERRCODE='23514';END IF;
           RETURN NULL;
         END IF;
         IF EXISTS(SELECT FROM cp6_personal_form_v2.outbox o LEFT JOIN public."Wf_Notification" n ON n."Id"=o.notification_id
            WHERE o.command_id=c.id AND (n."Id" IS NULL OR n."TenantId"<>c.tenant_id OR n."UserId"<>o.recipient_id
              OR n."Type"<>(o.body::jsonb->>'type')::int OR n."TaskId" IS DISTINCT FROM o.task_id
              OR n."InstanceId" IS DISTINCT FROM (o.body::jsonb->>'instance')::uuid
              OR n."InAppRequested" IS DISTINCT FROM (o.body::jsonb->>'inApp')::boolean
              OR n."EmailRequested" IS DISTINCT FROM (o.body::jsonb->>'email')::boolean))
           THEN RAISE EXCEPTION 'FORM_NOTIFICATION_OUTBOX_REQUIRED' USING ERRCODE='23514';END IF;
         IF c.draft_id IS NOT NULL THEN
           SELECT * INTO d FROM cp6_personal_form_v2.drafts WHERE id=c.draft_id;
           IF d.id IS NULL OR (d.tenant_id,d.environment_id,d.owner_id) IS DISTINCT FROM (c.tenant_id,c.environment_id,c.actor_id)
             OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.history h WHERE h.command_id=c.id AND h.draft_id=d.id AND h.after_json::jsonb=to_jsonb(d))
             THEN RAISE EXCEPTION 'FORM_DRAFT_HISTORY_REQUIRED' USING ERRCODE='23514';END IF;
         END IF;
         IF c.operation IN('FORM_DIRECT_SUBMIT','FORM_DRAFT_SUBMIT') THEN
           SELECT * INTO s FROM cp6_personal_form_v2.submissions WHERE command_id=c.id;
           IF s.form_data_id IS NULL OR s.form_data_id<>c.form_data_id OR s.root_id IS DISTINCT FROM c.root_id
              OR (s.tenant_id,s.environment_id,s.actor_id,s.draft_id) IS DISTINCT FROM (c.tenant_id,c.environment_id,c.actor_id,c.draft_id)
              OR NOT EXISTS(SELECT FROM public."Wf_FormData" f JOIN cp6_form_authoring_v2.versions v ON v.tenant_id=s.tenant_id AND v.environment_id=s.environment_id AND v.id=s.form_version
                WHERE f."Id"=s.form_data_id AND f."TenantId"=s.tenant_id AND f."FormDefVersionId"=s.form_version AND f."SubmittedBy"=c.actor_id
                  AND f."FormVersion"=v.version_number AND f."FormKey"::text COLLATE "C"=(v.snapshot_json::jsonb->>'key') COLLATE "C")
              OR c.draft_id IS NOT NULL AND (d.status<>1 OR d.form_data_id IS DISTINCT FROM s.form_data_id)
              OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.outbox WHERE command_id=c.id AND kind='FORM_SUBMITTED' AND recipient_id=c.actor_id)
              THEN RAISE EXCEPTION 'FORM_SUBMISSION_ATOMIC_REQUIRED' USING ERRCODE='23514';END IF;
         END IF;
         IF c.root_id IS NOT NULL THEN
           SELECT * INTO r FROM public."Wf_FlowInstance" WHERE "Id"=c.root_id;
           IF r."Id" IS NULL OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.instances WHERE instance_id=c.root_id AND root_id=c.root_id AND form_data_id=c.form_data_id)
             OR EXISTS(SELECT FROM public."Wf_FlowTask" t JOIN cp6_personal_form_v2.instances x ON x.instance_id=t."InstanceId" WHERE x.root_id=c.root_id
               AND NOT EXISTS(SELECT FROM cp6_personal_form_v2.assignments a WHERE a.task_id=t."Id" AND a.instance_id=x.instance_id
                 AND (COALESCE((SELECT body FROM cp6_personal_form_v2.assignment_changes WHERE task_id=a.task_id ORDER BY revision DESC LIMIT 1),a.body)::jsonb->>'principalId')::uuid=t."AssigneeId" AND (a.body::jsonb->>'tokenId')::uuid=t."TokenId"
                 AND (a.body::jsonb->>'flowVersionId')::uuid=x.flow_version))
             OR EXISTS(SELECT FROM cp6_personal_form_v2.assignments a WHERE a.command_id=c.id AND NOT EXISTS(SELECT FROM cp6_personal_form_v2.outbox o WHERE o.command_id=c.id AND o.task_id=a.task_id AND o.kind='TASK_ASSIGNED'))
             THEN RAISE EXCEPTION 'FORM_TASK_GRAPH_REQUIRED' USING ERRCODE='23514';END IF;
           IF r."Status" IN(1,2,3) AND (EXISTS(SELECT FROM public."Wf_FlowInstance" i JOIN cp6_personal_form_v2.instances x ON x.instance_id=i."Id" WHERE x.root_id=c.root_id AND i."Status" NOT IN(1,2,3))
             OR EXISTS(SELECT FROM public."Wf_FlowTask" t JOIN cp6_personal_form_v2.instances x ON x.instance_id=t."InstanceId" WHERE x.root_id=c.root_id AND t."Status" IN(0,4))
             OR EXISTS(SELECT FROM public."Wf_FlowToken" k JOIN cp6_personal_form_v2.instances x ON x.instance_id=k."InstanceId" WHERE x.root_id=c.root_id AND k."Status"=0))
             THEN RAISE EXCEPTION 'FORM_ROOT_INCOMPLETE' USING ERRCODE='23514';END IF;
         END IF;
         IF c.operation='FORM_TASK_DECISION' AND (NOT EXISTS(SELECT FROM cp6_personal_form_v2.votes v JOIN public."Wf_FlowTask" t ON t."Id"=v.task_id
           WHERE v.command_id=c.id AND v.task_id=c.task_id AND t."Status"=CASE WHEN (v.body::jsonb->>'approved')::boolean THEN 1 ELSE 2 END)
           OR NOT EXISTS(SELECT FROM cp6_personal_form_v2.outbox WHERE command_id=c.id AND kind='TASK_DECIDED'))
           THEN RAISE EXCEPTION 'FORM_DECISION_ATOMIC_REQUIRED' USING ERRCODE='23514';END IF;
         RETURN NEW;
        END $$;
        CREATE TRIGGER assignment_change_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.assignment_changes FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER assignment_change_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.assignment_changes FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER assignment_change_guard BEFORE INSERT ON cp6_personal_form_v2.assignment_changes FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.assignment_change_guard();
        CREATE TRIGGER assignment_change_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.add_sign_links FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER assignment_change_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.add_sign_links FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER assignment_change_guard BEFORE INSERT ON cp6_personal_form_v2.add_sign_links FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.assignment_change_guard();
        REVOKE ALL ON cp6_personal_form_v2.assignment_changes,cp6_personal_form_v2.add_sign_links FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_personal_form_v2.assignment_change_guard() FROM PUBLIC;
        ALTER TABLE cp6_workflow_read_v2.facts DROP CONSTRAINT facts_pkey;
        ALTER TABLE cp6_workflow_read_v2.facts ADD PRIMARY KEY(tenant_id,environment_id,kind,target_id,principal_id);
        CREATE OR REPLACE FUNCTION cp6_workflow_read_v2.command_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE f cp6_workflow_read_v2.facts;b jsonb; BEGIN
          b=NEW.result_json::jsonb;
          IF b->>'commandId' IS DISTINCT FROM NEW.command_key OR b->>'state' IS DISTINCT FROM (CASE WHEN NEW.closed THEN 'CLOSED' ELSE 'READ' END)
            THEN RAISE EXCEPTION 'INBOX_READ_RESULT_MISMATCH' USING ERRCODE='23514'; END IF;
          IF NOT NEW.closed THEN
            SELECT * INTO f FROM cp6_workflow_read_v2.facts WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND kind=NEW.kind AND target_id=NEW.target_id AND principal_id=(b->'result'->>'principalId')::uuid;
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
        CREATE FUNCTION cp6_personal_form_v2.returned_ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
        SELECT cp6_personal_form_v2.ready() AND NOT EXISTS(SELECT FROM (VALUES
          ('cp6_personal_form_v2.execution_cycles','returned_root','cp6_authentication_v1.before_change()',62,false),
          ('cp6_personal_form_v2.execution_cycles','returned_immutable','cp6_scope_authorization_v1.immutable()',58,false),
          ('cp6_personal_form_v2.execution_cycles','returned_guard','cp6_personal_form_v2.returned_guard()',7,false),
          ('cp6_personal_form_v2.returns','returned_root','cp6_authentication_v1.before_change()',62,false),
          ('cp6_personal_form_v2.returns','returned_immutable','cp6_scope_authorization_v1.immutable()',58,false),
          ('cp6_personal_form_v2.returns','returned_guard','cp6_personal_form_v2.returned_guard()',7,false),
          ('cp6_personal_form_v2.returned_history','returned_root','cp6_authentication_v1.before_change()',62,false),
          ('cp6_personal_form_v2.returned_history','returned_immutable','cp6_scope_authorization_v1.immutable()',58,false),
          ('cp6_personal_form_v2.returned_history','returned_guard','cp6_personal_form_v2.returned_guard()',7,false),
          ('cp6_personal_form_v2.assignment_changes','assignment_change_root','cp6_authentication_v1.before_change()',62,false),
          ('cp6_personal_form_v2.assignment_changes','assignment_change_immutable','cp6_scope_authorization_v1.immutable()',58,false),
          ('cp6_personal_form_v2.assignment_changes','assignment_change_guard','cp6_personal_form_v2.assignment_change_guard()',7,false),
          ('cp6_personal_form_v2.add_sign_links','assignment_change_root','cp6_authentication_v1.before_change()',62,false),
          ('cp6_personal_form_v2.add_sign_links','assignment_change_immutable','cp6_scope_authorization_v1.immutable()',58,false),
          ('cp6_personal_form_v2.add_sign_links','assignment_change_guard','cp6_personal_form_v2.assignment_change_guard()',7,false),
          ('cp6_personal_form_v2.commands','returned_complete','cp6_personal_form_v2.returned_complete()',21,true)) a(tbl,name,fn,typ,deferred) WHERE NOT EXISTS(SELECT FROM pg_trigger t
          WHERE t.tgrelid=to_regclass(a.tbl) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ
            AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        REVOKE ALL ON FUNCTION cp6_personal_form_v2.returned_ready() FROM PUBLIC;
        REVOKE ALL ON cp6_personal_form_v2.execution_cycles,cp6_personal_form_v2.returns,cp6_personal_form_v2.returned_history FROM PUBLIC;
        REVOKE ALL ON FUNCTION cp6_personal_form_v2.returned_guard(),cp6_personal_form_v2.returned_complete() FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Native returned facts require forward recovery.");
}
