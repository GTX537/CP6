using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)),Migration("20261030023200_ReturnDispositionV2")]
public sealed class ReturnDispositionV2 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_return_disposition_v2;
        CREATE TABLE cp6_return_disposition_v2.works(tenant_id uuid NOT NULL,work_id uuid NOT NULL,application_id uuid NOT NULL,line_id uuid NOT NULL,
          actor_id uuid NOT NULL,kind text NOT NULL CHECK(kind IN('RESALE_STOCK','REPAIR','SCRAP','SUPPLIER_RETURN','RETURN_TO_CUSTOMER')),
          state text NOT NULL,body bytea NOT NULL,result bytea,proof bytea,PRIMARY KEY(tenant_id,work_id),
          FOREIGN KEY(tenant_id,application_id) REFERENCES cp6_rma_formal_v1.heads(tenant_id,id),
          FOREIGN KEY(tenant_id,line_id) REFERENCES cp6_rma_formal_v1.physical_lines(tenant_id,line_id),
          CHECK((state='PREPARED' AND result IS NULL AND proof IS NULL) OR (state='APPLIED' AND result IS NOT NULL AND proof IS NULL)
            OR (state='SEALED_NO_EFFECT' AND result IS NULL AND proof IS NOT NULL)));
        CREATE INDEX return_disposition_lines ON cp6_return_disposition_v2.works(tenant_id,line_id,state);
        CREATE TABLE cp6_return_disposition_v2.receipts(tenant_id uuid NOT NULL,work_id uuid NOT NULL,owner text NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,work_id),FOREIGN KEY(tenant_id,work_id) REFERENCES cp6_return_disposition_v2.works(tenant_id,work_id));
        CREATE TABLE cp6_return_disposition_v2.quality_releases(tenant_id uuid NOT NULL,work_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,work_id),FOREIGN KEY(tenant_id,work_id) REFERENCES cp6_return_disposition_v2.works(tenant_id,work_id));
        CREATE TABLE cp6_return_disposition_v2.stock_movements(tenant_id uuid NOT NULL,work_id uuid NOT NULL,movement_id uuid NOT NULL,
          PRIMARY KEY(tenant_id,movement_id),FOREIGN KEY(tenant_id,work_id) REFERENCES cp6_return_disposition_v2.works(tenant_id,work_id));
        CREATE TABLE cp6_return_disposition_v2.outbox(tenant_id uuid NOT NULL,event_id uuid NOT NULL,work_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,event_id),FOREIGN KEY(tenant_id,work_id) REFERENCES cp6_return_disposition_v2.works(tenant_id,work_id));
        CREATE FUNCTION cp6_return_disposition_v2.work_transition() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP<>'UPDATE' OR (NEW.tenant_id,NEW.work_id,NEW.application_id,NEW.line_id,NEW.actor_id,NEW.kind,NEW.body)
             IS DISTINCT FROM (OLD.tenant_id,OLD.work_id,OLD.application_id,OLD.line_id,OLD.actor_id,OLD.kind,OLD.body)
             OR OLD.state<>'PREPARED' OR NEW.state NOT IN('APPLIED','SEALED_NO_EFFECT') THEN RAISE EXCEPTION 'DISPOSITION_TRANSITION_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER work_transition BEFORE UPDATE OR DELETE ON cp6_return_disposition_v2.works FOR EACH ROW EXECUTE FUNCTION cp6_return_disposition_v2.work_transition();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_return_disposition_v2.works FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        DO $$ DECLARE tbl text; BEGIN FOREACH tbl IN ARRAY ARRAY['receipts','quality_releases','stock_movements','outbox'] LOOP
          EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_return_disposition_v2.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',tbl);
        END LOOP; END $$;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        DO $$ DECLARE resource text; BEGIN FOREACH resource IN ARRAY ARRAY['wms-return-resale','mes-return-repair','quality-return-scrap','purchasing-return-shipment','wms-customer-return-shipment'] LOOP
          INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
          SELECT (SELECT coalesce(max("MenuId"),0)+1 FROM public."Sys_Menus"),resource,resource,'/wms/rma-applications','Switch',NULL,160,true,LOCALTIMESTAMP
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"=resource);
        END LOOP; END $$;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN(VALUES('query','读取处置',0),('prepare','准备处置',1),('post','执行处置',2),('seal','封存未执行处置',3)) a(code,name,sort)
        WHERE m."MenuKey" IN('wms-return-resale','mes-return-repair','quality-return-scrap','purchasing-return-shipment','wms-customer-return-shipment')
          AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN(VALUES('disposition-prepare','发起退货处置',36),('disposition-apply','采用退货处置',37),('disposition-seal','关闭未执行处置',38)) a(code,name,sort)
        WHERE m."MenuKey"='wms-rma' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'return-release','按退货质量判定放行准确数量',20,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='wms-stock-qc' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"='return-release');
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Return disposition and Stock effects require forward correction.");
}
