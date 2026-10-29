using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)),Migration("20261030023300_ReturnCorrectionV1")]
public sealed class ReturnCorrectionV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_return_correction_v1;
        CREATE TABLE cp6_return_correction_v1.proposals(tenant_id uuid NOT NULL,proposal_id uuid NOT NULL,application_id uuid NOT NULL,line_id uuid NOT NULL,
          target_id uuid NOT NULL,axis text NOT NULL CHECK(axis IN('PHYSICAL_RECEIPT','DISPOSITION')),
          state text NOT NULL CHECK(state IN('DRAFT','SUBMITTED','APPROVED','POSTED','WITHDRAWN')),body bytea NOT NULL,approval bytea,preparation bytea,result bytea,proof bytea,
          PRIMARY KEY(tenant_id,proposal_id),FOREIGN KEY(tenant_id,application_id) REFERENCES cp6_rma_formal_v1.heads(tenant_id,id),
          FOREIGN KEY(tenant_id,line_id) REFERENCES cp6_rma_formal_v1.physical_lines(tenant_id,line_id),
          CHECK((state='POSTED')=(result IS NOT NULL)),CHECK((state='WITHDRAWN')=(proof IS NOT NULL)),
          CHECK(state NOT IN('APPROVED','POSTED') OR approval IS NOT NULL),CHECK(state<>'POSTED' OR preparation IS NOT NULL));
        CREATE INDEX return_correction_target ON cp6_return_correction_v1.proposals(tenant_id,axis,target_id,state);
        CREATE INDEX return_correction_line ON cp6_return_correction_v1.proposals(tenant_id,line_id,state);
        CREATE TABLE cp6_return_correction_v1.versions(tenant_id uuid NOT NULL,proposal_id uuid NOT NULL,version integer NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,proposal_id,version),FOREIGN KEY(tenant_id,proposal_id) REFERENCES cp6_return_correction_v1.proposals(tenant_id,proposal_id));
        CREATE TABLE cp6_return_correction_v1.events(tenant_id uuid NOT NULL,event_id uuid NOT NULL,proposal_id uuid NOT NULL,action text NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,event_id),FOREIGN KEY(tenant_id,proposal_id) REFERENCES cp6_return_correction_v1.proposals(tenant_id,proposal_id));
        CREATE TABLE cp6_return_correction_v1.stock_movements(tenant_id uuid NOT NULL,proposal_id uuid NOT NULL,movement_id uuid NOT NULL,
          PRIMARY KEY(tenant_id,movement_id),FOREIGN KEY(tenant_id,proposal_id) REFERENCES cp6_return_correction_v1.proposals(tenant_id,proposal_id));
        DO $$ DECLARE tbl text; BEGIN FOREACH tbl IN ARRAY ARRAY['receipts','quality_releases','successors'] LOOP
          EXECUTE format('CREATE TABLE cp6_return_correction_v1.%I(tenant_id uuid NOT NULL,proposal_id uuid NOT NULL,body bytea NOT NULL,PRIMARY KEY(tenant_id,proposal_id),FOREIGN KEY(tenant_id,proposal_id) REFERENCES cp6_return_correction_v1.proposals(tenant_id,proposal_id))',tbl);
        END LOOP; END $$;
        DO $$ DECLARE tbl text; BEGIN FOREACH tbl IN ARRAY ARRAY['versions','events','stock_movements','receipts','quality_releases','successors'] LOOP
          EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_return_correction_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',tbl);
        END LOOP; END $$;
        CREATE FUNCTION cp6_return_correction_v1.transition() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP<>'UPDATE' OR OLD.state IN('POSTED','WITHDRAWN')
             OR (NEW.tenant_id,NEW.proposal_id,NEW.application_id,NEW.line_id,NEW.target_id,NEW.axis) IS DISTINCT FROM (OLD.tenant_id,OLD.proposal_id,OLD.application_id,OLD.line_id,OLD.target_id,OLD.axis)
             OR (OLD.state<>'DRAFT' AND NEW.body IS DISTINCT FROM OLD.body)
             OR (OLD.approval IS NOT NULL AND NEW.approval IS DISTINCT FROM OLD.approval)
             OR (OLD.preparation IS NOT NULL AND NEW.preparation IS DISTINCT FROM OLD.preparation)
             OR NOT ((OLD.state='DRAFT' AND NEW.state IN('DRAFT','SUBMITTED','WITHDRAWN'))
                 OR (OLD.state='SUBMITTED' AND NEW.state IN('APPROVED','WITHDRAWN'))
                 OR (OLD.state='APPROVED' AND NEW.state IN('APPROVED','POSTED','WITHDRAWN')))
          THEN RAISE EXCEPTION 'CORRECTION_TRANSITION_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER transition BEFORE UPDATE OR DELETE ON cp6_return_correction_v1.proposals FOR EACH ROW EXECUTE FUNCTION cp6_return_correction_v1.transition();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_return_correction_v1.proposals FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT coalesce(max("MenuId"),0)+1 FROM public."Sys_Menus"),'wms-return-correction','退货纠错冲正','/wms/rma-applications','RefreshLeft',NULL,165,true,LOCALTIMESTAMP
        WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='wms-return-correction');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.code,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN(VALUES('query',0),('draft',1),('submit',2),('bind-approval',3),('prepare',4),('apply',5),('withdraw',6)) a(code,sort)
        WHERE m."MenuKey"='wms-return-correction' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.code,40,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          JOIN(VALUES('wms-rma','correct-effect'),('wms-inbound-receipt','correct'),('wms-return-resale','correct'),('mes-return-repair','correct'),
            ('quality-return-scrap','correct'),('purchasing-return-shipment','correct'),('wms-customer-return-shipment','correct'),
            ('wms-stock-qc','correct-return-release'),('quality-return-inspection','source-void')) a(resource,code) ON a.resource=m."MenuKey"
        WHERE NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Return correction history is append-only.");
}
