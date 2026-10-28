using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261030014000_WarehouseClockPolicyV1")]
public sealed class WarehouseClockPolicyV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_warehouse_clock_v1;
        REVOKE ALL ON SCHEMA cp6_warehouse_clock_v1 FROM PUBLIC;
        CREATE TABLE cp6_warehouse_clock_v1.binding(singleton boolean PRIMARY KEY CHECK(singleton),environment_id uuid NOT NULL);
        CREATE TRIGGER warehouse_clock_binding_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_warehouse_clock_v1.binding
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TABLE cp6_warehouse_clock_v1.heads(
          tenant_id uuid NOT NULL,id uuid NOT NULL,warehouse_id uuid NOT NULL REFERENCES public."T_Warehouse"("Id"),revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,id),UNIQUE(tenant_id,warehouse_id));
        CREATE TABLE cp6_warehouse_clock_v1.versions(
          tenant_id uuid NOT NULL,id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),PRIMARY KEY(tenant_id,id,revision),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,id) REFERENCES cp6_warehouse_clock_v1.heads);
        ALTER TABLE cp6_warehouse_clock_v1.heads ADD CONSTRAINT warehouse_clock_current_original FOREIGN KEY(tenant_id,id,revision)
          REFERENCES cp6_warehouse_clock_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_warehouse_clock_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 OR NOT EXISTS(SELECT FROM public."T_Warehouse" w WHERE w."TenantId"=NEW.tenant_id AND w."Id"=NEW.warehouse_id AND NOT w."IsDeleted")
              THEN RAISE EXCEPTION 'CLOCK_POLICY_NATIVE_IDENTITY_REQUIRED';END IF;
          ELSIF NEW.revision<>OLD.revision+1 OR (NEW.tenant_id,NEW.id,NEW.warehouse_id) IS DISTINCT FROM (OLD.tenant_id,OLD.id,OLD.warehouse_id)
            THEN RAISE EXCEPTION 'CLOCK_POLICY_FRONTIER_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_warehouse_clock_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body,'UTF8')::jsonb;h cp6_warehouse_clock_v1.heads%ROWTYPE;BEGIN
          SELECT * INTO STRICT h FROM cp6_warehouse_clock_v1.heads WHERE tenant_id=NEW.tenant_id AND id=NEW.id;
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.id::text
            OR b->>'warehouseId' IS DISTINCT FROM h.warehouse_id::text OR b->>'version' IS DISTINCT FROM NEW.revision::text
            OR b->>'operationId' IS DISTINCT FROM NEW.operation_id::text OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR h.revision<>NEW.revision
            OR jsonb_typeof(b->'definition'->'enabled') IS DISTINCT FROM 'boolean'
            OR NOT EXISTS(SELECT FROM cp6_warehouse_clock_v1.binding WHERE environment_id::text=b->>'environmentId')
            OR b->'source'->'reference' IS DISTINCT FROM b->'definition'->'source'
            OR b->'source'->'authority'->>'environmentId' IS DISTINCT FROM b->>'environmentId'
            OR jsonb_typeof(b->'definition'->'validFrom') IS DISTINCT FROM 'string'
            OR jsonb_typeof(b->'definition'->'validTo') IS DISTINCT FROM 'string'
            OR NEW.revision>1 AND NOT EXISTS(SELECT FROM cp6_warehouse_clock_v1.versions WHERE tenant_id=NEW.tenant_id AND id=NEW.id AND revision=NEW.revision-1)
            THEN RAISE EXCEPTION 'CLOCK_POLICY_ORIGINAL_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_warehouse_clock_v1.provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE h cp6_warehouse_clock_v1.heads%ROWTYPE;BEGIN
          SELECT * INTO STRICT h FROM cp6_warehouse_clock_v1.heads WHERE tenant_id=NEW.tenant_id AND id=NEW.id;
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.operation_id=NEW.operation_id
            AND o.owner='Main.Wms.WarehouseClockPolicy.v1' AND o.contract='WAREHOUSE-CLOCK-POLICY/1' AND o.actor_id=NEW.actor_id
            AND o.state='COMMITTED' AND o.xmin=pg_current_xact_id()::text::xid AND o.object_ids=ARRAY[h.warehouse_id]
            AND convert_from(o.result_bytes,'UTF8')::jsonb @> jsonb_build_object('WarehouseId',h.warehouse_id::text,
              'Reference',jsonb_build_object('Owner',o.owner,'Id',NEW.id::text,'Version',NEW.revision::text,'Digest',NEW.digest)))
            THEN RAISE EXCEPTION 'CLOCK_POLICY_OPERATION_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_warehouse_clock_v1.native_identity() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (NEW."Id",NEW."TenantId") IS DISTINCT FROM (OLD."Id",OLD."TenantId") AND EXISTS(SELECT FROM cp6_warehouse_clock_v1.heads WHERE warehouse_id=OLD."Id")
            THEN RAISE EXCEPTION 'CLOCK_POLICY_IDENTITY_RESERVED';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER warehouse_clock_advance BEFORE INSERT OR UPDATE ON cp6_warehouse_clock_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_clock_v1.advance();
        CREATE TRIGGER warehouse_clock_heads_permanent BEFORE DELETE OR TRUNCATE ON cp6_warehouse_clock_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER warehouse_clock_validate BEFORE INSERT ON cp6_warehouse_clock_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_clock_v1.validate();
        CREATE CONSTRAINT TRIGGER warehouse_clock_provenance AFTER INSERT ON cp6_warehouse_clock_v1.versions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_clock_v1.provenance();
        CREATE TRIGGER warehouse_clock_versions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_warehouse_clock_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER warehouse_clock_native_identity BEFORE UPDATE ON public."T_Warehouse" FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_clock_v1.native_identity();
        CREATE TABLE cp6_warehouse_clock_v1.observations(
          tenant_id uuid NOT NULL,environment_id uuid NOT NULL,warehouse_id uuid NOT NULL,id uuid NOT NULL,
          policy_id uuid NOT NULL,policy_revision bigint NOT NULL,source_digest text NOT NULL,observed_at timestamptz NOT NULL,
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),PRIMARY KEY(tenant_id,id),
          FOREIGN KEY(tenant_id,policy_id,policy_revision) REFERENCES cp6_warehouse_clock_v1.versions);
        CREATE INDEX warehouse_clock_watermark ON cp6_warehouse_clock_v1.observations(tenant_id,environment_id,observed_at DESC);
        CREATE FUNCTION cp6_warehouse_clock_v1.observation_valid() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body,'UTF8')::jsonb;p jsonb;v cp6_warehouse_clock_v1.versions%ROWTYPE;BEGIN
          SELECT x.* INTO STRICT v FROM cp6_warehouse_clock_v1.versions x JOIN cp6_warehouse_clock_v1.heads h
            ON h.tenant_id=x.tenant_id AND h.id=x.id AND h.revision=x.revision
            WHERE h.tenant_id=NEW.tenant_id AND h.warehouse_id=NEW.warehouse_id AND h.id=NEW.policy_id AND h.revision=NEW.policy_revision;
          p=convert_from(v.body,'UTF8')::jsonb;
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.id::text
            OR b->>'warehouseId' IS DISTINCT FROM NEW.warehouse_id::text OR b->>'environmentId' IS DISTINCT FROM NEW.environment_id::text
            OR p->>'environmentId' IS DISTINCT FROM NEW.environment_id::text OR p->'definition'->>'enabled' IS DISTINCT FROM 'true'
            OR b->'policy' IS DISTINCT FROM jsonb_build_object('owner','Main.Wms.WarehouseClockPolicy.v1','id',NEW.policy_id::text,'version',NEW.policy_revision::text,'digest',v.digest)
            OR b->'source' IS DISTINCT FROM p->'definition'->'source' OR b->'source'->>'digest' IS DISTINCT FROM NEW.source_digest
            OR b->>'evidenceMode' IS DISTINCT FROM p->'source'->'authority'->>'evidenceMode'
            OR (b->>'observedAt')::timestamptz IS DISTINCT FROM NEW.observed_at
            OR NOT((b->>'earliest')::timestamptz<=NEW.observed_at AND (b->>'latest')::timestamptz>=NEW.observed_at AND (b->>'readElapsedTicks')::bigint>=0)
            THEN RAISE EXCEPTION 'CLOCK_OBSERVATION_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER warehouse_clock_observation_valid BEFORE INSERT ON cp6_warehouse_clock_v1.observations FOR EACH ROW EXECUTE FUNCTION cp6_warehouse_clock_v1.observation_valid();
        CREATE TRIGGER warehouse_clock_observation_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_warehouse_clock_v1.observations FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_warehouse_clock_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('cp6_warehouse_clock_v1.binding','warehouse_clock_binding_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('cp6_warehouse_clock_v1.observations','warehouse_clock_observation_valid','cp6_warehouse_clock_v1.observation_valid()',7,false),
            ('cp6_warehouse_clock_v1.observations','warehouse_clock_observation_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('cp6_warehouse_clock_v1.heads','warehouse_clock_advance','cp6_warehouse_clock_v1.advance()',23,false),
            ('cp6_warehouse_clock_v1.heads','warehouse_clock_heads_permanent','cp6_scope_authorization_v1.immutable()',42,false),
            ('cp6_warehouse_clock_v1.versions','warehouse_clock_validate','cp6_warehouse_clock_v1.validate()',7,false),
            ('cp6_warehouse_clock_v1.versions','warehouse_clock_provenance','cp6_warehouse_clock_v1.provenance()',5,true),
            ('cp6_warehouse_clock_v1.versions','warehouse_clock_versions_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('public."T_Warehouse"','warehouse_clock_native_identity','cp6_warehouse_clock_v1.native_identity()',19,false)
          ) a(tbl,name,fn,kind,deferred) WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tbl)
            AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.kind AND t.tgenabled IN('O','A')
            AND t.tgqual IS NULL AND t.tgnargs=0 AND (NOT a.deferred OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_warehouse_clock_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_warehouse_clock_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'clock-policy','维护仓库时钟政策',10,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='wms-warehouse' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"='clock-policy');
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Clock policy and observation originals require forward recovery.");
}
