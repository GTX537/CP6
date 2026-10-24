using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261024025500_SampleDepartmentOwnerV1")]
public sealed class SampleDepartmentOwnerV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE cp6_operations_owner_v1.department_mappings DROP CONSTRAINT department_mappings_object_kind_check;
        ALTER TABLE cp6_operations_owner_v1.department_mappings ADD CONSTRAINT department_mappings_object_kind_check
          CHECK(object_kind IN ('stock','stock-transaction','stocktake','work-order','machine','replenish-order','mobile-task','work-center','cross-dock','slotting-plan','vmi-billing','kit-master','kit-order','warehouse','location','routing-rule','sample-stock'));
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
        -- New read capabilities are definitions only; no role gains an automatic grant.
        INSERT INTO public."Sys_MenuAction" ("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",v.code,v.name,0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES ('query','读取样品原记录')) AS v(code,name)
        WHERE m."MenuKey" IN ('wms-sample-stock') AND NOT EXISTS
          (SELECT 1 FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"=v.code);
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Sample authority bindings require explicit forward recovery.");
}
