using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261030009000_LocationHierarchyV1")]
public sealed class LocationHierarchyV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_location_hierarchy_v1;
        REVOKE ALL ON SCHEMA cp6_location_hierarchy_v1 FROM PUBLIC;
        CREATE TABLE cp6_location_hierarchy_v1.heads(
          tenant_id uuid NOT NULL,id uuid NOT NULL,warehouse_id uuid NOT NULL REFERENCES public."T_Warehouse"("Id"),
          revision bigint NOT NULL CHECK(revision>0),hierarchy_revision bigint NOT NULL CHECK(hierarchy_revision>0),
          PRIMARY KEY(tenant_id,id),UNIQUE(tenant_id,warehouse_id));
        CREATE TABLE cp6_location_hierarchy_v1.versions(
          tenant_id uuid NOT NULL,id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          PRIMARY KEY(tenant_id,id,revision),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,id) REFERENCES cp6_location_hierarchy_v1.heads);
        ALTER TABLE cp6_location_hierarchy_v1.heads ADD CONSTRAINT hierarchy_current_original
          FOREIGN KEY(tenant_id,id,revision) REFERENCES cp6_location_hierarchy_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_location_hierarchy_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 OR NEW.hierarchy_revision<>1 OR NOT EXISTS(SELECT FROM public."T_Warehouse" w
              WHERE w."Id"=NEW.warehouse_id AND w."TenantId"=NEW.tenant_id AND NOT w."IsDeleted")
              THEN RAISE EXCEPTION 'HIERARCHY_HEAD_INVALID';END IF;
          ELSIF (to_jsonb(NEW)-ARRAY['revision','hierarchy_revision']) IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['revision','hierarchy_revision'])
            OR NOT (NEW.revision=OLD.revision+1 AND NEW.hierarchy_revision=OLD.hierarchy_revision
              OR pg_trigger_depth()>1 AND NEW.revision=OLD.revision AND NEW.hierarchy_revision=OLD.hierarchy_revision+1)
            THEN RAISE EXCEPTION 'HIERARCHY_HEAD_INVALID';END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_location_hierarchy_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body,'UTF8')::jsonb;h cp6_location_hierarchy_v1.heads%ROWTYPE;BEGIN
          SELECT * INTO STRICT h FROM cp6_location_hierarchy_v1.heads WHERE tenant_id=NEW.tenant_id AND id=NEW.id;
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.id::text
            OR b->>'warehouseId' IS DISTINCT FROM h.warehouse_id::text OR b->>'version' IS DISTINCT FROM NEW.revision::text
            OR b->>'operationId' IS DISTINCT FROM NEW.operation_id::text OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text
            OR jsonb_typeof(b->'enabled') IS DISTINCT FROM 'boolean' OR jsonb_typeof(b->'rootLevels') IS DISTINCT FROM 'array'
            OR jsonb_typeof(b->'edges') IS DISTINCT FROM 'array' OR NEW.revision<>h.revision
            OR NEW.revision>1 AND NOT EXISTS(SELECT FROM cp6_location_hierarchy_v1.versions WHERE tenant_id=NEW.tenant_id AND id=NEW.id AND revision=NEW.revision-1)
            THEN RAISE EXCEPTION 'HIERARCHY_ORIGINAL_INVALID';END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_location_hierarchy_v1.provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE w uuid;BEGIN
          SELECT warehouse_id INTO STRICT w FROM cp6_location_hierarchy_v1.heads WHERE tenant_id=NEW.tenant_id AND id=NEW.id;
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id
            AND o.owner='Main.Wms.LocationHierarchy.v1' AND o.contract='LOCATION-HIERARCHY/1' AND o.operation_id=NEW.operation_id
            AND o.actor_id=NEW.actor_id AND o.state='COMMITTED' AND o.object_ids=ARRAY[w] AND o.xmin=pg_current_xact_id()::text::xid
            AND convert_from(o.result_bytes,'UTF8')::jsonb @> jsonb_build_object('WarehouseId',w::text,'Changed',true,
              'Reference',jsonb_build_object('Owner',o.owner,'Id',NEW.id::text,'Version',NEW.revision::text,'Digest',NEW.digest)))
            THEN RAISE EXCEPTION 'HIERARCHY_OPERATION_REQUIRED';END IF;
          RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_location_hierarchy_v1.native_change() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            IF EXISTS(SELECT FROM cp6_location_hierarchy_v1.heads) THEN RAISE EXCEPTION 'HIERARCHY_REPAIR_REQUIRED';END IF;RETURN NULL;
          END IF;
          IF TG_OP='UPDATE' AND (OLD."Id",OLD."TenantId",OLD."WarehouseCd",OLD."LocationCd",OLD."ParentLocationCd",OLD."LocationLevel",OLD."IsDeleted")
            IS NOT DISTINCT FROM (NEW."Id",NEW."TenantId",NEW."WarehouseCd",NEW."LocationCd",NEW."ParentLocationCd",NEW."LocationLevel",NEW."IsDeleted")
            THEN RETURN NULL;END IF;
          UPDATE cp6_location_hierarchy_v1.heads h SET hierarchy_revision=hierarchy_revision+1
            FROM public."T_Warehouse" w WHERE w."Id"=h.warehouse_id AND w."TenantId"=h.tenant_id
            AND ((TG_OP<>'INSERT' AND OLD."TenantId"=h.tenant_id AND OLD."WarehouseCd"=w."WarehouseCd")
              OR (TG_OP<>'DELETE' AND NEW."TenantId"=h.tenant_id AND NEW."WarehouseCd"=w."WarehouseCd"));
          RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_location_hierarchy_v1.warehouse_identity() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            IF EXISTS(SELECT FROM cp6_location_hierarchy_v1.heads) THEN RAISE EXCEPTION 'HIERARCHY_IDENTITY_RESERVED';END IF;RETURN NULL;
          END IF;
          IF EXISTS(SELECT FROM cp6_location_hierarchy_v1.heads WHERE warehouse_id=OLD."Id")
            AND (TG_OP='DELETE' OR (OLD."Id",OLD."TenantId",OLD."WarehouseCd") IS DISTINCT FROM (NEW."Id",NEW."TenantId",NEW."WarehouseCd"))
            THEN RAISE EXCEPTION 'HIERARCHY_IDENTITY_RESERVED';END IF;
          RETURN CASE WHEN TG_OP='DELETE' THEN OLD ELSE NEW END;
        END $$;
        CREATE TRIGGER hierarchy_advance BEFORE INSERT OR UPDATE ON cp6_location_hierarchy_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_location_hierarchy_v1.advance();
        CREATE TRIGGER hierarchy_heads_permanent BEFORE DELETE OR TRUNCATE ON cp6_location_hierarchy_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER hierarchy_validate BEFORE INSERT ON cp6_location_hierarchy_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_location_hierarchy_v1.validate();
        CREATE TRIGGER hierarchy_versions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_location_hierarchy_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER hierarchy_provenance AFTER INSERT ON cp6_location_hierarchy_v1.versions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_hierarchy_v1.provenance();
        CREATE TRIGGER hierarchy_native_change AFTER INSERT OR UPDATE OR DELETE ON public."T_Location" FOR EACH ROW EXECUTE FUNCTION cp6_location_hierarchy_v1.native_change();
        CREATE TRIGGER hierarchy_native_truncate BEFORE TRUNCATE ON public."T_Location" FOR EACH STATEMENT EXECUTE FUNCTION cp6_location_hierarchy_v1.native_change();
        CREATE TRIGGER hierarchy_warehouse_identity BEFORE UPDATE OR DELETE ON public."T_Warehouse" FOR EACH ROW EXECUTE FUNCTION cp6_location_hierarchy_v1.warehouse_identity();
        CREATE TRIGGER hierarchy_warehouse_truncate BEFORE TRUNCATE ON public."T_Warehouse" FOR EACH STATEMENT EXECUTE FUNCTION cp6_location_hierarchy_v1.warehouse_identity();
        CREATE FUNCTION cp6_location_hierarchy_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('cp6_location_hierarchy_v1.heads','hierarchy_advance','cp6_location_hierarchy_v1.advance()'),
            ('cp6_location_hierarchy_v1.heads','hierarchy_heads_permanent','cp6_scope_authorization_v1.immutable()'),
            ('cp6_location_hierarchy_v1.versions','hierarchy_validate','cp6_location_hierarchy_v1.validate()'),
            ('cp6_location_hierarchy_v1.versions','hierarchy_versions_immutable','cp6_scope_authorization_v1.immutable()'),
            ('cp6_location_hierarchy_v1.versions','hierarchy_provenance','cp6_location_hierarchy_v1.provenance()'),
            ('public."T_Location"','hierarchy_native_change','cp6_location_hierarchy_v1.native_change()'),
            ('public."T_Location"','hierarchy_native_truncate','cp6_location_hierarchy_v1.native_change()'),
            ('public."T_Warehouse"','hierarchy_warehouse_identity','cp6_location_hierarchy_v1.warehouse_identity()'),
            ('public."T_Warehouse"','hierarchy_warehouse_truncate','cp6_location_hierarchy_v1.warehouse_identity()')) r(tbl,name,fn)
            WHERE NOT EXISTS(SELECT FROM pg_trigger g WHERE g.tgrelid=to_regclass(r.tbl) AND g.tgname=r.name
              AND g.tgenabled IN('O','A') AND g.tgfoid=to_regprocedure(r.fn)))
        $$;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_location_hierarchy_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Hierarchy originals require forward recovery.");
}
