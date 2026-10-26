using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261026130000_CommercialProductIdentityV1")]
public sealed class CommercialProductIdentityV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_product_identity_v1;
        REVOKE ALL ON SCHEMA cp6_product_identity_v1 FROM PUBLIC;
        CREATE TABLE cp6_product_identity_v1.heads (
          tenant_id uuid NOT NULL,product_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,product_id));
        CREATE TABLE cp6_product_identity_v1.versions (
          tenant_id uuid NOT NULL,product_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          source_present boolean NOT NULL,source_json text NOT NULL,
          source_digest text NOT NULL CHECK(source_digest=encode(sha256(convert_to(source_json,'UTF8')),'hex')),
          observed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,product_id,revision));
        ALTER TABLE cp6_product_identity_v1.heads ADD CONSTRAINT product_identity_head_version
          FOREIGN KEY(tenant_id,product_id,revision) REFERENCES cp6_product_identity_v1.versions
          DEFERRABLE INITIALLY DEFERRED;
        -- This is a source witness, not a second editable product master or an engineering snapshot.
        CREATE FUNCTION cp6_product_identity_v1.payload(p jsonb) RETURNS text
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
          SELECT jsonb_build_object('productCd',p->'ProductCd','itemCd',p->'ItemCd',
            'branch1',p->'Branch1','branch2',p->'Branch2','branch3',p->'Branch3',
            'setProductCd',p->'SetProductCd','qtyUnitCode',p->'QtyUnit',
            'status',p->'Status','wfApproval',p->'WfApprovalFlg','mcTransferred',p->'McTransferFlg',
            'isDeleted',p->'IsDeleted','trackingMode',p->'TrackingMode')::text
        $$;
        CREATE FUNCTION cp6_product_identity_v1.capture(p jsonb,present boolean) RETURNS void
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE t uuid=(p->>'TenantId')::uuid; i uuid=(p->>'Id')::uuid; v bigint; body text;
        BEGIN
          INSERT INTO cp6_product_identity_v1.heads(tenant_id,product_id,revision) VALUES(t,i,1)
          ON CONFLICT(tenant_id,product_id) DO UPDATE SET revision=cp6_product_identity_v1.heads.revision+1
          RETURNING revision INTO v;
          body=cp6_product_identity_v1.payload(p);
          INSERT INTO cp6_product_identity_v1.versions(tenant_id,product_id,revision,source_present,source_json,source_digest)
          VALUES(t,i,v,present,body,encode(sha256(convert_to(body,'UTF8')),'hex'));
        END $$;
        CREATE FUNCTION cp6_product_identity_v1.record_change() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='DELETE' THEN PERFORM cp6_product_identity_v1.capture(to_jsonb(OLD),false); RETURN OLD; END IF;
          IF TG_OP='UPDATE' AND (OLD."TenantId",OLD."Id") IS DISTINCT FROM (NEW."TenantId",NEW."Id") THEN
            PERFORM cp6_product_identity_v1.capture(to_jsonb(OLD),false);
          END IF;
          PERFORM cp6_product_identity_v1.capture(to_jsonb(NEW),true); RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_product_identity_v1.immutable() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'PRODUCT_IDENTITY_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE FUNCTION cp6_product_identity_v1.advance_head() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (OLD.tenant_id,OLD.product_id) IS DISTINCT FROM (NEW.tenant_id,NEW.product_id)
            OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'PRODUCT_IDENTITY_FRONTIER_INVALID' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER product_identity_head_monotonic BEFORE UPDATE ON cp6_product_identity_v1.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_product_identity_v1.advance_head();
        CREATE TRIGGER product_identity_original_immutable BEFORE UPDATE OR DELETE ON cp6_product_identity_v1.versions
          FOR EACH ROW EXECUTE FUNCTION cp6_product_identity_v1.immutable();
        CREATE TRIGGER product_identity_original_no_truncate BEFORE TRUNCATE ON cp6_product_identity_v1.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_product_identity_v1.immutable();
        CREATE TRIGGER product_identity_head_no_delete BEFORE DELETE ON cp6_product_identity_v1.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_product_identity_v1.immutable();
        CREATE TRIGGER product_identity_head_no_truncate BEFORE TRUNCATE ON cp6_product_identity_v1.heads
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_product_identity_v1.immutable();
        LOCK TABLE public."T_ProductMaster" IN SHARE ROW EXCLUSIVE MODE;
        CREATE TRIGGER product_identity_capture AFTER INSERT OR UPDATE OR DELETE ON public."T_ProductMaster"
          FOR EACH ROW EXECUTE FUNCTION cp6_product_identity_v1.record_change();
        CREATE TRIGGER product_identity_no_truncate BEFORE TRUNCATE ON public."T_ProductMaster"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_product_identity_v1.immutable();
        -- Existing rows start at the actual migration observation; no earlier history is invented.
        SELECT cp6_product_identity_v1.capture(to_jsonb(p),true) FROM public."T_ProductMaster" p ORDER BY p."TenantId",p."Id";
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_product_identity_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_product_identity_v1 FROM PUBLIC;
        ALTER TABLE cp6_operations_owner_v1.department_mappings DROP CONSTRAINT department_mappings_object_kind_check;
        ALTER TABLE cp6_operations_owner_v1.department_mappings ADD CONSTRAINT department_mappings_object_kind_check
          CHECK(object_kind IN ('stock','stock-transaction','stocktake','work-order','machine','replenish-order','mobile-task','work-center','cross-dock','slotting-plan','vmi-billing','kit-master','kit-order','warehouse','location','routing-rule','sample-stock','product-master'));
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
              WHEN 'stock' THEN EXISTS(SELECT FROM public."T_Stock" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'stock-transaction' THEN EXISTS(SELECT FROM public."T_StockTransaction" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'stocktake' THEN EXISTS(SELECT FROM public."T_StockTake" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'work-order' THEN EXISTS(SELECT FROM public."T_WorkOrder" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted" AND (o."WebOrderNo" IS NULL OR o."WebOrderNo"=''))
              WHEN 'machine' THEN EXISTS(SELECT FROM public."M_Machine" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'replenish-order' THEN EXISTS(SELECT FROM public."T_ReplenishOrder" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'mobile-task' THEN EXISTS(SELECT FROM public."T_MobileTask" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
              WHEN 'work-center' THEN EXISTS(SELECT FROM public."T_WorkCenter" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
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
        -- Capability definition only; no automatic role grant.
        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'query','读取产品原身份',0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='erp-product' AND NOT EXISTS
          (SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='query');
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Product source history requires explicit forward recovery.");
}
