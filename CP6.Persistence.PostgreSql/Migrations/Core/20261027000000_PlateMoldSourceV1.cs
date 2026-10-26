using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261027000000_PlateMoldSourceV1")]
public sealed class PlateMoldSourceV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_plate_mold_source_v1;
        REVOKE ALL ON SCHEMA cp6_plate_mold_source_v1 FROM PUBLIC;
        CREATE TABLE cp6_plate_mold_source_v1.heads (
          tenant_id uuid NOT NULL,object_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),PRIMARY KEY(tenant_id,object_id));
        CREATE TABLE cp6_plate_mold_source_v1.versions (
          tenant_id uuid NOT NULL,object_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),source_present boolean NOT NULL,
          source_json text NOT NULL,source_digest text NOT NULL CHECK(source_digest=encode(sha256(convert_to(source_json,'UTF8')),'hex')),
          native_token text NOT NULL,observed_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(tenant_id,object_id,revision));
        ALTER TABLE cp6_plate_mold_source_v1.heads ADD CONSTRAINT plate_mold_source_head_version
          FOREIGN KEY(tenant_id,object_id,revision) REFERENCES cp6_plate_mold_source_v1.versions DEFERRABLE INITIALLY DEFERRED;
        -- Full native identity, technical original values and native control, with exact decimal text/null.
        -- These observations are neither technical approval nor documents/physical tool qualification.
        CREATE FUNCTION cp6_plate_mold_source_v1.payload(p jsonb) RETURNS text
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
          SELECT jsonb_build_object(
            'identity',jsonb_build_object('baseCode',p->'BaseCd','number',p->'WdPtnNo','businessRevision',p->'WdRev','name',p->'VrsnName'),
            'technical',jsonb_build_object('representativeProductCode',p->'RepresentativeProductCd','newVersionCode',p->'NewVerCd',
              'processCode',p->'ProcessCd','typeClass',p->'TypeClass','endPlaceCode',p->'EndPlaceCd','sheetFlute',p->'SheetFlute',
              'paperCodeF',p->'PaperCdF','printCodeF',p->'PrintCdF','embossCodeF',p->'EmbossCdF','makerCodeF',p->'MakerCdF',
              'paperCodeC',p->'PaperCdC','printCodeC',p->'PrintCdC','embossCodeC',p->'EmbossCdC','makerCodeC',p->'MakerCdC',
              'paperCodeB',p->'PaperCdB','printCodeB',p->'PrintCdB','embossCodeB',p->'EmbossCdB','makerCodeB',p->'MakerCdB',
              'extractionDirection',p->'ExtractionDirect','duplicatePlate',p->'DuplicatePlateFlg',
              'sheetWidth',p->>'SheetWidth','sheetFlow',p->>'SheetFlow','bladeWidth',p->>'BladeWidth','bladeFlow',p->>'BladeFlow',
              'compositionQuantity',p->'CompositionQty','manufacturingQuantity',p->'MfgQty','colorQuantity',p->'ColorQty','bookQuantity',p->'BookQty',
              'flexoThickness',p->>'FlexoThick','cylinderSize',p->>'CylinderSize',
              'strippingCode',p->'StrippingCd','standingCode',p->'StandingCd','hammerCode',p->'HammerCd','oversightCode',p->'OversightCd'),
            'validity',jsonb_build_object('startDate',p->'StDate','endDate',p->'EndDate'),
            'legacyAttachmentFlags',jsonb_build_object('sheetFront',p->'AtachInfoSheetFront','sheetBack',p->'AtachInfoSheetBack',
              'actual',p->'AtachInfoActual','baseplate',p->'AtachInfoBaseplate','positive',p->'AtachInfoPositive',
              'negative',p->'AtachInfoNegative','mo',p->'AtachInfoMo','fd',p->'AtachInfoFd'),
            'requirements',jsonb_build_object('draft',p->'NeedDraft','mylar',p->'NeedMylar','galley',p->'NeedGalley','proof',p->'NeedProof',
              'blueprint',p->'NeedBlueprint','comp',p->'NeedComp','designSheet',p->'NeedDesignSheet','otherName',p->'NeedOtherName','other',p->'NeedOther'),
            'control',jsonb_build_object('isDeleted',p->'IsDeleted','creator',p->'Creator','legacyTransferStatus',p->'Status','legacyMcTransferred',p->'McTransferFlg'))::text
        $$;
        CREATE FUNCTION cp6_plate_mold_source_v1.capture(p jsonb,present boolean) RETURNS void
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE t uuid=(p->>'TenantId')::uuid; i uuid=(p->>'Id')::uuid; v bigint; body text;
          prior cp6_plate_mold_source_v1.versions%ROWTYPE;
        BEGIN
          body=cp6_plate_mold_source_v1.payload(p);
          SELECT h.revision INTO v FROM cp6_plate_mold_source_v1.heads h WHERE h.tenant_id=t AND h.object_id=i FOR UPDATE;
          IF FOUND THEN
            SELECT * INTO STRICT prior FROM cp6_plate_mold_source_v1.versions WHERE tenant_id=t AND object_id=i AND revision=v;
            IF NOT prior.source_present AND present THEN RAISE EXCEPTION 'TOOL_SOURCE_IDENTITY_REUSED' USING ERRCODE='23514'; END IF;
            IF prior.source_json=body AND prior.source_present=present THEN RETURN; END IF;
            UPDATE cp6_plate_mold_source_v1.heads SET revision=revision+1 WHERE tenant_id=t AND object_id=i RETURNING revision INTO v;
          ELSE INSERT INTO cp6_plate_mold_source_v1.heads VALUES(t,i,1);v=1;
          END IF;
          INSERT INTO cp6_plate_mold_source_v1.versions(tenant_id,object_id,revision,source_present,source_json,source_digest,native_token)
            VALUES(t,i,v,present,body,encode(sha256(convert_to(body,'UTF8')),'hex'),encode(decode(substr(p->>'RowVersion',3),'hex'),'base64'));
        END $$;
        CREATE FUNCTION cp6_plate_mold_source_v1.record_change() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='DELETE' THEN PERFORM cp6_plate_mold_source_v1.capture(to_jsonb(OLD),false);RETURN OLD; END IF;
          IF TG_OP='UPDATE' AND (OLD."TenantId",OLD."Id") IS DISTINCT FROM (NEW."TenantId",NEW."Id") THEN
            PERFORM cp6_plate_mold_source_v1.capture(to_jsonb(OLD),false);
          END IF;
          PERFORM cp6_plate_mold_source_v1.capture(to_jsonb(NEW),true);RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_plate_mold_source_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'TOOL_SOURCE_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE FUNCTION cp6_plate_mold_source_v1.advance_head() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (OLD.tenant_id,OLD.object_id) IS DISTINCT FROM (NEW.tenant_id,NEW.object_id) OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'TOOL_SOURCE_FRONTIER_INVALID' USING ERRCODE='23514';
          END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER plate_mold_source_head_monotonic BEFORE UPDATE ON cp6_plate_mold_source_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_plate_mold_source_v1.advance_head();
        CREATE TRIGGER plate_mold_source_head_no_delete BEFORE DELETE ON cp6_plate_mold_source_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_plate_mold_source_v1.immutable();
        CREATE TRIGGER plate_mold_source_head_no_truncate BEFORE TRUNCATE ON cp6_plate_mold_source_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_plate_mold_source_v1.immutable();
        CREATE TRIGGER plate_mold_source_original_immutable BEFORE UPDATE OR DELETE ON cp6_plate_mold_source_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_plate_mold_source_v1.immutable();
        CREATE TRIGGER plate_mold_source_original_no_truncate BEFORE TRUNCATE ON cp6_plate_mold_source_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_plate_mold_source_v1.immutable();
        LOCK TABLE public."T_PlateMold" IN SHARE ROW EXCLUSIVE MODE;
        CREATE TRIGGER plate_mold_source_capture AFTER INSERT OR UPDATE OR DELETE ON public."T_PlateMold" FOR EACH ROW EXECUTE FUNCTION cp6_plate_mold_source_v1.record_change();
        CREATE TRIGGER plate_mold_source_no_truncate BEFORE TRUNCATE ON public."T_PlateMold" FOR EACH STATEMENT EXECUTE FUNCTION cp6_plate_mold_source_v1.immutable();
        -- First actual observation only: do not invent earlier revisions or rewrite original business rows.
        SELECT cp6_plate_mold_source_v1.capture(to_jsonb(p),true) FROM public."T_PlateMold" p ORDER BY p."TenantId",p."Id";
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_plate_mold_source_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_plate_mold_source_v1 FROM PUBLIC;
        ALTER TABLE cp6_operations_owner_v1.department_mappings DROP CONSTRAINT department_mappings_object_kind_check;
        ALTER TABLE cp6_operations_owner_v1.department_mappings ADD CONSTRAINT department_mappings_object_kind_check
          CHECK(object_kind IN ('stock','stock-transaction','stocktake','work-order','machine','replenish-order','mobile-task','work-center','cross-dock','slotting-plan','vmi-billing','kit-master','kit-order','warehouse','location','routing-rule','sample-stock','product-master','uom-profile','currency-policy','plm-engineering-product','plm-engineering-revision','plm-configuration-rule','plate-mold'));
        CREATE OR REPLACE FUNCTION cp6_operations_owner_v1.mapping_validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM public."Sys_Dept" d WHERE d."Id"=NEW.department_id AND d."TenantId"=NEW.tenant_id) THEN
            RAISE EXCEPTION 'STANDALONE_DEPARTMENT_TENANT_MISMATCH' USING ERRCODE='23514';
          END IF;
          IF TG_OP='UPDATE' AND (OLD.id,OLD.tenant_id,OLD.object_kind,OLD.object_id)
              IS DISTINCT FROM (NEW.id,NEW.tenant_id,NEW.object_kind,NEW.object_id) THEN
            RAISE EXCEPTION 'STANDALONE_OBJECT_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          IF TG_OP='INSERT' OR NEW.enabled THEN
            IF NOT (CASE NEW.object_kind
              WHEN 'plate-mold' THEN EXISTS(SELECT FROM public."T_PlateMold" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'stock' THEN EXISTS(SELECT FROM public."T_Stock" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'stock-transaction' THEN EXISTS(SELECT FROM public."T_StockTransaction" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'stocktake' THEN EXISTS(SELECT FROM public."T_StockTake" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'work-order' THEN EXISTS(SELECT FROM public."T_WorkOrder" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted" AND (o."WebOrderNo" IS NULL OR o."WebOrderNo"=''))
              WHEN 'machine' THEN EXISTS(SELECT FROM public."M_Machine" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'replenish-order' THEN EXISTS(SELECT FROM public."T_ReplenishOrder" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'mobile-task' THEN EXISTS(SELECT FROM public."T_MobileTask" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'work-center' THEN EXISTS(SELECT FROM public."T_WorkCenter" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'currency-policy' THEN EXISTS(SELECT FROM cp6_currency_policy_v1.heads o WHERE o.policy_id=NEW.object_id AND o.tenant_id=NEW.tenant_id)
              WHEN 'plm-engineering-product' THEN EXISTS(SELECT FROM cp6_engineering_product_v1.heads o WHERE o.product_id=NEW.object_id AND o.tenant_id=NEW.tenant_id)
              WHEN 'plm-configuration-rule' THEN EXISTS(SELECT FROM cp6_configuration_rule_v1.heads o WHERE o.rule_id=NEW.object_id AND o.tenant_id=NEW.tenant_id)
              WHEN 'plm-engineering-revision' THEN EXISTS(SELECT FROM cp6_engineering_revision_v1.heads o WHERE o.revision_id=NEW.object_id AND o.tenant_id=NEW.tenant_id)
              WHEN 'uom-profile' THEN EXISTS(SELECT FROM cp6_uom_profile_v1.heads o WHERE o.profile_id=NEW.object_id AND o.tenant_id=NEW.tenant_id)
              WHEN 'product-master' THEN EXISTS(SELECT FROM public."T_ProductMaster" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'sample-stock' THEN EXISTS(SELECT FROM public."T_SampleStock" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'routing-rule' THEN EXISTS(SELECT FROM public."T_OutboundRoutingRule" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted" AND EXISTS(SELECT FROM public."T_Warehouse" w WHERE w."TenantId"=o."TenantId" AND w."WarehouseCd"=o."TargetWarehouseCd" AND NOT w."IsDeleted"))
              WHEN 'warehouse' THEN EXISTS(SELECT FROM public."T_Warehouse" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'location' THEN EXISTS(SELECT FROM public."T_Location" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted" AND EXISTS(SELECT FROM public."T_Warehouse" w WHERE w."TenantId"=o."TenantId" AND w."WarehouseCd"=o."WarehouseCd" AND NOT w."IsDeleted"))
              WHEN 'kit-master'  THEN EXISTS(SELECT FROM public."T_KitMaster" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'kit-order' THEN EXISTS(SELECT FROM public."T_KitOrder" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'vmi-billing' THEN EXISTS(SELECT FROM public."T_VmiBilling" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'slotting-plan' THEN EXISTS(SELECT FROM public."T_SlottingPlan" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'cross-dock' THEN EXISTS(SELECT FROM public."T_CrossDockOrder" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              ELSE FALSE END) THEN
              RAISE EXCEPTION 'STANDALONE_OBJECT_UNAVAILABLE' USING ERRCODE='23514';
            END IF;
          END IF;
          NEW.revision=gen_random_uuid(); RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION cp6_operations_owner_v1.mapping_validate() FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'query','读取版型工具原来源',0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='erp-plate-mold' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a
          WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='query');
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Plate mold observations require forward recovery.");
}
