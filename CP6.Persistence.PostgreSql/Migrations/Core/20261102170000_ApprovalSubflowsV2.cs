using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102170000_ApprovalSubflowsV2")]
public sealed class ApprovalSubflowsV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_approval_runtime_v2;
        REVOKE ALL ON SCHEMA cp6_approval_runtime_v2 FROM PUBLIC;
        CREATE TABLE cp6_approval_runtime_v2.children(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,instance_id uuid PRIMARY KEY,
          root_instance_id uuid NOT NULL,parent_instance_id uuid NOT NULL,parent_token_id uuid NOT NULL,
          parent_node text COLLATE "C" NOT NULL,sub_index integer NOT NULL CHECK(sub_index>=0),
          flow_head uuid NOT NULL,flow_version uuid NOT NULL,
          CHECK(instance_id<>root_instance_id AND instance_id<>parent_instance_id),
          UNIQUE(parent_token_id,sub_index),
          FOREIGN KEY(instance_id) REFERENCES public."Wf_FlowInstance"("Id") DEFERRABLE INITIALLY DEFERRED,
          FOREIGN KEY(root_instance_id) REFERENCES cp6_approval_start_v2.starts(instance_id) DEFERRABLE INITIALLY DEFERRED,
          FOREIGN KEY(parent_instance_id) REFERENCES public."Wf_FlowInstance"("Id") DEFERRABLE INITIALLY DEFERRED,
          FOREIGN KEY(parent_token_id) REFERENCES public."Wf_FlowToken"("Id") DEFERRABLE INITIALLY DEFERRED,
          FOREIGN KEY(tenant_id,environment_id,flow_head,flow_version) REFERENCES cp6_workflow_authoring_v2.versions(tenant_id,environment_id,head_id,id));
        CREATE INDEX children_root ON cp6_approval_runtime_v2.children(tenant_id,environment_id,root_instance_id);
        CREATE FUNCTION cp6_approval_runtime_v2.child_writer() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_approval_start_v2.starts WHERE instance_id=NEW.root_instance_id)
            AND NOT EXISTS(SELECT FROM cp6_approval_decision_v2.commands c JOIN cp6_approval_runtime_v2.instances r ON r.instance_id=c.instance_id
              WHERE r.root_instance_id=NEW.root_instance_id AND c.tenant_id=NEW.tenant_id AND c.environment_id=NEW.environment_id
                AND c.writer_xid=pg_current_xact_id() AND NOT c.completed)
            THEN RAISE EXCEPTION 'APP_DECISION_OWNER_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER runtime_child_writer BEFORE INSERT ON cp6_approval_runtime_v2.children FOR EACH ROW EXECUTE FUNCTION cp6_approval_runtime_v2.child_writer();
        CREATE TRIGGER runtime_child_root BEFORE INSERT ON cp6_approval_runtime_v2.children FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER runtime_child_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_approval_runtime_v2.children FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        -- Existing roots keep their original starts. No reconstructed child or historical evidence.
        CREATE VIEW cp6_approval_runtime_v2.instances AS
          SELECT tenant_id,environment_id,owner,submission_id,instance_id,flow_head,flow_version,instance_id AS root_instance_id FROM cp6_approval_start_v2.starts
          UNION ALL
          SELECT c.tenant_id,c.environment_id,s.owner,s.submission_id,c.instance_id,c.flow_head,c.flow_version,c.root_instance_id
            FROM cp6_approval_runtime_v2.children c JOIN cp6_approval_start_v2.starts s
              ON s.instance_id=c.root_instance_id AND s.tenant_id=c.tenant_id AND s.environment_id=c.environment_id;
        ALTER TABLE cp6_approval_tasks_v2.assignments DROP CONSTRAINT assignments_instance_id_fkey;
        ALTER TABLE cp6_approval_tasks_v2.assignments ADD CONSTRAINT assignment_native_instance
          FOREIGN KEY(instance_id) REFERENCES public."Wf_FlowInstance"("Id") DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_approval_runtime_v2.form_pin(t uuid,e uuid,v uuid) RETURNS uuid LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT (d->>'version')::uuid FROM cp6_workflow_authoring_v2.versions p,
            LATERAL jsonb_array_elements(p.snapshot_json::jsonb->'dependencyRefs') d
            WHERE p.tenant_id=t AND p.environment_id=e AND p.id=v AND d->>'kind'='FORM' AND d->>'location'='formKey';
        $$;
        CREATE FUNCTION cp6_approval_runtime_v2.child_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_approval_runtime_v2.children;i public."Wf_FlowInstance";p cp6_approval_runtime_v2.instances;
          s cp6_approval_start_v2.starts;source jsonb;form uuid;BEGIN
          SELECT * INTO c FROM cp6_approval_runtime_v2.children WHERE instance_id=NEW.instance_id;
          SELECT * INTO i FROM public."Wf_FlowInstance" WHERE "Id"=c.instance_id;
          SELECT * INTO p FROM cp6_approval_runtime_v2.instances WHERE instance_id=c.parent_instance_id;
          SELECT * INTO s FROM cp6_approval_start_v2.starts WHERE instance_id=c.root_instance_id;
          SELECT snapshot_json::jsonb INTO source FROM cp6_workflow_authoring_v2.versions
            WHERE tenant_id=c.tenant_id AND environment_id=c.environment_id AND head_id=c.flow_head AND id=c.flow_version;
          form=cp6_approval_runtime_v2.form_pin(c.tenant_id,c.environment_id,c.flow_version);
          IF i."Id" IS NULL OR p.instance_id IS NULL OR s.instance_id IS NULL
            OR i."Creator" IS DISTINCT FROM 'oa.app/2.0' OR i."TenantId"<>c.tenant_id
            OR (p.tenant_id,p.environment_id,p.root_instance_id) IS DISTINCT FROM (c.tenant_id,c.environment_id,c.root_instance_id)
            OR i."ParentInstanceId" IS DISTINCT FROM c.parent_instance_id OR i."ParentTokenId" IS DISTINCT FROM c.parent_token_id
            OR i."SubIndex" IS DISTINCT FROM c.sub_index OR i."FlowDefVersionId" IS DISTINCT FROM c.flow_version
            OR i."FormDefVersionId" IS DISTINCT FROM form OR i."FlowKey"::text COLLATE "C" IS DISTINCT FROM (source->>'key') COLLATE "C"
            OR i."BizType" IS NOT NULL OR i."BizId" IS NOT NULL
            OR NOT EXISTS(SELECT FROM public."Wf_FlowInstance" r WHERE r."Id"=s.instance_id AND r."StarterId"=i."StarterId")
            OR NOT EXISTS(SELECT FROM public."Wf_FlowToken" t JOIN public."Wf_FlowToken" b ON b."Id"=t."ParentTokenId"
              WHERE t."Id"=c.parent_token_id AND t."InstanceId"=c.parent_instance_id AND t."TenantId"=c.tenant_id AND t."NodeId"::text COLLATE "C"=c.parent_node
                AND b."InstanceId"=c.parent_instance_id AND b."TenantId"=c.tenant_id AND b."Id"<>t."Id")
            OR NOT EXISTS(SELECT FROM cp6_workflow_authoring_v2.versions v,LATERAL jsonb_array_elements(v.snapshot_json::jsonb->'dependencyRefs') d
              WHERE v.tenant_id=p.tenant_id AND v.environment_id=p.environment_id AND v.head_id=p.flow_head AND v.id=p.flow_version
                AND d->>'kind'='FLOW' AND (d->>'location') COLLATE "C"=c.parent_node AND (d->>'version')::uuid=c.flow_version
                AND (d->>'reference') COLLATE "C"=(source->>'key') COLLATE "C")
            OR (form IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_form_authoring_v2.versions v WHERE v.tenant_id=c.tenant_id AND v.environment_id=c.environment_id AND v.id=form))
            THEN RAISE EXCEPTION 'APP_CHILD_GRAPH_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER runtime_child_complete AFTER INSERT ON cp6_approval_runtime_v2.children DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_runtime_v2.child_complete();

        CREATE OR REPLACE FUNCTION cp6_approval_start_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
                DECLARE i uuid;s cp6_approval_start_v2.starts;p jsonb;a jsonb;v jsonb; BEGIN
                  IF TG_TABLE_NAME='starts' THEN i=NEW.instance_id;
                  ELSE
                    IF NEW."Creator" IS DISTINCT FROM 'oa.app/2.0' THEN RETURN NEW; END IF;
                    i=NEW."Id";
                  END IF;
                  SELECT * INTO s FROM cp6_approval_start_v2.starts WHERE instance_id=i;
                  IF NOT FOUND AND EXISTS(SELECT FROM cp6_approval_runtime_v2.children WHERE instance_id=i) THEN RETURN NEW; END IF;
                  IF NOT FOUND THEN RAISE EXCEPTION 'APP_START_COMPLETION_REQUIRED' USING ERRCODE='23514'; END IF;
                  SELECT convert_from(x.payload,'UTF8')::jsonb,x.ack_json::jsonb INTO p,a
                    FROM cp6_approval_intake_v2.submissions x WHERE x.tenant_id=s.tenant_id AND x.environment_id=s.environment_id AND x.owner=s.owner
                      AND x.id=s.submission_id AND x.flow_head=s.flow_head AND x.flow_version=s.flow_version;
                  v=s.result_json::jsonb;
                  IF p IS NULL OR v IS DISTINCT FROM (a || jsonb_build_object('instanceId',i,'state',s.state,'rowVersion','AAAAAAAAAAI='))
                    OR s.audit_json::jsonb->>'engineStartId' IS DISTINCT FROM s.submission_id::text
                    OR NOT EXISTS(SELECT FROM public."Wf_FlowInstance" f WHERE f."Id"=i AND f."TenantId"=s.tenant_id AND f."Creator"='oa.app/2.0'
                      AND f."FlowDefVersionId"=s.flow_version AND f."StarterId"=(p->>'starterId')::uuid
                      AND f."BizType" IS NULL AND f."BizId" IS NULL
                      AND f."FormDefVersionId" IS NOT DISTINCT FROM cp6_approval_runtime_v2.form_pin(s.tenant_id,s.environment_id,s.flow_version)
                      AND f."ParentInstanceId" IS NULL AND f."ParentTokenId" IS NULL AND f."SubIndex" IS NULL
                      AND ((f."VarsJson"::text COLLATE "C")=(convert_from(decode(p#>>'{snapshot,canonicalBytesBase64}','base64'),'UTF8') COLLATE "C")
                        OR EXISTS(SELECT FROM cp6_approval_runtime_v2.children WHERE root_instance_id=i)
                        OR EXISTS(SELECT FROM public."Wf_FlowHistory" h WHERE h."InstanceId"=i AND h."TenantId"=s.tenant_id AND h."Action"='subFlowError'))
                      AND f."Status"=CASE s.state WHEN 'RUNNING' THEN 0 ELSE 4 END)
                    OR (s.state='RUNNING' AND NOT EXISTS(SELECT FROM public."Wf_FlowTask" t JOIN cp6_approval_runtime_v2.instances r ON r.instance_id=t."InstanceId"
                        WHERE r.root_instance_id=i AND t."TenantId"=s.tenant_id AND t."Status"=0)
                      AND NOT EXISTS(SELECT FROM public."Wf_FlowInstance" f JOIN cp6_approval_runtime_v2.instances r ON r.instance_id=f."Id" WHERE r.root_instance_id=i AND f."Status"=4))
                    OR NOT EXISTS(SELECT FROM public."Wf_FlowToken" t WHERE t."InstanceId"=i AND t."TenantId"=s.tenant_id)
                    OR NOT EXISTS(SELECT FROM public."Wf_FlowHistory" h WHERE h."InstanceId"=i AND h."TenantId"=s.tenant_id AND h."Action"='submit' AND h."ActorId"=(p->>'starterId')::uuid)
                    OR EXISTS(SELECT FROM public."Wf_FlowTask" t WHERE t."InstanceId"=i AND (t."TenantId"<>s.tenant_id OR t."Status"<>0
                      OR NOT EXISTS(SELECT FROM public."Wf_FlowToken" k WHERE k."Id"=t."TokenId" AND k."InstanceId"=i AND k."TenantId"=s.tenant_id)))
                    THEN RAISE EXCEPTION 'APP_START_ATOMIC_GRAPH_REQUIRED' USING ERRCODE='23514'; END IF;
                  RETURN NEW;
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
                      AND x."TenantId"=a.tenant_id AND x."AssigneeId"=a.principal_id AND x."TokenId"=a.token_id))
                    THEN RAISE EXCEPTION 'APP_TASK_ASSIGNMENT_MISMATCH' USING ERRCODE='23514'; END IF;
                  FOR t IN SELECT * FROM public."Wf_FlowTask" WHERE "InstanceId"=instance LOOP
                    SELECT * INTO f FROM cp6_approval_tasks_v2.assignments WHERE task_id=t."Id";
                    IF NOT FOUND THEN RAISE EXCEPTION 'APP_TASK_ASSIGNMENT_REQUIRED' USING ERRCODE='23514'; END IF;
                    b=f.body::jsonb;
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
        CREATE CONSTRAINT TRIGGER runtime_child_assignments AFTER INSERT ON cp6_approval_runtime_v2.children DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_tasks_v2.complete();
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
                        AND c.tenant_id=(r->>'TenantId')::uuid AND c.writer_xid=pg_current_xact_id() AND NOT c.completed) AND NOT EXISTS(SELECT FROM cp6_approval_withdraw_v2.commands w WHERE w.instance_id=root AND w.tenant_id=(r->>'TenantId')::uuid AND w.active_xid=pg_current_xact_id() AND w.state='RECEIVED'))
                      THEN RAISE EXCEPTION 'APP_DECISION_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
                    IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_FlowInstance' AND
                      (to_jsonb(OLD)-ARRAY['Status','CurrentNode','ModifyDate','Modifier','RowVersion','VarsJson']) IS DISTINCT FROM
                      (r-ARRAY['Status','CurrentNode','ModifyDate','Modifier','RowVersion','VarsJson']) THEN
                        RAISE EXCEPTION 'APP_INSTANCE_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
                    IF TG_OP='UPDATE' AND TG_TABLE_NAME='Wf_FlowTask' AND
                      (to_jsonb(OLD)-ARRAY['Status','Comment','ModifyDate','Modifier','ApprovalRowVersion']) IS DISTINCT FROM
                      (r-ARRAY['Status','Comment','ModifyDate','Modifier','ApprovalRowVersion']) THEN
                        RAISE EXCEPTION 'APP_TASK_IDENTITY_IMMUTABLE' USING ERRCODE='23514'; END IF;
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
        CREATE OR REPLACE FUNCTION cp6_approval_decision_v2.close_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
                BEGIN
                  IF TG_TABLE_NAME='closed_commands' THEN
                    IF EXISTS(SELECT FROM cp6_approval_decision_v2.commands WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND id=NEW.id)
                      OR NOT EXISTS(SELECT FROM public."Wf_FlowTask" t JOIN cp6_approval_runtime_v2.instances s ON s.tenant_id=t."TenantId" AND s.instance_id=t."InstanceId"
                        WHERE t."TenantId"=NEW.tenant_id AND t."Id"=NEW.task_id AND s.environment_id=NEW.environment_id)
                      THEN RAISE EXCEPTION 'DECISION_CLOSE_CONFLICT' USING ERRCODE='23514'; END IF;
                  ELSE
                    IF EXISTS(SELECT FROM cp6_approval_decision_v2.closed_commands WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND id=NEW.id)
                      THEN RAISE EXCEPTION 'DECISION_COMMAND_CLOSED' USING ERRCODE='23514'; END IF;
                  END IF;RETURN NEW;
                END $$;
        CREATE OR REPLACE FUNCTION cp6_approval_withdraw_v2.bump() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
                DECLARE sid uuid;own text;BEGIN
                  IF TG_TABLE_SCHEMA='public' THEN
                    UPDATE cp6_approval_withdraw_v2.submission_versions v SET row_version=int8send(nextval('cp6_approval_withdraw_v2.version'))
                      FROM cp6_approval_runtime_v2.instances s WHERE s.instance_id=(CASE WHEN TG_TABLE_NAME='Wf_FlowInstance' THEN to_jsonb(NEW)->>'Id' ELSE to_jsonb(NEW)->>'InstanceId' END)::uuid
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
        CREATE OR REPLACE FUNCTION cp6_approval_withdraw_v2.verify_decision(t uuid,e uuid,did uuid) RETURNS void LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
                    OR EXISTS(SELECT FROM public."Wf_FlowInstance" x JOIN cp6_approval_runtime_v2.instances r ON r.instance_id=x."Id" WHERE r.root_instance_id=d.instance_id AND x."Status" NOT IN(1,2,3))
                    OR EXISTS(SELECT FROM public."Wf_FlowTask" x WHERE x."InstanceId" IN(SELECT instance_id FROM cp6_approval_runtime_v2.instances WHERE root_instance_id=d.instance_id) AND x."Status" IN(0,4))
                    OR EXISTS(SELECT FROM public."Wf_FlowToken" x WHERE x."InstanceId" IN(SELECT instance_id FROM cp6_approval_runtime_v2.instances WHERE root_instance_id=d.instance_id) AND x."Status"=0)
                    OR EXISTS(SELECT FROM public."Wf_FlowFormTo" x WHERE x."InstanceId" IN(SELECT instance_id FROM cp6_approval_runtime_v2.instances WHERE root_instance_id=d.instance_id) AND x."Status"=0)
                    OR NOT EXISTS(SELECT FROM public."Wf_FlowHistory" h WHERE h."InstanceId"=d.instance_id AND h."TenantId"=t AND h."Action"='withdraw'
                      AND h."ActorId"::text=f#>>'{actor,actualHandlerId}')
                    OR NOT EXISTS(SELECT FROM cp6_approval_decision_v2.outbox o WHERE o.tenant_id=t AND o.environment_id=e AND o.decision_id=did
                      AND o.id=d.event_id AND o.submission_id=d.submission_id AND o.consumer_owner=w.owner AND o.payload=d.payload AND o.payload_digest=d.payload_digest)
                    THEN RAISE EXCEPTION 'WITHDRAW_DECISION_EVIDENCE_REQUIRED';END IF;
                END $$;
        ALTER FUNCTION cp6_approval_start_v2.ready() RENAME TO ready_before_subflows;
        CREATE FUNCTION cp6_approval_start_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_approval_start_v2.ready_before_subflows() AND NOT EXISTS(SELECT FROM (VALUES
            ('runtime_child_root','cp6_authentication_v1.before_change()',6,false),
            ('runtime_child_writer','cp6_approval_runtime_v2.child_writer()',7,false),
            ('runtime_child_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('runtime_child_complete','cp6_approval_runtime_v2.child_complete()',5,true),
            ('runtime_child_assignments','cp6_approval_tasks_v2.complete()',5,true)) a(name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='cp6_approval_runtime_v2.children'::regclass AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward recovery preserves original runtime graphs and decisions.");
}
