using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261103051000_NativeTaskFormToIdentityV2")]
public sealed class NativeTaskFormToIdentityV2:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE public."Wf_FlowFormTo" ADD COLUMN "NativeTaskId" uuid REFERENCES public."Wf_FlowTask"("Id") DEFERRABLE INITIALLY DEFERRED;
        CREATE INDEX "IX_Wf_FlowFormTo_NativeTask" ON public."Wf_FlowFormTo"("NativeTaskId") WHERE "NativeTaskId" IS NOT NULL;
        -- Existing rows keep their actual provenance. No guessed task linkage is backfilled.
        CREATE FUNCTION cp6_native_tasks_v2.formto_identity() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF OLD."NativeTaskId" IS NOT NULL OR NEW."NativeTaskId" IS NOT NULL THEN
            IF (to_jsonb(OLD)-ARRAY['Status','ActualHandlerId','OnBehalfOfId','HandledAt','Comment','ModifyDate','Modifier']) IS DISTINCT FROM
               (to_jsonb(NEW)-ARRAY['Status','ActualHandlerId','OnBehalfOfId','HandledAt','Comment','ModifyDate','Modifier'])
              THEN RAISE EXCEPTION 'TASK_FORMTO_IDENTITY_IMMUTABLE' USING ERRCODE='23514';END IF;
          END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_native_tasks_v2.formto_complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE f public."Wf_FlowFormTo";t public."Wf_FlowTask";BEGIN
          SELECT * INTO f FROM public."Wf_FlowFormTo" WHERE "Id"=NEW."Id";
          IF f."NativeTaskId" IS NULL THEN
            IF TG_OP='INSERT' AND EXISTS(SELECT FROM public."Wf_FlowInstance" WHERE "Id"=f."InstanceId" AND "Creator" IN('oa.app/2.0','oa.form/2.0'))
              THEN RAISE EXCEPTION 'TASK_FORMTO_IDENTITY_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
          END IF;
          SELECT * INTO t FROM public."Wf_FlowTask" WHERE "Id"=f."NativeTaskId";
          IF t."Id" IS NULL OR (t."TenantId",t."InstanceId",t."TokenId",t."StageIndex",t."StageRound") IS DISTINCT FROM
              (f."TenantId",f."InstanceId",f."TokenId",COALESCE(f."StageIndex",0),COALESCE(f."StageRound",0))
            OR (t."NodeId"::text COLLATE "C") IS DISTINCT FROM (f."NodeId"::text COLLATE "C")
            OR f."Status"=0 AND (t."AssigneeId"<>f."ExpectedHandlerId" OR t."Status" NOT IN(0,4)
              OR (SELECT count(*) FROM public."Wf_FlowFormTo" WHERE "NativeTaskId"=t."Id" AND "Status"=0)<>1)
            THEN RAISE EXCEPTION 'TASK_FORMTO_IDENTITY_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER native_formto_identity BEFORE UPDATE ON public."Wf_FlowFormTo" FOR EACH ROW EXECUTE FUNCTION cp6_native_tasks_v2.formto_identity();
        CREATE CONSTRAINT TRIGGER native_formto_complete AFTER INSERT OR UPDATE ON public."Wf_FlowFormTo" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_native_tasks_v2.formto_complete();
        ALTER FUNCTION cp6_native_tasks_v2.ready() RENAME TO ready_before_formto_identity;
        CREATE FUNCTION cp6_native_tasks_v2.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT cp6_native_tasks_v2.ready_before_formto_identity() AND NOT EXISTS(SELECT FROM (VALUES
            ('native_formto_identity','cp6_native_tasks_v2.formto_identity()',19,false),('native_formto_complete','cp6_native_tasks_v2.formto_complete()',21,true)) a(name,fn,typ,deferred)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid='public."Wf_FlowFormTo"'::regclass AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn)
              AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=0 AND t.tgdeferrable=a.deferred AND t.tginitdeferred=a.deferred));
        $$;
        REVOKE ALL ON FUNCTION cp6_native_tasks_v2.formto_identity(),cp6_native_tasks_v2.formto_complete() FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Native task lineage requires forward recovery.");
}
