using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context))]
[Migration("20261109040000_StockCreationMappingDispatchV2")]
public sealed class StockCreationMappingDispatchV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE OR REPLACE FUNCTION cp6_operations_owner_v1.mapping_validate() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE target_exists boolean;
        BEGIN
          IF NOT EXISTS(SELECT FROM public."Sys_Dept" d WHERE d."Id"=NEW.department_id AND d."TenantId"=NEW.tenant_id) THEN
            RAISE EXCEPTION 'STANDALONE_DEPARTMENT_TENANT_MISMATCH' USING ERRCODE='23514';
          END IF;
          IF TG_OP='UPDATE' AND (OLD.id,OLD.tenant_id,OLD.object_kind,OLD.object_id)
              IS DISTINCT FROM (NEW.id,NEW.tenant_id,NEW.object_kind,NEW.object_id) THEN
            RAISE EXCEPTION 'STANDALONE_OBJECT_IMMUTABLE' USING ERRCODE='23514';
          END IF;
          IF TG_OP='INSERT' OR NEW.enabled THEN
            -- The Stock executor resolves only its own fixed relation.
            IF NEW.object_kind='stock-transaction' THEN
              target_exists := EXISTS(SELECT FROM public."T_StockTransaction" o
                WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted");
            ELSIF NEW.object_kind='stock' THEN
              target_exists := EXISTS(SELECT FROM public."T_Stock" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted");
            ELSE
              target_exists := CASE NEW.object_kind
                WHEN 'plm-engineering-requirement' THEN EXISTS(SELECT FROM cp6_engineering_requirement_v1.heads o WHERE o.object_id=NEW.object_id AND o.tenant_id=NEW.tenant_id)
                WHEN 'plm-engineering-iteration' THEN EXISTS(SELECT FROM cp6_engineering_iteration_v1.heads o WHERE o.object_id=NEW.object_id AND o.tenant_id=NEW.tenant_id AND o.kind='PROJECT')
                WHEN 'transfer-request' THEN EXISTS(SELECT FROM cp6_transfer_request_v1.heads o WHERE o.id=NEW.object_id AND o.tenant_id=NEW.tenant_id)
                WHEN 'inventory-identity' THEN EXISTS(SELECT FROM cp6_inventory_identity_v1.heads o WHERE o.object_id=NEW.object_id AND o.tenant_id=NEW.tenant_id)
                WHEN 'plm-requirement-set' THEN EXISTS(SELECT FROM cp6_requirement_set_v1.heads o WHERE o.object_id=NEW.object_id AND o.tenant_id=NEW.tenant_id)
                WHEN 'business-partner' THEN EXISTS(SELECT FROM public."T_WebBusinessPartner" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
                WHEN 'tool-instance' THEN EXISTS(SELECT FROM public."T_PlateMoldStock" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
                WHEN 'plate-mold' THEN EXISTS(SELECT FROM public."T_PlateMold" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
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
                WHEN 'kit-master' THEN EXISTS(SELECT FROM public."T_KitMaster" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
                WHEN 'kit-order' THEN EXISTS(SELECT FROM public."T_KitOrder" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
                WHEN 'vmi-billing' THEN EXISTS(SELECT FROM public."T_VmiBilling" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
                WHEN 'slotting-plan' THEN EXISTS(SELECT FROM public."T_SlottingPlan" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
                WHEN 'cross-dock' THEN EXISTS(SELECT FROM public."T_CrossDockOrder" o WHERE o."Id"=NEW.object_id AND o."TenantId"=NEW.tenant_id AND NOT o."IsDeleted")
                ELSE FALSE END;
            END IF;
            IF NOT target_exists THEN
              RAISE EXCEPTION 'STANDALONE_OBJECT_UNAVAILABLE' USING ERRCODE='23514';
            END IF;
          END IF;
          NEW.revision=gen_random_uuid(); RETURN NEW;
        END $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Stock creation mapping dispatch requires forward recovery.");
}
