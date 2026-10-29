using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)),Migration("20261030023100_ReturnQualityV1")]
public sealed class ReturnQualityV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_return_quality_v1;
        CREATE TABLE cp6_return_quality_v1.inspections(tenant_id uuid NOT NULL,inspection_id uuid NOT NULL,
          application_id uuid NOT NULL,line_id uuid NOT NULL,revision integer NOT NULL CHECK(revision>=0),body bytea NOT NULL,
          PRIMARY KEY(tenant_id,inspection_id),UNIQUE(tenant_id,application_id,line_id),
          FOREIGN KEY(tenant_id,application_id) REFERENCES cp6_rma_formal_v1.heads(tenant_id,id),
          FOREIGN KEY(tenant_id,line_id) REFERENCES cp6_rma_formal_v1.physical_lines(tenant_id,line_id));
        CREATE TABLE cp6_return_quality_v1.decisions(tenant_id uuid NOT NULL,inspection_id uuid NOT NULL,
          revision integer NOT NULL CHECK(revision>0),body bytea NOT NULL,PRIMARY KEY(tenant_id,inspection_id,revision),
          FOREIGN KEY(tenant_id,inspection_id) REFERENCES cp6_return_quality_v1.inspections(tenant_id,inspection_id));
        CREATE TABLE cp6_return_quality_v1.observations(ordinal bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
          tenant_id uuid NOT NULL,inspection_id uuid NOT NULL,body bytea NOT NULL,
          FOREIGN KEY(tenant_id,inspection_id) REFERENCES cp6_return_quality_v1.inspections(tenant_id,inspection_id));
        CREATE INDEX return_quality_observation_head ON cp6_return_quality_v1.observations(tenant_id,inspection_id,ordinal DESC);
        CREATE TABLE cp6_return_quality_v1.outbox(tenant_id uuid NOT NULL,event_id uuid NOT NULL,inspection_id uuid NOT NULL,body bytea NOT NULL,
          PRIMARY KEY(tenant_id,event_id),FOREIGN KEY(tenant_id,inspection_id) REFERENCES cp6_return_quality_v1.inspections(tenant_id,inspection_id));
        CREATE FUNCTION cp6_return_quality_v1.preserve_inspection() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN IF TG_OP<>'UPDATE' OR NEW.tenant_id<>OLD.tenant_id OR NEW.inspection_id<>OLD.inspection_id OR NEW.application_id<>OLD.application_id
          OR NEW.line_id<>OLD.line_id OR NEW.body<>OLD.body OR NEW.revision<>OLD.revision+1 THEN RAISE EXCEPTION 'QUALITY_INSPECTION_IMMUTABLE'; END IF; RETURN NEW; END $$;
        CREATE TRIGGER preserve_inspection BEFORE UPDATE OR DELETE ON cp6_return_quality_v1.inspections FOR EACH ROW EXECUTE FUNCTION cp6_return_quality_v1.preserve_inspection();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_return_quality_v1.inspections FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable();
        DO $$ DECLARE tbl text; BEGIN FOREACH tbl IN ARRAY ARRAY['decisions','observations','outbox'] LOOP
          EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_return_quality_v1.%I FOR EACH STATEMENT EXECUTE FUNCTION cp6_rma_formal_v1.immutable()',tbl);
        END LOOP; END $$;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT coalesce(max("MenuId"),0)+1 FROM public."Sys_Menus"),'quality-return-inspection','退货质量判定','/wms/rma-applications','CircleCheck',NULL,159,true,LOCALTIMESTAMP
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='quality-return-inspection');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN(VALUES('query','读取退货检验',0),('start','建立退货检验',1),('decide','签署退货质量判定',2),('withdraw','撤回退货质量判定',3)) a(code,name,sort)
          WHERE m."MenuKey"='quality-return-inspection' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN(VALUES('inspection-start','申请退货检验',34),('quality-observe','采用退货质量结果',35)) a(code,name,sort)
          WHERE m."MenuKey"='wms-rma' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Quality decisions and RMA observations require forward recovery.");
}
