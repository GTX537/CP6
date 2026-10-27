using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028150000_ApprovalTaskAssignmentsV2")]
public sealed class ApprovalTaskAssignmentsV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_approval_tasks_v2;
        CREATE TABLE cp6_approval_tasks_v2.assignments(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,submission_id uuid NOT NULL,instance_id uuid NOT NULL,
          task_id uuid PRIMARY KEY,token_id uuid NOT NULL,flow_version uuid NOT NULL,principal_id uuid NOT NULL,
          body text NOT NULL,digest text NOT NULL CHECK(digest ~ '^[0-9a-f]{64}$'),
          FOREIGN KEY(task_id) REFERENCES public."Wf_FlowTask"("Id"),
          FOREIGN KEY(instance_id) REFERENCES cp6_approval_start_v2.starts(instance_id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TRIGGER assignment_root BEFORE INSERT ON cp6_approval_tasks_v2.assignments FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER assignment_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_approval_tasks_v2.assignments FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_approval_tasks_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE instance uuid;f cp6_approval_tasks_v2.assignments;t public."Wf_FlowTask";s cp6_approval_start_v2.starts;b jsonb; BEGIN
          IF TG_TABLE_NAME='assignments' THEN instance=NEW.instance_id;
          ELSIF TG_TABLE_NAME='starts' THEN instance=NEW.instance_id;
          ELSE instance=NEW."InstanceId"; END IF;
          SELECT * INTO s FROM cp6_approval_start_v2.starts WHERE instance_id=instance;
          IF NOT FOUND THEN RETURN NEW; END IF;
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
        CREATE CONSTRAINT TRIGGER assignment_complete AFTER INSERT ON cp6_approval_tasks_v2.assignments DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_tasks_v2.complete();
        CREATE CONSTRAINT TRIGGER assignment_start_complete AFTER INSERT ON cp6_approval_start_v2.starts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_tasks_v2.complete();
        CREATE CONSTRAINT TRIGGER assignment_task_complete AFTER INSERT ON public."Wf_FlowTask" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_tasks_v2.complete();
        CREATE FUNCTION cp6_approval_tasks_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_approval_start_v2.ready() AND NOT EXISTS(SELECT FROM (VALUES
            ('cp6_approval_tasks_v2.assignments','assignment_root','cp6_authentication_v1.before_change()',6,false),
            ('cp6_approval_tasks_v2.assignments','assignment_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('cp6_approval_tasks_v2.assignments','assignment_complete','cp6_approval_tasks_v2.complete()',5,true),
            ('cp6_approval_start_v2.starts','assignment_start_complete','cp6_approval_tasks_v2.complete()',5,true),
            ('public."Wf_FlowTask"','assignment_task_complete','cp6_approval_tasks_v2.complete()',5,true)) a(tbl,name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tbl) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
              AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred AND t.tgqual IS NULL));
        $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Original assignment evidence is permanent; forward recovery only.");
}
