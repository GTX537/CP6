using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261028140000_ApprovalStartV2")]
public sealed class ApprovalStartV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_approval_start_v2;
        CREATE TABLE cp6_approval_start_v2.starts(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,owner text NOT NULL,submission_id uuid NOT NULL,
          engine_start_id uuid NOT NULL,instance_id uuid NOT NULL UNIQUE,flow_head uuid NOT NULL,flow_version uuid NOT NULL,
          state text NOT NULL CHECK(state IN('RUNNING','SUSPENDED')),result_json text NOT NULL,audit_json text NOT NULL,
          completed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          CHECK(engine_start_id=submission_id),PRIMARY KEY(tenant_id,environment_id,owner,submission_id),
          FOREIGN KEY(tenant_id,environment_id,owner,submission_id) REFERENCES cp6_approval_intake_v2.start_outbox(tenant_id,environment_id,owner,submission_id),
          FOREIGN KEY(instance_id) REFERENCES public."Wf_FlowInstance"("Id"),
          FOREIGN KEY(tenant_id,environment_id,flow_head,flow_version) REFERENCES cp6_workflow_authoring_v2.versions(tenant_id,environment_id,head_id,id));
        CREATE TRIGGER start_root BEFORE INSERT ON cp6_approval_start_v2.starts FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER start_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_approval_start_v2.starts FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_approval_start_v2.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE i uuid;s cp6_approval_start_v2.starts;p jsonb;a jsonb;v jsonb; BEGIN
          IF TG_TABLE_NAME='starts' THEN i=NEW.instance_id;
          ELSE
            IF NEW."Creator" IS DISTINCT FROM 'oa.app/2.0' THEN RETURN NEW; END IF;
            i=NEW."Id";
          END IF;
          SELECT * INTO s FROM cp6_approval_start_v2.starts WHERE instance_id=i;
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
              AND (f."VarsJson"::text COLLATE "C")=(convert_from(decode(p#>>'{snapshot,canonicalBytesBase64}','base64'),'UTF8') COLLATE "C")
              AND f."Status"=CASE s.state WHEN 'RUNNING' THEN 0 ELSE 4 END)
            OR (s.state='RUNNING' AND NOT EXISTS(SELECT FROM public."Wf_FlowTask" t WHERE t."InstanceId"=i AND t."TenantId"=s.tenant_id AND t."Status"=0))
            OR NOT EXISTS(SELECT FROM public."Wf_FlowToken" t WHERE t."InstanceId"=i AND t."TenantId"=s.tenant_id)
            OR NOT EXISTS(SELECT FROM public."Wf_FlowHistory" h WHERE h."InstanceId"=i AND h."TenantId"=s.tenant_id AND h."Action"='submit' AND h."ActorId"=(p->>'starterId')::uuid)
            OR EXISTS(SELECT FROM public."Wf_FlowTask" t WHERE t."InstanceId"=i AND (t."TenantId"<>s.tenant_id OR t."Status"<>0
              OR NOT EXISTS(SELECT FROM public."Wf_FlowToken" k WHERE k."Id"=t."TokenId" AND k."InstanceId"=i AND k."TenantId"=s.tenant_id)))
            THEN RAISE EXCEPTION 'APP_START_ATOMIC_GRAPH_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER start_complete AFTER INSERT ON cp6_approval_start_v2.starts DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_start_v2.complete();
        CREATE CONSTRAINT TRIGGER start_instance_complete AFTER INSERT ON public."Wf_FlowInstance" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_approval_start_v2.complete();
        -- This phase has no TxD writer. Do not let legacy entry points, timeout scanners or raw
        -- SQL mutate adopted instances behind the decision/receipt protocol. TxD replaces this
        -- explicit guard when that writer is adopted; original legacy instances remain unchanged.
        CREATE FUNCTION cp6_approval_start_v2.runtime_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE i uuid;prior uuid;r jsonb; BEGIN
          IF TG_OP='TRUNCATE' THEN
            IF EXISTS(SELECT FROM cp6_approval_start_v2.starts) THEN RAISE EXCEPTION 'APP_DECISION_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
            RETURN NULL;
          END IF;
          r=CASE WHEN TG_OP='DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END;
          i=(CASE WHEN TG_TABLE_NAME='Wf_FlowInstance' THEN r->>'Id' ELSE r->>'InstanceId' END)::uuid;
          IF TG_OP='UPDATE' THEN prior=(CASE WHEN TG_TABLE_NAME='Wf_FlowInstance' THEN to_jsonb(OLD)->>'Id' ELSE to_jsonb(OLD)->>'InstanceId' END)::uuid; END IF;
          IF EXISTS(SELECT FROM cp6_approval_start_v2.starts WHERE instance_id=i)
            OR EXISTS(SELECT FROM cp6_approval_start_v2.starts WHERE instance_id=prior)
            OR (TG_OP<>'INSERT' AND TG_TABLE_NAME='Wf_FlowInstance' AND to_jsonb(OLD)->>'Creator'='oa.app/2.0')
            THEN RAISE EXCEPTION 'APP_DECISION_OWNER_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
        END $$;
        DO $$ DECLARE tbl text; BEGIN
          FOREACH tbl IN ARRAY ARRAY['Wf_FlowInstance','Wf_FlowTask','Wf_FlowToken','Wf_FlowHistory','Wf_FlowFormTo','Wf_FlowData','Wf_FlowCc','Wf_ServiceJob'] LOOP
            EXECUTE format('CREATE TRIGGER approval_runtime_root BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON public.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change()',tbl);
            EXECUTE format('CREATE TRIGGER approval_runtime_guard BEFORE INSERT OR UPDATE OR DELETE ON public.%I FOR EACH ROW EXECUTE FUNCTION cp6_approval_start_v2.runtime_guard()',tbl);
            EXECUTE format('CREATE TRIGGER approval_runtime_truncate BEFORE TRUNCATE ON public.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_approval_start_v2.runtime_guard()',tbl);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_approval_start_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_approval_intake_v2.ready() AND NOT EXISTS(
            SELECT FROM (VALUES('Wf_FlowInstance'),('Wf_FlowTask'),('Wf_FlowToken'),('Wf_FlowHistory'),('Wf_FlowFormTo'),('Wf_FlowData'),('Wf_FlowCc'),('Wf_ServiceJob')) a(tbl)
              CROSS JOIN (VALUES('approval_runtime_guard',31),('approval_runtime_truncate',34)) b(name,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('public.'||quote_ident(a.tbl)) AND t.tgname=b.name
              AND t.tgfoid='cp6_approval_start_v2.runtime_guard()'::regprocedure AND t.tgtype=b.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL))
            AND NOT EXISTS(SELECT FROM (VALUES('Wf_FlowInstance'),('Wf_FlowTask'),('Wf_FlowToken'),('Wf_FlowHistory'),('Wf_FlowFormTo'),('Wf_FlowData'),('Wf_FlowCc'),('Wf_ServiceJob')) a(tbl)
              WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('public.'||quote_ident(a.tbl)) AND t.tgname='approval_runtime_root'
                AND t.tgfoid='cp6_authentication_v1.before_change()'::regprocedure AND t.tgtype=62 AND t.tgenabled IN('O','A') AND t.tgqual IS NULL))
            AND NOT EXISTS(SELECT FROM (VALUES
              ('cp6_approval_start_v2.starts','start_root','cp6_authentication_v1.before_change()',6,false),
              ('cp6_approval_start_v2.starts','start_immutable','cp6_scope_authorization_v1.immutable()',58,false),
              ('cp6_approval_start_v2.starts','start_complete','cp6_approval_start_v2.complete()',5,true),
              ('public."Wf_FlowInstance"','start_instance_complete','cp6_approval_start_v2.complete()',5,true)) a(tbl,name,fn,typ,deferred)
              WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tbl) AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
                AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred AND t.tgqual IS NULL));
        $$;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Forward recovery only; original starts and runtime evidence are permanent.");
}
