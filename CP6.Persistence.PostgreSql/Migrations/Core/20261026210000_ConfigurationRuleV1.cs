using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261026210000_ConfigurationRuleV1")]
public sealed class ConfigurationRuleV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_configuration_rule_v1;
        REVOKE ALL ON SCHEMA cp6_configuration_rule_v1 FROM PUBLIC;
        CREATE TABLE cp6_configuration_rule_v1.heads (
          tenant_id uuid NOT NULL REFERENCES public."Sys_Tenants"("Id"),rule_id uuid NOT NULL,creator_id uuid NOT NULL,
          product_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),active_version bigint CHECK(active_version>0 AND active_version<=revision),
          CHECK(rule_id<>'00000000-0000-0000-0000-000000000000'::uuid AND creator_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          PRIMARY KEY(tenant_id,rule_id),UNIQUE(tenant_id,product_id),
          FOREIGN KEY(tenant_id,product_id) REFERENCES cp6_engineering_product_v1.heads(tenant_id,product_id));
        CREATE TABLE cp6_configuration_rule_v1.versions (
          tenant_id uuid NOT NULL,rule_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 8388608),
          body_digest text COLLATE "C" NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          actor_id uuid NOT NULL CHECK(actor_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(tenant_id,rule_id,revision),
          FOREIGN KEY(tenant_id,rule_id) REFERENCES cp6_configuration_rule_v1.heads DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_configuration_rule_v1.heads ADD CONSTRAINT configuration_rule_original
          FOREIGN KEY(tenant_id,rule_id,revision) REFERENCES cp6_configuration_rule_v1.versions DEFERRABLE INITIALLY DEFERRED;
        ALTER TABLE cp6_configuration_rule_v1.heads ADD CONSTRAINT configuration_rule_active
          FOREIGN KEY(tenant_id,rule_id,active_version) REFERENCES cp6_configuration_rule_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_configuration_rule_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'CONFIGURATION_RULE_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE FUNCTION cp6_configuration_rule_v1.advance_head() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 OR NEW.active_version IS NOT NULL THEN RAISE EXCEPTION 'CONFIGURATION_RULE_FRONTIER_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF (to_jsonb(OLD)-'revision'-'active_version') IS DISTINCT FROM (to_jsonb(NEW)-'revision'-'active_version') OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'CONFIGURATION_RULE_FRONTIER_INVALID' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_configuration_rule_v1.validate_original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE body jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb; previous jsonb; active bigint; expected_active bigint; old_digest text;
        BEGIN
          SELECT active_version INTO active FROM cp6_configuration_rule_v1.heads WHERE tenant_id=NEW.tenant_id AND rule_id=NEW.rule_id;
          IF body->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR body->>'id' IS DISTINCT FROM NEW.rule_id::text
             OR body->>'version' IS DISTINCT FROM NEW.revision::text OR body->>'actorId' IS DISTINCT FROM NEW.actor_id::text
             OR body->>'etag' IS NULL OR body->>'etag'='00000000-0000-0000-0000-000000000000'
             OR body#>>'{body,scope}' IS DISTINCT FROM 'ENGINEERING_OPTIONS_ONLY'
             OR NOT EXISTS(SELECT FROM cp6_configuration_rule_v1.heads h WHERE h.tenant_id=NEW.tenant_id AND h.rule_id=NEW.rule_id
               AND h.revision=NEW.revision AND h.creator_id::text=body->>'creatorId' AND h.product_id::text=body#>>'{body,productRef,id}') THEN
            RAISE EXCEPTION 'CONFIGURATION_RULE_IDENTITY_INVALID' USING ERRCODE='23514';
          END IF;
          IF NEW.revision=1 THEN
            IF body->>'parentVersion' IS NOT NULL OR body->>'action' IS DISTINCT FROM 'CREATE' OR body->>'state' IS DISTINCT FROM 'DRAFT'
               OR body->>'previousActiveRef' IS NOT NULL THEN RAISE EXCEPTION 'CONFIGURATION_RULE_PARENT_INVALID' USING ERRCODE='23514'; END IF;
          ELSE
            SELECT convert_from(body_bytes,'UTF8')::jsonb INTO previous FROM cp6_configuration_rule_v1.versions
              WHERE tenant_id=NEW.tenant_id AND rule_id=NEW.rule_id AND revision=NEW.revision-1;
            IF previous IS NULL OR body->>'parentVersion' IS DISTINCT FROM (NEW.revision-1)::text OR body->>'etag'=previous->>'etag'
              OR body->>'action' NOT IN ('REVISE','SUBMIT','APPROVE','RETURN','WITHDRAW') THEN
              RAISE EXCEPTION 'CONFIGURATION_RULE_PARENT_INVALID' USING ERRCODE='23514'; END IF;
            expected_active=CASE WHEN previous->>'action'='APPROVE' THEN NEW.revision-1 WHEN previous->>'action'='WITHDRAW' THEN NULL
              ELSE (previous#>>'{previousActiveRef,version}')::bigint END;
            IF (body#>>'{previousActiveRef,version}')::bigint IS DISTINCT FROM expected_active THEN
              RAISE EXCEPTION 'CONFIGURATION_RULE_ACTIVE_INVALID' USING ERRCODE='23514'; END IF;
            IF expected_active IS NOT NULL THEN
              SELECT body_digest INTO old_digest FROM cp6_configuration_rule_v1.versions WHERE tenant_id=NEW.tenant_id AND rule_id=NEW.rule_id AND revision=expected_active;
              IF body#>>'{previousActiveRef,owner}' IS DISTINCT FROM 'Main.Plm.ConfigurationRule.v1' OR body#>>'{previousActiveRef,id}' IS DISTINCT FROM NEW.rule_id::text
                OR body#>>'{previousActiveRef,digest}' IS DISTINCT FROM old_digest THEN RAISE EXCEPTION 'CONFIGURATION_RULE_ACTIVE_INVALID' USING ERRCODE='23514'; END IF;
            END IF;
            IF body->>'action' <> 'REVISE' AND body->'body' IS DISTINCT FROM previous->'body' THEN
              RAISE EXCEPTION 'CONFIGURATION_RULE_CONTENT_INVALID' USING ERRCODE='23514'; END IF;
            IF NOT (CASE body->>'action'
              WHEN 'REVISE' THEN previous->>'state'<>'UNDER_REVIEW' AND body->>'state'='DRAFT'
              WHEN 'SUBMIT' THEN previous->>'state'='DRAFT' AND body->>'state'='UNDER_REVIEW'
              WHEN 'APPROVE' THEN previous->>'state'='UNDER_REVIEW' AND body->>'state'='APPROVED'
              WHEN 'RETURN' THEN previous->>'state'='UNDER_REVIEW' AND body->>'state'='DRAFT'
              WHEN 'WITHDRAW' THEN expected_active IS NOT NULL AND body->>'state'=CASE WHEN previous->>'state'='APPROVED' THEN 'WITHDRAWN' ELSE previous->>'state' END
              ELSE FALSE END) THEN RAISE EXCEPTION 'CONFIGURATION_RULE_STATE_INVALID' USING ERRCODE='23514'; END IF;
          END IF;
          expected_active=CASE WHEN body->>'action'='APPROVE' THEN NEW.revision WHEN body->>'action'='WITHDRAW' THEN NULL ELSE expected_active END;
          IF active IS DISTINCT FROM expected_active THEN RAISE EXCEPTION 'CONFIGURATION_RULE_ACTIVE_INVALID' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER configuration_rule_advance BEFORE INSERT OR UPDATE ON cp6_configuration_rule_v1.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_configuration_rule_v1.advance_head();
        CREATE TRIGGER configuration_rule_original_validate BEFORE INSERT ON cp6_configuration_rule_v1.versions
          FOR EACH ROW EXECUTE FUNCTION cp6_configuration_rule_v1.validate_original();
        CREATE TRIGGER configuration_rule_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_configuration_rule_v1.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_configuration_rule_v1.immutable();
        CREATE TRIGGER configuration_rule_no_delete BEFORE DELETE OR TRUNCATE ON cp6_configuration_rule_v1.heads
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_configuration_rule_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_configuration_rule_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_configuration_rule_v1 FROM PUBLIC;
        ALTER TABLE cp6_operations_owner_v1.department_mappings DROP CONSTRAINT department_mappings_object_kind_check;
        ALTER TABLE cp6_operations_owner_v1.department_mappings ADD CONSTRAINT department_mappings_object_kind_check
          CHECK(object_kind IN ('stock','stock-transaction','stocktake','work-order','machine','replenish-order','mobile-task','work-center','cross-dock','slotting-plan','vmi-billing','kit-master','kit-order','warehouse','location','routing-rule','sample-stock','product-master','uom-profile','currency-policy','plm-engineering-product','plm-engineering-revision','plm-configuration-rule'));
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
        -- Define the independent entry/capabilities only. No role receives a grant.
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus"
            WHERE "MenuId">=1 AND "MenuId"<2147483647) available
            WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END
          FROM public."Sys_Menus"),'plm-configuration-rule','工程选项规则',
          '/plm/configuration-rules','Connection',NULL,150,true,LOCALTIMESTAMP
        WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='plm-configuration-rule');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('query','读取规则',0),('add','创建规则',1),('edit','修改草稿',2),('submit','提交审核',3),('approve','批准或退回',4),('withdraw','撤回批准',5)) a(code,name,sort)
        WHERE m."MenuKey"='plm-configuration-rule' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Configuration rule history requires forward recovery.");
}
