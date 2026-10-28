using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261030010000_LocationDraftV1")]
public sealed class LocationDraftV1 : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE public."T_Location" ADD COLUMN "IsInitializationQuarantined" boolean NOT NULL DEFAULT false;
        -- Keep the old consumer namespace unique. Quarantined identities are warehouse-scoped.
        DROP INDEX public."IX_T_Location_LocationCd";
        CREATE UNIQUE INDEX "IX_T_Location_LocationCd" ON public."T_Location"("TenantId","LocationCd") WHERE NOT "IsInitializationQuarantined";
        CREATE UNIQUE INDEX "IX_T_Location_TenantId_WarehouseCd_LocationCd" ON public."T_Location"("TenantId","WarehouseCd","LocationCd");
        CREATE SCHEMA cp6_location_draft_v1;
        REVOKE ALL ON SCHEMA cp6_location_draft_v1 FROM PUBLIC;
        CREATE TABLE cp6_location_draft_v1.locations(
          tenant_id uuid NOT NULL,id uuid PRIMARY KEY REFERENCES public."T_Location"("Id") DEFERRABLE INITIALLY DEFERRED,
          warehouse_id uuid NOT NULL,parent_id uuid REFERENCES cp6_location_draft_v1.locations(id),
          code_key text COLLATE "C" NOT NULL CHECK(length(code_key) BETWEEN 1 AND 300),
          lifecycle text NOT NULL CHECK(lifecycle='DRAFT'),source_kind text NOT NULL CHECK(source_kind='MANUAL'),
          initialization_state text NOT NULL CHECK(initialization_state='QUARANTINED'),
          provenance text NOT NULL CHECK(provenance='MANUAL_CREATE'),legacy_provenance text NOT NULL CHECK(legacy_provenance='NEW_TECHNICAL_PLACEHOLDER'),
          business_revision bigint NOT NULL CHECK(business_revision>0),control_revision bigint NOT NULL CHECK(control_revision=1),
          policy_id uuid NOT NULL,policy_revision bigint NOT NULL,hierarchy_before bigint NOT NULL,
          operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          fence_capability text NOT NULL CHECK(fence_capability='Main.Wms.LocationInitialization.DraftWarehouse.v1'),
          UNIQUE(tenant_id,id),UNIQUE(tenant_id,warehouse_id,code_key),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,warehouse_id) REFERENCES cp6_warehouse_draft_v1.warehouses(tenant_id,id),
          FOREIGN KEY(tenant_id,policy_id,policy_revision) REFERENCES cp6_location_hierarchy_v1.versions(tenant_id,id,revision)
        );
        CREATE FUNCTION cp6_location_draft_v1.protect() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='UPDATE' AND pg_trigger_depth()>1
            AND (to_jsonb(NEW)-'business_revision')=(to_jsonb(OLD)-'business_revision')
            AND NEW.business_revision=OLD.business_revision+1 THEN RETURN NEW;END IF;
          RAISE EXCEPTION 'LOCATION_INITIALIZATION_IMMUTABLE' USING ERRCODE='23514';
        END $$;
        CREATE TRIGGER location_draft_protect BEFORE UPDATE OR DELETE ON cp6_location_draft_v1.locations FOR EACH ROW EXECUTE FUNCTION cp6_location_draft_v1.protect();
        CREATE TRIGGER location_draft_no_truncate BEFORE TRUNCATE ON cp6_location_draft_v1.locations FOR EACH STATEMENT EXECUTE FUNCTION cp6_location_draft_v1.protect();
        CREATE FUNCTION cp6_location_draft_v1.native_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            IF EXISTS(SELECT FROM cp6_location_draft_v1.locations) THEN RAISE EXCEPTION 'LOCATION_IDENTITY_RESERVED' USING ERRCODE='23514';END IF;RETURN NULL;
          END IF;
          IF TG_OP='DELETE' THEN
            IF OLD."IsInitializationQuarantined" THEN RAISE EXCEPTION 'LOCATION_IDENTITY_RESERVED' USING ERRCODE='23514';END IF;RETURN OLD;
          END IF;
          IF TG_OP='UPDATE' THEN
            IF OLD."IsInitializationQuarantined" IS DISTINCT FROM NEW."IsInitializationQuarantined" THEN
              RAISE EXCEPTION 'LOCATION_INITIALIZATION_IMMUTABLE' USING ERRCODE='23514';END IF;
            IF OLD."IsInitializationQuarantined" THEN
              IF (to_jsonb(NEW)-ARRAY['LocationName','Modifier','ModifyDate','RowVersion'])
                IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['LocationName','Modifier','ModifyDate','RowVersion']) THEN
                RAISE EXCEPTION 'LOCATION_DRAFT_WRITE_VERSION_REQUIRED' USING ERRCODE='23514';END IF;
              IF NEW."LocationName" IS DISTINCT FROM OLD."LocationName" THEN
                UPDATE cp6_location_draft_v1.locations SET business_revision=business_revision+1 WHERE id=NEW."Id";
              END IF;
            END IF;
          ELSIF NEW."IsInitializationQuarantined" THEN
            IF NOT NEW."IsBlocked" OR NEW."IsPickable" OR NEW."IsDeleted" OR NEW."CapacityQty"<>0 OR NEW."ReservedCapacityQty"<>0
              OR NEW."AreaCd" IS NOT NULL OR NEW."AllowedProductType" IS NOT NULL OR NEW."XCoord" IS NOT NULL
              OR NEW."YCoord" IS NOT NULL OR NEW."ZCoord" IS NOT NULL OR NEW."Barcode" IS NOT NULL
              OR NOT EXISTS(SELECT FROM cp6_location_draft_v1.locations d JOIN public."T_Warehouse" w ON w."Id"=d.warehouse_id
                WHERE d.id=NEW."Id" AND d.tenant_id=NEW."TenantId" AND w."WarehouseCd"=NEW."WarehouseCd"
                  AND d.xmin=pg_current_xact_id()::text::xid) THEN
              RAISE EXCEPTION 'LOCATION_INITIALIZATION_REQUIRED' USING ERRCODE='23514';END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER location_draft_native BEFORE INSERT OR UPDATE OR DELETE ON public."T_Location" FOR EACH ROW EXECUTE FUNCTION cp6_location_draft_v1.native_guard();
        CREATE TRIGGER location_draft_native_truncate BEFORE TRUNCATE ON public."T_Location" FOR EACH STATEMENT EXECUTE FUNCTION cp6_location_draft_v1.native_guard();
        CREATE FUNCTION cp6_location_draft_v1.original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE n public."T_Location";p public."T_Location";BEGIN
          SELECT * INTO STRICT n FROM public."T_Location" WHERE "Id"=NEW.id AND "TenantId"=NEW.tenant_id;
          IF NOT n."IsInitializationQuarantined" OR NEW.business_revision<>1 OR NOT n."IsBlocked" OR n."IsPickable"
            OR n."CapacityQty"<>0 OR n."ReservedCapacityQty"<>0 OR n."IsDeleted" THEN
            RAISE EXCEPTION 'LOCATION_INITIALIZATION_REQUIRED' USING ERRCODE='23514';END IF;
          IF NEW.parent_id IS NULL THEN
            IF n."ParentLocationCd" IS NOT NULL THEN RAISE EXCEPTION 'LOCATION_PARENT_MISMATCH' USING ERRCODE='23514';END IF;
          ELSE
            SELECT * INTO STRICT p FROM public."T_Location" WHERE "Id"=NEW.parent_id AND "TenantId"=NEW.tenant_id;
            IF p."WarehouseCd"<>n."WarehouseCd" OR p."LocationCd" IS DISTINCT FROM n."ParentLocationCd"
              OR p."LocationLevel">=n."LocationLevel" OR p."IsDeleted" THEN RAISE EXCEPTION 'LOCATION_PARENT_MISMATCH' USING ERRCODE='23514';END IF;
          END IF;
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id
            AND o.owner='Main.Wms.LocationDraft.v1' AND o.contract='LOCATION-DRAFT/1' AND o.operation_id=NEW.operation_id
            AND o.actor_id=NEW.actor_id AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.id]
            AND o.xmin=pg_current_xact_id()::text::xid
            AND convert_from(o.result_bytes,'UTF8')::jsonb @> jsonb_build_object('WarehouseId',NEW.warehouse_id::text,
              'LocationId',NEW.id::text,'BusinessRevision','1','Effect','CREATED_QUARANTINED_DRAFT')) THEN
            RAISE EXCEPTION 'LOCATION_DRAFT_ORIGINAL_REQUIRED' USING ERRCODE='23514';END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER location_draft_original AFTER INSERT ON cp6_location_draft_v1.locations DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_draft_v1.original();
        CREATE FUNCTION cp6_location_draft_v1.name_original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NEW."IsInitializationQuarantined" AND OLD."LocationName" IS DISTINCT FROM NEW."LocationName"
            AND NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW."TenantId"
              AND o.owner='Main.Wms.MasterName.wms-location' AND o.contract='MASTER-NAME/1' AND o.state='COMMITTED'
              AND o.object_ids=ARRAY[NEW."Id"] AND o.xmin=pg_current_xact_id()::text::xid) THEN
              RAISE EXCEPTION 'LOCATION_NAME_ORIGINAL_REQUIRED' USING ERRCODE='23514';END IF;RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER location_draft_name_original AFTER UPDATE ON public."T_Location" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_draft_v1.name_original();
        -- The exception is an initialized native identity, never admission to stock/task use.
        CREATE OR REPLACE FUNCTION cp6_warehouse_draft_v1.use_guard() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE column_name text;value text;BEGIN
          IF TG_TABLE_SCHEMA='public' AND TG_TABLE_NAME='T_Location' AND (to_jsonb(NEW)->>'IsInitializationQuarantined')::boolean
            AND EXISTS(SELECT FROM cp6_location_draft_v1.locations d JOIN public."T_Warehouse" w ON w."Id"=d.warehouse_id
              WHERE d.id=(to_jsonb(NEW)->>'Id')::uuid AND d.tenant_id=(to_jsonb(NEW)->>'TenantId')::uuid
                AND w."WarehouseCd"=(to_jsonb(NEW)->>'WarehouseCd')::bpchar
                AND (TG_OP='UPDATE' OR d.xmin=pg_current_xact_id()::text::xid)) THEN RETURN NEW;END IF;
          FOREACH column_name IN ARRAY TG_ARGV LOOP
            value=to_jsonb(NEW)->>column_name;IF value IS NULL OR value='' THEN CONTINUE;END IF;
            IF EXISTS(SELECT FROM cp6_warehouse_draft_v1.warehouses d JOIN public."T_Warehouse" w ON w."Id"=d.id
              WHERE d.tenant_id=(to_jsonb(NEW)->>'TenantId')::uuid AND
              (CASE WHEN column_name LIKE '%WarehouseId' THEN w."Id"::text=value ELSE w."WarehouseCd"=value::bpchar END)) THEN
              RAISE EXCEPTION 'WAREHOUSE_NOT_ACTIVATED' USING ERRCODE='23514';END IF;
          END LOOP;RETURN NEW;
        END $$;
        -- These legacy rows identify a Location only through the old global code namespace.
        -- Preserve an actual non-quarantined legacy identity when another warehouse uses the same code.
        CREATE FUNCTION cp6_location_draft_v1.unscoped_use() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE col text;value text;tenant uuid;BEGIN
          tenant=(to_jsonb(NEW)->>'TenantId')::uuid;
          FOREACH col IN ARRAY TG_ARGV LOOP
            IF col='TargetKey' AND (to_jsonb(NEW)->>'BarcodeType') IS DISTINCT FROM 'LOCATION' THEN CONTINUE;END IF;
            value=to_jsonb(NEW)->>col;IF value IS NULL OR value='' THEN CONTINUE;END IF;
            IF EXISTS(SELECT FROM public."T_Location" l WHERE l."TenantId"=tenant AND l."IsInitializationQuarantined" AND l."LocationCd"=value::bpchar)
              AND NOT EXISTS(SELECT FROM public."T_Location" l WHERE l."TenantId"=tenant AND NOT l."IsInitializationQuarantined" AND NOT l."IsDeleted" AND l."LocationCd"=value::bpchar)
              THEN RAISE EXCEPTION 'LOCATION_NOT_INITIALIZED' USING ERRCODE='23514';END IF;
          END LOOP;RETURN NEW;
        END $$;
        CREATE TRIGGER location_draft_unscoped BEFORE INSERT OR UPDATE ON public."T_InkLot" FOR EACH ROW EXECUTE FUNCTION cp6_location_draft_v1.unscoped_use('LocationCd');
        CREATE TRIGGER location_draft_unscoped BEFORE INSERT OR UPDATE ON public."T_BarcodeAlias" FOR EACH ROW EXECUTE FUNCTION cp6_location_draft_v1.unscoped_use('LocationCd','TargetKey');
        CREATE TRIGGER location_draft_unscoped BEFORE INSERT OR UPDATE ON public."T_InboundOrderDetail" FOR EACH ROW EXECUTE FUNCTION cp6_location_draft_v1.unscoped_use('ExpectedLocationCd');
        CREATE TRIGGER location_draft_unscoped BEFORE INSERT OR UPDATE ON public."T_InboundReceiptDetail" FOR EACH ROW EXECUTE FUNCTION cp6_location_draft_v1.unscoped_use('LocationCd');
        CREATE TRIGGER location_draft_unscoped BEFORE INSERT OR UPDATE ON public."T_RmaDetail" FOR EACH ROW EXECUTE FUNCTION cp6_location_draft_v1.unscoped_use('DestLocationCd');
        CREATE FUNCTION cp6_location_draft_v1.reserve_code(tenant uuid,code text) RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r record;collision boolean;BEGIN
          IF EXISTS(SELECT FROM public."T_Location" l WHERE l."TenantId"=tenant AND NOT l."IsInitializationQuarantined" AND NOT l."IsDeleted" AND l."LocationCd"=code::bpchar) THEN RETURN true;END IF;
          FOR r IN SELECT * FROM (VALUES('T_InkLot','LocationCd'),('T_BarcodeAlias','LocationCd'),
            ('T_InboundOrderDetail','ExpectedLocationCd'),('T_InboundReceiptDetail','LocationCd'),('T_RmaDetail','DestLocationCd')) c(tbl,col)
          LOOP
            EXECUTE format('SELECT EXISTS(SELECT FROM public.%I WHERE "TenantId"=$1 AND %I=$2::bpchar)',r.tbl,r.col) INTO collision USING tenant,code;
            IF collision THEN RETURN false;END IF;
          END LOOP;
          RETURN NOT EXISTS(SELECT FROM public."T_BarcodeAlias" WHERE "TenantId"=tenant AND "BarcodeType"='LOCATION' AND "TargetKey"=code::bpchar);
        END $$;
        CREATE FUNCTION cp6_location_draft_v1.ready() RETURNS boolean LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE r record;BEGIN
          IF NOT cp6_location_hierarchy_v1.ready() OR EXISTS(SELECT FROM (VALUES
            ('cp6_location_draft_v1.locations','location_draft_protect','cp6_location_draft_v1.protect()'),
            ('cp6_location_draft_v1.locations','location_draft_no_truncate','cp6_location_draft_v1.protect()'),
            ('cp6_location_draft_v1.locations','location_draft_original','cp6_location_draft_v1.original()'),
            ('public."T_Location"','location_draft_native','cp6_location_draft_v1.native_guard()'),
            ('public."T_Location"','location_draft_native_truncate','cp6_location_draft_v1.native_guard()'),
            ('public."T_Location"','location_draft_name_original','cp6_location_draft_v1.name_original()'),
            ('cp6_warehouse_draft_v1.warehouses','warehouse_draft_protect','cp6_warehouse_draft_v1.protect()'),
            ('cp6_warehouse_draft_v1.warehouses','warehouse_draft_no_truncate','cp6_warehouse_draft_v1.protect()'),
            ('cp6_warehouse_draft_v1.warehouses','warehouse_draft_original','cp6_warehouse_draft_v1.original()'),
            ('public."T_Warehouse"','warehouse_draft_change','cp6_warehouse_draft_v1.warehouse_change()'),
            ('public."T_Warehouse"','warehouse_draft_truncate','cp6_warehouse_draft_v1.warehouse_change()'),
            ('public."T_Warehouse"','warehouse_draft_creation','cp6_warehouse_draft_v1.creation()')) t(tbl,name,fn)
            WHERE NOT EXISTS(SELECT FROM pg_trigger g WHERE g.tgrelid=to_regclass(t.tbl) AND g.tgname=t.name
              AND g.tgenabled IN('O','A') AND g.tgfoid=to_regprocedure(t.fn))) THEN RETURN false;END IF;
          FOR r IN SELECT c.table_name,array_agg(c.column_name::text ORDER BY c.column_name) AS cols
            FROM information_schema.columns c JOIN information_schema.tables t USING(table_schema,table_name)
            WHERE c.table_schema='public' AND t.table_type='BASE TABLE' AND c.table_name NOT IN('T_Warehouse','T_WmsRoleScope')
              AND c.column_name ~ 'Warehouse(Cd|Code|Id)$' AND EXISTS(SELECT FROM information_schema.columns x
                WHERE x.table_schema=c.table_schema AND x.table_name=c.table_name AND x.column_name='TenantId')
            GROUP BY c.table_name ORDER BY c.table_name
          LOOP
            EXECUTE format('LOCK TABLE public.%I IN SHARE MODE',r.table_name);
            IF NOT EXISTS(SELECT FROM cp6_warehouse_draft_v1.consumer_fences f JOIN pg_trigger g
              ON g.tgrelid=to_regclass(format('public.%I',f.table_name)) WHERE f.table_name=r.table_name AND f.columns=r.cols
                AND g.tgname='warehouse_draft_use' AND g.tgenabled IN('O','A') AND g.tgfoid='cp6_warehouse_draft_v1.use_guard()'::regprocedure
                AND g.tgnargs=cardinality(r.cols) AND g.tgargs=(SELECT string_agg(convert_to(v,'UTF8')||decode('00','hex'),''::bytea ORDER BY n) FROM unnest(r.cols) WITH ORDINALITY a(v,n)))
            THEN RETURN false;END IF;
          END LOOP;
          -- Frozen reviewed native inventory; a new Location-only consumer must not escape the warehouse scan.
          IF EXISTS(SELECT FROM (
            SELECT c.table_name,array_agg(c.column_name::text ORDER BY c.column_name) cols FROM information_schema.columns c
            WHERE c.table_schema='public' AND c.column_name ~ 'Location(Cd|Code|Id)$' AND EXISTS(SELECT FROM information_schema.columns x
              WHERE x.table_schema=c.table_schema AND x.table_name=c.table_name AND x.column_name='TenantId') GROUP BY c.table_name
          ) actual FULL JOIN (VALUES
            ('Space_Location',ARRAY['LocationCode']::text[]),
            ('T_BarcodeAlias',ARRAY['LocationCd']::text[]),
            ('T_CrossDockOrder',ARRAY['TempLocationCd']::text[]),
            ('T_InboundOrderDetail',ARRAY['ExpectedLocationCd']::text[]),
            ('T_InboundReceiptDetail',ARRAY['LocationCd']::text[]),
            ('T_InkLot',ARRAY['LocationCd']::text[]),
            ('T_IotSensor',ARRAY['LocationCd']::text[]),
            ('T_KitOrder',ARRAY['KitLocationCd']::text[]),
            ('T_Location',ARRAY['LocationCd','ParentLocationCd']::text[]),
            ('T_LogisticsUnit',ARRAY['LocationCd']::text[]),
            ('T_MobileTask',ARRAY['FromLocationCd','ToLocationCd']::text[]),
            ('T_MobileTaskReservation',ARRAY['FromLocationCd','ToLocationCd']::text[]),
            ('T_OutboundOrderDetail',ARRAY['LocationCd']::text[]),
            ('T_Pallet',ARRAY['LocationCd']::text[]),
            ('T_PaperRoll',ARRAY['LocationCd']::text[]),
            ('T_PlateMoldStock',ARRAY['LocationCd']::text[]),
            ('T_RemnantMaterial',ARRAY['LocationCd']::text[]),
            ('T_ReplenishOrder',ARRAY['FromLocationCd','ToLocationCd']::text[]),
            ('T_RmaDetail',ARRAY['DestLocationCd']::text[]),
            ('T_SampleStock',ARRAY['LocationCd']::text[]),
            ('T_Stock',ARRAY['LocationCd']::text[]),
            ('T_StockSerial',ARRAY['LocationCd']::text[]),
            ('T_StockSerialTransaction',ARRAY['FromLocationCd','ToLocationCd']::text[]),
            ('T_StockTakeDetail',ARRAY['LocationCd']::text[]),
            ('T_StockTransaction',ARRAY['CounterLocationCd','LocationCd']::text[]),
            ('T_WcsTask',ARRAY['FromLocationCd','ToLocationCd']::text[]),
            ('T_WmsBin',ARRAY['LocationCode']::text[])) expected(table_name,cols) USING(table_name)
            WHERE actual.cols IS DISTINCT FROM expected.cols) THEN RETURN false;END IF;
          FOR r IN SELECT * FROM (VALUES('T_InkLot',ARRAY['LocationCd']),('T_BarcodeAlias',ARRAY['LocationCd','TargetKey']),
            ('T_InboundOrderDetail',ARRAY['ExpectedLocationCd']),('T_InboundReceiptDetail',ARRAY['LocationCd']),('T_RmaDetail',ARRAY['DestLocationCd'])) f(tbl,cols)
          LOOP
            EXECUTE format('LOCK TABLE public.%I IN SHARE MODE',r.tbl);
            IF NOT EXISTS(SELECT FROM pg_trigger g WHERE g.tgrelid=to_regclass(format('public.%I',r.tbl))
              AND g.tgname='location_draft_unscoped' AND g.tgenabled IN('O','A') AND g.tgtype=23
              AND g.tgfoid='cp6_location_draft_v1.unscoped_use()'::regprocedure AND g.tgnargs=cardinality(r.cols)
              AND g.tgargs=(SELECT string_agg(convert_to(v,'UTF8')||decode('00','hex'),''::bytea ORDER BY n) FROM unnest(r.cols) WITH ORDINALITY a(v,n))) THEN RETURN false;END IF;
          END LOOP;
          RETURN EXISTS(SELECT FROM pg_index WHERE indexrelid=to_regclass('public."IX_T_Location_TenantId_WarehouseCd_LocationCd"') AND indisunique AND indisvalid)
             AND EXISTS(SELECT FROM pg_index WHERE indexrelid=to_regclass('public."IX_T_Location_LocationCd"') AND indisunique AND indisvalid AND indpred IS NOT NULL);
        END $$;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_location_draft_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Location identities and quarantine require forward recovery.");
}
