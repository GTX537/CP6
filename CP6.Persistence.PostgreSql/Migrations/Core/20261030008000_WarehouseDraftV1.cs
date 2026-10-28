using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030008000_WarehouseDraftV1")]
public sealed class WarehouseDraftV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_warehouse_draft_v1;
        REVOKE ALL ON SCHEMA cp6_warehouse_draft_v1 FROM PUBLIC;
        CREATE TABLE cp6_warehouse_draft_v1.warehouses (
          tenant_id uuid NOT NULL, id uuid PRIMARY KEY REFERENCES public."T_Warehouse"("Id"),
          site_id uuid NOT NULL, site_ref_json text NOT NULL, code text NOT NULL,
          code_key text COLLATE "C" NOT NULL CHECK(length(code_key) BETWEEN 1 AND 100),
          lifecycle text NOT NULL CHECK(lifecycle='DRAFT'), business_revision bigint NOT NULL CHECK(business_revision>0),
          operation_id uuid NOT NULL, actor_id uuid NOT NULL,
          UNIQUE(tenant_id,id), UNIQUE(tenant_id,code_key), UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,site_id) REFERENCES cp6_physical_site_identity_v1.heads(tenant_id,site_id)
        );
        CREATE FUNCTION cp6_warehouse_draft_v1.protect() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND pg_trigger_depth()>1
            AND (to_jsonb(NEW)-'business_revision')=(to_jsonb(OLD)-'business_revision')
            AND NEW.business_revision=OLD.business_revision+1 THEN RETURN NEW; END IF;
          RAISE EXCEPTION 'WAREHOUSE_DRAFT_IMMUTABLE' USING ERRCODE='23514';
        END $$;
        CREATE TRIGGER warehouse_draft_protect BEFORE UPDATE OR DELETE ON cp6_warehouse_draft_v1.warehouses
          FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_draft_v1.protect();
        CREATE TRIGGER warehouse_draft_no_truncate BEFORE TRUNCATE ON cp6_warehouse_draft_v1.warehouses
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_warehouse_draft_v1.protect();
        CREATE FUNCTION cp6_warehouse_draft_v1.original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM public."T_Warehouse" w WHERE w."Id"=NEW.id AND w."TenantId"=NEW.tenant_id
             AND NOT w."IsDeleted" AND w."WarehouseCd"=NEW.code AND w."WarehouseType" BETWEEN 1 AND 5)
            OR NEW.business_revision<>1
            OR NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id
              AND o.owner='Main.Wms.WarehouseDraft.v1' AND o.contract='WAREHOUSE-DRAFT/1'
              AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id AND o.state='COMMITTED'
              AND o.object_ids=ARRAY[NEW.id] AND o.xmin=pg_current_xact_id()::text::xid)
          THEN RAISE EXCEPTION 'WAREHOUSE_DRAFT_ORIGINAL_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER warehouse_draft_original AFTER INSERT ON cp6_warehouse_draft_v1.warehouses
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_draft_v1.original();
        CREATE FUNCTION cp6_warehouse_draft_v1.warehouse_change() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            IF EXISTS(SELECT FROM cp6_warehouse_draft_v1.warehouses) THEN
              RAISE EXCEPTION 'WAREHOUSE_IDENTITY_RESERVED' USING ERRCODE='23514'; END IF;
            RETURN NULL;
          END IF;
          IF TG_OP='DELETE' THEN
            IF EXISTS(SELECT FROM cp6_warehouse_draft_v1.warehouses WHERE tenant_id=OLD."TenantId") THEN
              RAISE EXCEPTION 'WAREHOUSE_IDENTITY_RESERVED' USING ERRCODE='23514'; END IF;
            RETURN OLD;
          END IF;
          IF (OLD."Id",OLD."TenantId",OLD."WarehouseCd") IS DISTINCT FROM (NEW."Id",NEW."TenantId",NEW."WarehouseCd")
            AND EXISTS(SELECT FROM cp6_warehouse_draft_v1.warehouses WHERE tenant_id IN (OLD."TenantId",NEW."TenantId")) THEN
            RAISE EXCEPTION 'WAREHOUSE_IDENTITY_RESERVED' USING ERRCODE='23514';
          END IF;
          IF EXISTS(SELECT FROM cp6_warehouse_draft_v1.warehouses WHERE tenant_id=OLD."TenantId" AND id=OLD."Id") THEN
            IF (to_jsonb(NEW)-ARRAY['WarehouseName','Modifier','ModifyDate','RowVersion'])
               IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['WarehouseName','Modifier','ModifyDate','RowVersion']) THEN
              RAISE EXCEPTION 'WAREHOUSE_DRAFT_WRITE_VERSION_REQUIRED' USING ERRCODE='23514'; END IF;
            IF OLD."WarehouseName" IS DISTINCT FROM NEW."WarehouseName" THEN
              UPDATE cp6_warehouse_draft_v1.warehouses SET business_revision=business_revision+1 WHERE id=NEW."Id";
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER warehouse_draft_change BEFORE UPDATE OR DELETE ON public."T_Warehouse"
          FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_draft_v1.warehouse_change();
        CREATE TRIGGER warehouse_draft_truncate BEFORE TRUNCATE ON public."T_Warehouse"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_warehouse_draft_v1.warehouse_change();
        CREATE FUNCTION cp6_warehouse_draft_v1.name_original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF OLD."WarehouseName" IS DISTINCT FROM NEW."WarehouseName"
             AND EXISTS(SELECT FROM cp6_warehouse_draft_v1.warehouses WHERE id=NEW."Id")
             AND NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW."TenantId"
                AND o.owner='Main.Wms.MasterName.wms-warehouse' AND o.contract='MASTER-NAME/1'
                AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW."Id"] AND o.xmin=pg_current_xact_id()::text::xid) THEN
             RAISE EXCEPTION 'WAREHOUSE_NAME_ORIGINAL_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER warehouse_draft_name_original AFTER UPDATE ON public."T_Warehouse"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_draft_v1.name_original();
        -- Once a tenant has adopted the new creation protocol, raw new identities cannot bypass its
        -- canonical reservation. Existing identities are not relabelled or normalized by migration.
        CREATE FUNCTION cp6_warehouse_draft_v1.creation() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF EXISTS(SELECT FROM cp6_warehouse_draft_v1.warehouses WHERE tenant_id=NEW."TenantId")
            AND NOT EXISTS(SELECT FROM cp6_warehouse_draft_v1.warehouses WHERE tenant_id=NEW."TenantId" AND id=NEW."Id") THEN
            RAISE EXCEPTION 'WAREHOUSE_DRAFT_CREATE_REQUIRED' USING ERRCODE='23514'; END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER warehouse_draft_creation AFTER INSERT ON public."T_Warehouse"
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_draft_v1.creation();
        -- Every current native public business table with a warehouse identity/code is fenced.
        -- Permission grants remain independent and never constitute admission to physical use.
        CREATE TABLE cp6_warehouse_draft_v1.consumer_fences(table_name text PRIMARY KEY, columns text[] NOT NULL);
        CREATE FUNCTION cp6_warehouse_draft_v1.use_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE column_name text; value text;
        BEGIN
          FOREACH column_name IN ARRAY TG_ARGV LOOP
            value=to_jsonb(NEW)->>column_name;
            IF value IS NULL OR value='' THEN CONTINUE; END IF;
            IF EXISTS(SELECT FROM cp6_warehouse_draft_v1.warehouses d JOIN public."T_Warehouse" w ON w."Id"=d.id
                WHERE d.tenant_id=(to_jsonb(NEW)->>'TenantId')::uuid
                AND (CASE WHEN column_name LIKE '%WarehouseId' THEN w."Id"::text=value
                      ELSE w."WarehouseCd"=value::bpchar END)) THEN
              RAISE EXCEPTION 'WAREHOUSE_NOT_ACTIVATED' USING ERRCODE='23514';
            END IF;
          END LOOP;
          RETURN NEW;
        END $$;
        DO $$ DECLARE r record; args text; BEGIN
          FOR r IN SELECT c.table_name,array_agg(c.column_name::text ORDER BY c.column_name) AS cols
            FROM information_schema.columns c JOIN information_schema.tables t USING(table_schema,table_name)
            WHERE c.table_schema='public' AND t.table_type='BASE TABLE'
              AND c.table_name NOT IN ('T_Warehouse','T_WmsRoleScope')
              AND c.column_name ~ 'Warehouse(Cd|Code|Id)$'
              AND EXISTS(SELECT FROM information_schema.columns tenant WHERE tenant.table_schema=c.table_schema
                AND tenant.table_name=c.table_name AND tenant.column_name='TenantId') GROUP BY c.table_name
          LOOP
            SELECT string_agg(quote_literal(x),',') INTO args FROM unnest(r.cols) x;
            EXECUTE format('CREATE TRIGGER warehouse_draft_use BEFORE INSERT OR UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_draft_v1.use_guard(%s)',r.table_name,args);
            INSERT INTO cp6_warehouse_draft_v1.consumer_fences VALUES(r.table_name,r.cols);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_warehouse_draft_v1.reserve_code(tenant uuid, code text) RETURNS boolean
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r record; col text; collision boolean;
        BEGIN
          FOR r IN SELECT c.table_name,array_agg(c.column_name::text ORDER BY c.column_name) AS cols
            FROM information_schema.columns c JOIN information_schema.tables t USING(table_schema,table_name)
            WHERE c.table_schema='public' AND t.table_type='BASE TABLE'
              AND c.table_name NOT IN ('T_Warehouse','T_WmsRoleScope')
              AND c.column_name ~ 'Warehouse(Cd|Code|Id)$'
              AND EXISTS(SELECT FROM information_schema.columns x WHERE x.table_schema=c.table_schema
                AND x.table_name=c.table_name AND x.column_name='TenantId') GROUP BY c.table_name ORDER BY c.table_name
          LOOP
            EXECUTE format('LOCK TABLE public.%I IN SHARE MODE',r.table_name);
            IF NOT EXISTS(SELECT FROM cp6_warehouse_draft_v1.consumer_fences f
                JOIN pg_catalog.pg_trigger g ON g.tgrelid=to_regclass(format('public.%I',f.table_name))
                WHERE f.table_name=r.table_name AND f.columns=r.cols AND g.tgname='warehouse_draft_use'
                  AND g.tgenabled IN ('O','A') AND g.tgfoid='cp6_warehouse_draft_v1.use_guard()'::regprocedure) THEN
              RAISE EXCEPTION 'WAREHOUSE_USE_FENCE_UNAVAILABLE' USING ERRCODE='23514'; END IF;
            FOREACH col IN ARRAY r.cols LOOP
              IF col LIKE '%WarehouseId' THEN CONTINUE; END IF;
              EXECUTE format('SELECT EXISTS(SELECT FROM public.%I WHERE "TenantId"=$1 AND %I=$2::bpchar)',r.table_name,col)
                INTO collision USING tenant,code;
              IF collision THEN RETURN false; END IF;
            END LOOP;
          END LOOP;
          RETURN true;
        END $$;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_warehouse_draft_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Warehouse identities require explicit forward recovery.");
}
