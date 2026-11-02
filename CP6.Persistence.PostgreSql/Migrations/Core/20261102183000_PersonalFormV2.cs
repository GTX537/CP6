using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261102183000_PersonalFormV2")]
public sealed class PersonalFormV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_personal_form_v2;
        REVOKE ALL ON SCHEMA cp6_personal_form_v2 FROM PUBLIC;
        CREATE TABLE cp6_personal_form_v2.policy(
         tenant_id uuid NOT NULL,environment_id uuid NOT NULL,head_id uuid NOT NULL,
         row_version uuid NOT NULL DEFAULT gen_random_uuid(),body text NOT NULL,
         PRIMARY KEY(tenant_id,environment_id,head_id),FOREIGN KEY(tenant_id,environment_id,head_id) REFERENCES cp6_form_authoring_v2.heads(tenant_id,environment_id,id));
        CREATE TABLE cp6_personal_form_v2.commands(
         id uuid PRIMARY KEY,tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,
         operation text COLLATE "C" NOT NULL,resource_key text COLLATE "C" NOT NULL,command_id text COLLATE "C" NOT NULL,
         digest text NOT NULL,raw_body bytea NOT NULL,expected_version text,correlation text,
         draft_id uuid,form_data_id uuid,root_id uuid,task_id uuid,
         result_json text,audit_json text,completed boolean NOT NULL DEFAULT false,closed boolean NOT NULL DEFAULT false,recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
         writer_xid xid8 NOT NULL DEFAULT pg_current_xact_id(),
         UNIQUE(tenant_id,environment_id,actor_id,operation,resource_key,command_id));
        CREATE TABLE cp6_personal_form_v2.drafts(
         id uuid PRIMARY KEY,tenant_id uuid NOT NULL,environment_id uuid NOT NULL,owner_id uuid NOT NULL,
         head_id uuid NOT NULL,form_version uuid NOT NULL,data_json text NOT NULL,title text CHECK(length(title)<=200),
         status integer NOT NULL DEFAULT 0 CHECK(status IN(0,1,2)),row_version uuid NOT NULL DEFAULT gen_random_uuid(),
         form_data_id uuid,rebased_from uuid,updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
         deleted_at timestamptz,retain_until timestamptz,retention_ref text,
         CHECK(status<>2 OR deleted_at IS NOT NULL AND retain_until>deleted_at AND retention_ref IS NOT NULL),
         CHECK((status=1)=(form_data_id IS NOT NULL)),
         FOREIGN KEY(tenant_id,environment_id,head_id,form_version) REFERENCES cp6_form_authoring_v2.versions(tenant_id,environment_id,head_id,id));
        CREATE INDEX personal_drafts_owner ON cp6_personal_form_v2.drafts(tenant_id,environment_id,owner_id,status,updated_at DESC,id DESC);
        CREATE TABLE cp6_personal_form_v2.history(
         id uuid PRIMARY KEY,command_id uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.commands(id),
         draft_id uuid NOT NULL REFERENCES cp6_personal_form_v2.drafts(id),before_json text,after_json text NOT NULL);
        CREATE TABLE cp6_personal_form_v2.previews(
         id uuid PRIMARY KEY,tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,
         draft_id uuid NOT NULL REFERENCES cp6_personal_form_v2.drafts(id),draft_version uuid NOT NULL,source_version uuid NOT NULL,target_version uuid NOT NULL,
         digest text NOT NULL,result_json text NOT NULL,expires_at timestamptz NOT NULL);
        CREATE TABLE cp6_personal_form_v2.submissions(
         form_data_id uuid PRIMARY KEY,tenant_id uuid NOT NULL,environment_id uuid NOT NULL,actor_id uuid NOT NULL,
         command_id uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.commands(id),draft_id uuid UNIQUE REFERENCES cp6_personal_form_v2.drafts(id),
         head_id uuid NOT NULL,form_version uuid NOT NULL,form_activation uuid NOT NULL,
         binding_revision uuid,flow_head uuid,flow_version uuid,flow_activation uuid,root_id uuid UNIQUE,
         title text,submitted_at timestamptz NOT NULL,
         CHECK((root_id IS NULL AND binding_revision IS NULL AND flow_head IS NULL AND flow_version IS NULL AND flow_activation IS NULL)
           OR(root_id IS NOT NULL AND binding_revision IS NOT NULL AND flow_head IS NOT NULL AND flow_version IS NOT NULL AND flow_activation IS NOT NULL)),
         FOREIGN KEY(form_data_id) REFERENCES public."Wf_FormData"("Id") DEFERRABLE INITIALLY DEFERRED,
         FOREIGN KEY(tenant_id,environment_id,head_id,form_version) REFERENCES cp6_form_authoring_v2.versions(tenant_id,environment_id,head_id,id),
         FOREIGN KEY(tenant_id,environment_id,flow_head,flow_version) REFERENCES cp6_workflow_authoring_v2.versions(tenant_id,environment_id,head_id,id));
        CREATE TABLE cp6_personal_form_v2.instances(
         instance_id uuid PRIMARY KEY,tenant_id uuid NOT NULL,environment_id uuid NOT NULL,form_data_id uuid NOT NULL REFERENCES cp6_personal_form_v2.submissions(form_data_id),
         root_id uuid NOT NULL,parent_id uuid,parent_token_id uuid,parent_node text COLLATE "C",sub_index integer,
         flow_head uuid NOT NULL,flow_version uuid NOT NULL,
         FOREIGN KEY(instance_id) REFERENCES public."Wf_FlowInstance"("Id") DEFERRABLE INITIALLY DEFERRED,
         FOREIGN KEY(root_id) REFERENCES cp6_personal_form_v2.instances(instance_id) DEFERRABLE INITIALLY DEFERRED,
         FOREIGN KEY(parent_id) REFERENCES cp6_personal_form_v2.instances(instance_id) DEFERRABLE INITIALLY DEFERRED,
         FOREIGN KEY(parent_token_id) REFERENCES public."Wf_FlowToken"("Id") DEFERRABLE INITIALLY DEFERRED,
         FOREIGN KEY(tenant_id,environment_id,flow_head,flow_version) REFERENCES cp6_workflow_authoring_v2.versions(tenant_id,environment_id,head_id,id),
         UNIQUE(parent_token_id,sub_index),CHECK((instance_id=root_id AND parent_id IS NULL AND parent_token_id IS NULL AND parent_node IS NULL AND sub_index IS NULL)
           OR(instance_id<>root_id AND parent_id IS NOT NULL AND parent_token_id IS NOT NULL AND parent_node IS NOT NULL AND sub_index>=0)));
        CREATE INDEX personal_instances_root ON cp6_personal_form_v2.instances(tenant_id,environment_id,root_id);
        CREATE TABLE cp6_personal_form_v2.assignments(
         task_id uuid PRIMARY KEY REFERENCES public."Wf_FlowTask"("Id") DEFERRABLE INITIALLY DEFERRED,
         instance_id uuid NOT NULL REFERENCES cp6_personal_form_v2.instances(instance_id),command_id uuid NOT NULL REFERENCES cp6_personal_form_v2.commands(id),body text NOT NULL);
        CREATE TABLE cp6_personal_form_v2.votes(
         id uuid PRIMARY KEY,command_id uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.commands(id),
         task_id uuid NOT NULL UNIQUE REFERENCES cp6_personal_form_v2.assignments(task_id),body text NOT NULL);
        CREATE TABLE cp6_personal_form_v2.outbox(
         id uuid PRIMARY KEY,command_id uuid NOT NULL REFERENCES cp6_personal_form_v2.commands(id),
         kind text NOT NULL,recipient_id uuid NOT NULL,task_id uuid,body text NOT NULL,
         notification_id uuid NOT NULL UNIQUE REFERENCES public."Wf_Notification"("Id") DEFERRABLE INITIALLY DEFERRED,
         created_at timestamptz NOT NULL DEFAULT clock_timestamp());
        CREATE UNIQUE INDEX personal_task_notification ON cp6_personal_form_v2.outbox(task_id) WHERE kind='TASK_ASSIGNED';
        CREATE FUNCTION cp6_personal_form_v2.policy_version() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
         IF TG_OP='UPDATE' AND (NEW.tenant_id,NEW.environment_id,NEW.head_id) IS DISTINCT FROM (OLD.tenant_id,OLD.environment_id,OLD.head_id)
           THEN RAISE EXCEPTION 'FORM_POLICY_IDENTITY_IMMUTABLE' USING ERRCODE='23514';END IF;
         NEW.row_version=gen_random_uuid();RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_personal_form_v2.fact_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r jsonb;c cp6_personal_form_v2.commands;x cp6_personal_form_v2.instances;BEGIN
         r=to_jsonb(NEW);
         IF TG_TABLE_NAME='instances' THEN
           SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE form_data_id=NEW.form_data_id AND root_id=NEW.root_id
             AND tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id AND writer_xid=pg_current_xact_id() AND NOT completed;
         ELSE
           SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE id=(r->>'command_id')::uuid AND writer_xid=pg_current_xact_id() AND NOT completed;
         END IF;
         IF c.id IS NULL THEN RAISE EXCEPTION 'FORM_COMMAND_WRITER_REQUIRED' USING ERRCODE='23514';END IF;
         IF TG_TABLE_NAME='history' AND (r->>'draft_id')::uuid IS DISTINCT FROM c.draft_id
           OR TG_TABLE_NAME='votes' AND (r->>'task_id')::uuid IS DISTINCT FROM c.task_id
           OR TG_TABLE_NAME='submissions' AND (r->>'form_data_id')::uuid IS DISTINCT FROM c.form_data_id
           THEN RAISE EXCEPTION 'FORM_COMMAND_RESOURCE_REQUIRED' USING ERRCODE='23514';END IF;
         IF TG_TABLE_NAME='assignments' THEN
           SELECT * INTO x FROM cp6_personal_form_v2.instances WHERE instance_id=NEW.instance_id;
           IF (x.tenant_id,x.environment_id,x.form_data_id,x.root_id) IS DISTINCT FROM (c.tenant_id,c.environment_id,c.form_data_id,c.root_id)
             THEN RAISE EXCEPTION 'FORM_COMMAND_RESOURCE_REQUIRED' USING ERRCODE='23514';END IF;
         END IF;
         RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_personal_form_v2.command_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
         IF TG_OP='INSERT' THEN NEW.writer_xid=pg_current_xact_id();
         ELSIF TG_OP<>'UPDATE' THEN RAISE EXCEPTION 'FORM_COMMAND_IMMUTABLE' USING ERRCODE='23514';
         ELSIF OLD.completed OR OLD.writer_xid<>pg_current_xact_id()
           OR (to_jsonb(OLD)-ARRAY['result_json','audit_json','completed']) IS DISTINCT FROM (to_jsonb(NEW)-ARRAY['result_json','audit_json','completed'])
           THEN RAISE EXCEPTION 'FORM_COMMAND_IMMUTABLE' USING ERRCODE='23514';END IF;
         RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_personal_form_v2.draft_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE c cp6_personal_form_v2.commands;BEGIN
         IF TG_OP IN('DELETE','TRUNCATE') THEN RAISE EXCEPTION 'FORM_TOMBSTONE_REQUIRED' USING ERRCODE='23514';END IF;
         SELECT * INTO c FROM cp6_personal_form_v2.commands WHERE tenant_id=NEW.tenant_id AND environment_id=NEW.environment_id
           AND actor_id=NEW.owner_id AND draft_id=NEW.id AND writer_xid=pg_current_xact_id() AND NOT completed;
         IF c.id IS NULL THEN RAISE EXCEPTION 'FORM_DRAFT_OWNER_REQUIRED' USING ERRCODE='23514';END IF;
         IF TG_OP='UPDATE' THEN
           IF (OLD.id,OLD.tenant_id,OLD.environment_id,OLD.owner_id,OLD.head_id) IS DISTINCT FROM (NEW.id,NEW.tenant_id,NEW.environment_id,NEW.owner_id,NEW.head_id)
             OR OLD.status=1 OR (OLD.status=2 AND c.operation<>'FORM_DRAFT_RESTORE')
             OR (OLD.form_version<>NEW.form_version AND c.operation<>'FORM_DRAFT_REBASE')
             OR (NEW.status=1 AND c.operation<>'FORM_DRAFT_SUBMIT') OR (NEW.status=2 AND c.operation<>'FORM_DRAFT_DELETE')
             OR c.expected_version IS DISTINCT FROM OLD.row_version::text THEN RAISE EXCEPTION 'FORM_DRAFT_TRANSITION_INVALID' USING ERRCODE='23514';END IF;
         END IF;
         NEW.row_version=gen_random_uuid();NEW.updated_at=clock_timestamp();RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_personal_form_v2.runtime_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
             IF TG_TABLE_NAME IN('Wf_FormData','Wf_FlowHistory','Wf_FlowData')
               OR TG_TABLE_NAME='Wf_FlowInstance' AND (prior-ARRAY['Status','CurrentNode','ModifyDate','Modifier','RowVersion','VarsJson']) IS DISTINCT FROM (r-ARRAY['Status','CurrentNode','ModifyDate','Modifier','RowVersion','VarsJson'])
               OR TG_TABLE_NAME='Wf_FlowTask' AND (prior-ARRAY['Status','Comment','ModifyDate','Modifier','ApprovalRowVersion']) IS DISTINCT FROM (r-ARRAY['Status','Comment','ModifyDate','Modifier','ApprovalRowVersion'])
               OR TG_TABLE_NAME='Wf_FlowToken' AND ((prior-ARRAY['Status','NodeId','StagePlanJson','ModifyDate','Modifier']) IS DISTINCT FROM (r-ARRAY['Status','NodeId','StagePlanJson','ModifyDate','Modifier'])
                 OR prior->>'NodeId' IS DISTINCT FROM r->>'NodeId' AND EXISTS(SELECT FROM cp6_personal_form_v2.instances WHERE parent_token_id=(r->>'Id')::uuid))
               THEN RAISE EXCEPTION 'FORM_RUNTIME_IDENTITY_IMMUTABLE' USING ERRCODE='23514';END IF;
           END IF;
         END IF;RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
        END $$;
        CREATE FUNCTION cp6_personal_form_v2.graph_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE x cp6_personal_form_v2.instances;i public."Wf_FlowInstance";s cp6_personal_form_v2.submissions;v jsonb;BEGIN
         SELECT * INTO x FROM cp6_personal_form_v2.instances WHERE instance_id=NEW.instance_id;
         SELECT * INTO i FROM public."Wf_FlowInstance" WHERE "Id"=x.instance_id;
         SELECT * INTO s FROM cp6_personal_form_v2.submissions WHERE form_data_id=x.form_data_id;
         SELECT snapshot_json::jsonb INTO v FROM cp6_workflow_authoring_v2.versions WHERE tenant_id=x.tenant_id AND environment_id=x.environment_id AND head_id=x.flow_head AND id=x.flow_version;
         IF i."Id" IS NULL OR s.form_data_id IS NULL OR (i."TenantId",i."FlowDefVersionId",i."StarterId",i."Creator",i."FormDataId",i."BizType",i."BizId"::text)
            IS DISTINCT FROM (x.tenant_id,x.flow_version,s.actor_id,'oa.form/2.0',s.form_data_id,
              CASE WHEN x.instance_id=x.root_id THEN 'SFS' ELSE NULL END,
              CASE WHEN x.instance_id=x.root_id THEN s.form_data_id::text ELSE NULL END)
            OR i."FlowKey"::text COLLATE "C" IS DISTINCT FROM (v->>'key') COLLATE "C"
            OR i."FormDefVersionId" IS DISTINCT FROM cp6_approval_runtime_v2.form_pin(x.tenant_id,x.environment_id,x.flow_version)
            OR (i."ParentInstanceId",i."ParentTokenId",i."SubIndex") IS DISTINCT FROM (x.parent_id,x.parent_token_id,x.sub_index)
            OR (s.tenant_id,s.environment_id,s.root_id) IS DISTINCT FROM (x.tenant_id,x.environment_id,x.root_id)
            OR x.instance_id=x.root_id AND (x.flow_head,x.flow_version,i."FormDefVersionId") IS DISTINCT FROM (s.flow_head,s.flow_version,s.form_version)
            THEN RAISE EXCEPTION 'FORM_RUNTIME_GRAPH_REQUIRED' USING ERRCODE='23514';END IF;
         IF x.parent_id IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_personal_form_v2.instances p
            JOIN cp6_workflow_authoring_v2.versions pv ON pv.tenant_id=p.tenant_id AND pv.environment_id=p.environment_id AND pv.head_id=p.flow_head AND pv.id=p.flow_version,
            LATERAL jsonb_array_elements(pv.snapshot_json::jsonb->'dependencyRefs') d
            WHERE p.instance_id=x.parent_id AND p.root_id=x.root_id AND p.form_data_id=x.form_data_id
              AND d->>'kind'='FLOW' AND (d->>'location') COLLATE "C"=x.parent_node COLLATE "C" AND (d->>'version')::uuid=x.flow_version
              AND EXISTS(SELECT FROM public."Wf_FlowToken" k JOIN public."Wf_FlowToken" b ON b."Id"=k."ParentTokenId"
                WHERE k."Id"=x.parent_token_id AND k."InstanceId"=x.parent_id AND b."InstanceId"=x.parent_id AND k."NodeId"::text COLLATE "C"=x.parent_node COLLATE "C"))
            THEN RAISE EXCEPTION 'FORM_CHILD_GRAPH_REQUIRED' USING ERRCODE='23514';END IF;
         RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_personal_form_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
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
                 AND (a.body::jsonb->>'principalId')::uuid=t."AssigneeId" AND (a.body::jsonb->>'tokenId')::uuid=t."TokenId"
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
        CREATE TRIGGER personal_policy_fence BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.policy FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.before_authority_change();
        CREATE TRIGGER personal_policy_version BEFORE INSERT OR UPDATE ON cp6_personal_form_v2.policy FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.policy_version();
        CREATE TRIGGER personal_commands_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_drafts_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.drafts FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_previews_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.previews FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_previews_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.previews FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER personal_submissions_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.submissions FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_submissions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.submissions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER personal_instances_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.instances FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_instances_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.instances FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER personal_assignments_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.assignments FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_assignments_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.assignments FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER personal_votes_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.votes FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_votes_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.votes FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER personal_outbox_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.outbox FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_outbox_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.outbox FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER personal_history_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.history FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_history_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_personal_form_v2.history FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER personal_command_guard BEFORE INSERT OR UPDATE OR DELETE ON cp6_personal_form_v2.commands FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.command_guard();
        CREATE TRIGGER personal_commands_no_truncate BEFORE TRUNCATE ON cp6_personal_form_v2.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER personal_draft_guard BEFORE INSERT OR UPDATE OR DELETE ON cp6_personal_form_v2.drafts FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.draft_guard();
        CREATE TRIGGER personal_drafts_no_truncate BEFORE TRUNCATE ON cp6_personal_form_v2.drafts FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER personal_command_complete AFTER INSERT OR UPDATE ON cp6_personal_form_v2.commands DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.complete();
        CREATE CONSTRAINT TRIGGER personal_graph_complete AFTER INSERT ON cp6_personal_form_v2.instances DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.graph_complete();
        CREATE TRIGGER personal_fact_guard BEFORE INSERT ON cp6_personal_form_v2.submissions FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.fact_guard();
        CREATE TRIGGER personal_fact_guard BEFORE INSERT ON cp6_personal_form_v2.instances FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.fact_guard();
        CREATE TRIGGER personal_fact_guard BEFORE INSERT ON cp6_personal_form_v2.assignments FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.fact_guard();
        CREATE TRIGGER personal_fact_guard BEFORE INSERT ON cp6_personal_form_v2.votes FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.fact_guard();
        CREATE TRIGGER personal_fact_guard BEFORE INSERT ON cp6_personal_form_v2.outbox FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.fact_guard();
        CREATE TRIGGER personal_fact_guard BEFORE INSERT ON cp6_personal_form_v2.history FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.fact_guard();
        CREATE TRIGGER personal_runtime_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FormData" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_runtime_guard BEFORE INSERT OR UPDATE OR DELETE ON public."Wf_FormData" FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_truncate BEFORE TRUNCATE ON public."Wf_FormData" FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FlowInstance" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_runtime_guard BEFORE INSERT OR UPDATE OR DELETE ON public."Wf_FlowInstance" FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_truncate BEFORE TRUNCATE ON public."Wf_FlowInstance" FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FlowTask" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_runtime_guard BEFORE INSERT OR UPDATE OR DELETE ON public."Wf_FlowTask" FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_truncate BEFORE TRUNCATE ON public."Wf_FlowTask" FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FlowToken" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_runtime_guard BEFORE INSERT OR UPDATE OR DELETE ON public."Wf_FlowToken" FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_truncate BEFORE TRUNCATE ON public."Wf_FlowToken" FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FlowHistory" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_runtime_guard BEFORE INSERT OR UPDATE OR DELETE ON public."Wf_FlowHistory" FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_truncate BEFORE TRUNCATE ON public."Wf_FlowHistory" FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FlowData" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_runtime_guard BEFORE INSERT OR UPDATE OR DELETE ON public."Wf_FlowData" FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_truncate BEFORE TRUNCATE ON public."Wf_FlowData" FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FlowFormTo" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_runtime_guard BEFORE INSERT OR UPDATE OR DELETE ON public."Wf_FlowFormTo" FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_truncate BEFORE TRUNCATE ON public."Wf_FlowFormTo" FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_FlowCc" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_runtime_guard BEFORE INSERT OR UPDATE OR DELETE ON public."Wf_FlowCc" FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_truncate BEFORE TRUNCATE ON public."Wf_FlowCc" FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public."Wf_ServiceJob" FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER personal_runtime_guard BEFORE INSERT OR UPDATE OR DELETE ON public."Wf_ServiceJob" FOR EACH ROW EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE TRIGGER personal_runtime_truncate BEFORE TRUNCATE ON public."Wf_ServiceJob" FOR EACH STATEMENT EXECUTE FUNCTION cp6_personal_form_v2.runtime_guard();
        CREATE FUNCTION cp6_personal_form_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
        SELECT current_setting('session_replication_role')='origin' AND cp6_workflow_authoring_v2.form_binding_ready() AND cp6_form_authoring_v2.activation_ready()
         AND NOT EXISTS(SELECT FROM (VALUES
        ('cp6_personal_form_v2.policy','personal_policy_fence','cp6_scope_authorization_v1.before_authority_change()',62,false),
        ('cp6_personal_form_v2.policy','personal_policy_version','cp6_personal_form_v2.policy_version()',23,false),
        ('cp6_personal_form_v2.commands','personal_commands_root','cp6_authentication_v1.before_change()',62,false),
        ('cp6_personal_form_v2.drafts','personal_drafts_root','cp6_authentication_v1.before_change()',62,false),
        ('cp6_personal_form_v2.previews','personal_previews_root','cp6_authentication_v1.before_change()',62,false),
        ('cp6_personal_form_v2.previews','personal_previews_immutable','cp6_scope_authorization_v1.immutable()',58,false),
        ('cp6_personal_form_v2.submissions','personal_submissions_root','cp6_authentication_v1.before_change()',62,false),
        ('cp6_personal_form_v2.submissions','personal_submissions_immutable','cp6_scope_authorization_v1.immutable()',58,false),
        ('cp6_personal_form_v2.instances','personal_instances_root','cp6_authentication_v1.before_change()',62,false),
        ('cp6_personal_form_v2.instances','personal_instances_immutable','cp6_scope_authorization_v1.immutable()',58,false),
        ('cp6_personal_form_v2.assignments','personal_assignments_root','cp6_authentication_v1.before_change()',62,false),
        ('cp6_personal_form_v2.assignments','personal_assignments_immutable','cp6_scope_authorization_v1.immutable()',58,false),
        ('cp6_personal_form_v2.votes','personal_votes_root','cp6_authentication_v1.before_change()',62,false),
        ('cp6_personal_form_v2.votes','personal_votes_immutable','cp6_scope_authorization_v1.immutable()',58,false),
        ('cp6_personal_form_v2.outbox','personal_outbox_root','cp6_authentication_v1.before_change()',62,false),
        ('cp6_personal_form_v2.outbox','personal_outbox_immutable','cp6_scope_authorization_v1.immutable()',58,false),
        ('cp6_personal_form_v2.history','personal_history_root','cp6_authentication_v1.before_change()',62,false),
        ('cp6_personal_form_v2.history','personal_history_immutable','cp6_scope_authorization_v1.immutable()',58,false),
        ('cp6_personal_form_v2.commands','personal_command_guard','cp6_personal_form_v2.command_guard()',31,false),
        ('cp6_personal_form_v2.commands','personal_commands_no_truncate','cp6_scope_authorization_v1.immutable()',34,false),
        ('cp6_personal_form_v2.drafts','personal_draft_guard','cp6_personal_form_v2.draft_guard()',31,false),
        ('cp6_personal_form_v2.drafts','personal_drafts_no_truncate','cp6_scope_authorization_v1.immutable()',34,false),
        ('cp6_personal_form_v2.commands','personal_command_complete','cp6_personal_form_v2.complete()',21,true),
        ('cp6_personal_form_v2.instances','personal_graph_complete','cp6_personal_form_v2.graph_complete()',5,true),
        ('cp6_personal_form_v2.submissions','personal_fact_guard','cp6_personal_form_v2.fact_guard()',7,false),
        ('cp6_personal_form_v2.instances','personal_fact_guard','cp6_personal_form_v2.fact_guard()',7,false),
        ('cp6_personal_form_v2.assignments','personal_fact_guard','cp6_personal_form_v2.fact_guard()',7,false),
        ('cp6_personal_form_v2.votes','personal_fact_guard','cp6_personal_form_v2.fact_guard()',7,false),
        ('cp6_personal_form_v2.outbox','personal_fact_guard','cp6_personal_form_v2.fact_guard()',7,false),
        ('cp6_personal_form_v2.history','personal_fact_guard','cp6_personal_form_v2.fact_guard()',7,false),
        ('public."Wf_FormData"','personal_runtime_root','cp6_authentication_v1.before_change()',62,false),
        ('public."Wf_FormData"','personal_runtime_guard','cp6_personal_form_v2.runtime_guard()',31,false),
        ('public."Wf_FormData"','personal_runtime_truncate','cp6_personal_form_v2.runtime_guard()',34,false),
        ('public."Wf_FlowInstance"','personal_runtime_root','cp6_authentication_v1.before_change()',62,false),
        ('public."Wf_FlowInstance"','personal_runtime_guard','cp6_personal_form_v2.runtime_guard()',31,false),
        ('public."Wf_FlowInstance"','personal_runtime_truncate','cp6_personal_form_v2.runtime_guard()',34,false),
        ('public."Wf_FlowTask"','personal_runtime_root','cp6_authentication_v1.before_change()',62,false),
        ('public."Wf_FlowTask"','personal_runtime_guard','cp6_personal_form_v2.runtime_guard()',31,false),
        ('public."Wf_FlowTask"','personal_runtime_truncate','cp6_personal_form_v2.runtime_guard()',34,false),
        ('public."Wf_FlowToken"','personal_runtime_root','cp6_authentication_v1.before_change()',62,false),
        ('public."Wf_FlowToken"','personal_runtime_guard','cp6_personal_form_v2.runtime_guard()',31,false),
        ('public."Wf_FlowToken"','personal_runtime_truncate','cp6_personal_form_v2.runtime_guard()',34,false),
        ('public."Wf_FlowHistory"','personal_runtime_root','cp6_authentication_v1.before_change()',62,false),
        ('public."Wf_FlowHistory"','personal_runtime_guard','cp6_personal_form_v2.runtime_guard()',31,false),
        ('public."Wf_FlowHistory"','personal_runtime_truncate','cp6_personal_form_v2.runtime_guard()',34,false),
        ('public."Wf_FlowData"','personal_runtime_root','cp6_authentication_v1.before_change()',62,false),
        ('public."Wf_FlowData"','personal_runtime_guard','cp6_personal_form_v2.runtime_guard()',31,false),
        ('public."Wf_FlowData"','personal_runtime_truncate','cp6_personal_form_v2.runtime_guard()',34,false),
        ('public."Wf_FlowFormTo"','personal_runtime_root','cp6_authentication_v1.before_change()',62,false),
        ('public."Wf_FlowFormTo"','personal_runtime_guard','cp6_personal_form_v2.runtime_guard()',31,false),
        ('public."Wf_FlowFormTo"','personal_runtime_truncate','cp6_personal_form_v2.runtime_guard()',34,false),
        ('public."Wf_FlowCc"','personal_runtime_root','cp6_authentication_v1.before_change()',62,false),
        ('public."Wf_FlowCc"','personal_runtime_guard','cp6_personal_form_v2.runtime_guard()',31,false),
        ('public."Wf_FlowCc"','personal_runtime_truncate','cp6_personal_form_v2.runtime_guard()',34,false),
        ('public."Wf_ServiceJob"','personal_runtime_root','cp6_authentication_v1.before_change()',62,false),
        ('public."Wf_ServiceJob"','personal_runtime_guard','cp6_personal_form_v2.runtime_guard()',31,false),
        ('public."Wf_ServiceJob"','personal_runtime_truncate','cp6_personal_form_v2.runtime_guard()',34,false)) a(tbl,name,fn,typ,deferred) WHERE NOT EXISTS(SELECT FROM pg_trigger t
         WHERE t.tgrelid=to_regclass(a.tbl) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ
         AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_personal_form_v2 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_personal_form_v2 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Personal form history and receipts require forward recovery.");
}
