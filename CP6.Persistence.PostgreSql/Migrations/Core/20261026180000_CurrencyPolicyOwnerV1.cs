using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261026180000_CurrencyPolicyOwnerV1")]
public sealed class CurrencyPolicyOwnerV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_currency_policy_v1;
        REVOKE ALL ON SCHEMA cp6_currency_policy_v1 FROM PUBLIC;
        CREATE TABLE cp6_currency_policy_v1.heads (
          tenant_id uuid NOT NULL REFERENCES public."Sys_Tenants"("Id"),
          policy_id uuid NOT NULL CHECK(policy_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          code text COLLATE "C" NOT NULL CHECK(length(code) BETWEEN 1 AND 128),
          creator_id uuid NOT NULL CHECK(creator_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,policy_id), UNIQUE(tenant_id,code));
        CREATE TABLE cp6_currency_policy_v1.versions (
          tenant_id uuid NOT NULL,policy_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 65536),
          body_digest text COLLATE "C" NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          actor_id uuid NOT NULL CHECK(actor_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,policy_id,revision),
          FOREIGN KEY(tenant_id,policy_id) REFERENCES cp6_currency_policy_v1.heads DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_currency_policy_v1.heads ADD CONSTRAINT currency_head_original
          FOREIGN KEY(tenant_id,policy_id,revision) REFERENCES cp6_currency_policy_v1.versions DEFERRABLE INITIALLY DEFERRED;

        CREATE FUNCTION cp6_currency_policy_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'CURRENCY_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE FUNCTION cp6_currency_policy_v1.advance_head() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 THEN RAISE EXCEPTION 'CURRENCY_FRONTIER_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF (to_jsonb(OLD)-'revision') IS DISTINCT FROM (to_jsonb(NEW)-'revision') OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'CURRENCY_FRONTIER_INVALID' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_currency_policy_v1.validate_original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE body jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb;
        BEGIN
          IF body->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR body->>'version' IS DISTINCT FROM NEW.revision::text
             OR body->>'id' IS DISTINCT FROM NEW.policy_id::text OR NOT EXISTS
             (SELECT FROM cp6_currency_policy_v1.heads h WHERE h.tenant_id=NEW.tenant_id AND h.policy_id=NEW.policy_id
               AND h.revision=NEW.revision AND h.creator_id::text=body->>'creatorId' AND h.code=body#>>'{policy,code}') THEN
            RAISE EXCEPTION 'CURRENCY_ORIGINAL_IDENTITY_INVALID' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER currency_head_advance BEFORE INSERT OR UPDATE ON cp6_currency_policy_v1.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_currency_policy_v1.advance_head();
        CREATE TRIGGER currency_original_validate BEFORE INSERT ON cp6_currency_policy_v1.versions
          FOR EACH ROW EXECUTE FUNCTION cp6_currency_policy_v1.validate_original();
        CREATE TRIGGER currency_original_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_currency_policy_v1.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_currency_policy_v1.immutable();
        CREATE TRIGGER currency_head_no_delete BEFORE DELETE OR TRUNCATE ON cp6_currency_policy_v1.heads
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_currency_policy_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_currency_policy_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_currency_policy_v1 FROM PUBLIC;
        ALTER TABLE cp6_operations_owner_v1.department_mappings DROP CONSTRAINT department_mappings_object_kind_check;
        ALTER TABLE cp6_operations_owner_v1.department_mappings ADD CONSTRAINT department_mappings_object_kind_check
          CHECK(object_kind IN ('stock','stock-transaction','stocktake','work-order','machine','replenish-order','mobile-task','work-center','cross-dock','slotting-plan','vmi-billing','kit-master','kit-order','warehouse','location','routing-rule','sample-stock','product-master','uom-profile','currency-policy'));
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
              WHEN 'currency-policy' THEN EXISTS(SELECT FROM cp6_currency_policy_v1.heads o WHERE o.policy_id=NEW.object_id AND o.tenant_id=NEW.tenant_id)
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
        -- Define the independent entry/capabilities only. No role receives a grant.
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus"
            WHERE "MenuId">=1 AND "MenuId"<2147483647) available
            WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END
          FROM public."Sys_Menus"),'erp-currency-policy','币种政策',
          '/erp/currency-policies','Money',NULL,149,true,LOCALTIMESTAMP
        WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='erp-currency-policy');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('query','读取币种政策',0),('add','新增币种政策',1),('edit','修订及停用币种政策',2)) a(code,name,sort)
        WHERE m."MenuKey"='erp-currency-policy' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Native currency policy originals require forward recovery.");
}
