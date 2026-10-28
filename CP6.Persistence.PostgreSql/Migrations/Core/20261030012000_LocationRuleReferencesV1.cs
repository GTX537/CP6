using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)),Migration("20261030012000_LocationRuleReferencesV1")]
public sealed class LocationRuleReferencesV1:Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE SCHEMA cp6_location_rule_refs_v1;
        REVOKE ALL ON SCHEMA cp6_location_rule_refs_v1 FROM PUBLIC;
        CREATE TABLE cp6_location_rule_refs_v1.heads(
          tenant_id uuid NOT NULL,id uuid NOT NULL,warehouse_id uuid NOT NULL REFERENCES public."T_Warehouse"("Id"),revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,id),UNIQUE(tenant_id,warehouse_id));
        CREATE TABLE cp6_location_rule_refs_v1.versions(
          tenant_id uuid NOT NULL,id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),PRIMARY KEY(tenant_id,id,revision),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,id) REFERENCES cp6_location_rule_refs_v1.heads);
        ALTER TABLE cp6_location_rule_refs_v1.heads ADD CONSTRAINT rule_refs_current_original FOREIGN KEY(tenant_id,id,revision)
          REFERENCES cp6_location_rule_refs_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_location_rule_refs_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 OR NOT EXISTS(SELECT FROM public."T_Warehouse" w WHERE w."TenantId"=NEW.tenant_id AND w."Id"=NEW.warehouse_id AND NOT w."IsDeleted")
              THEN RAISE EXCEPTION 'RULE_DICTIONARY_NATIVE_IDENTITY_REQUIRED';END IF;
          ELSIF NEW.revision<>OLD.revision+1 OR (NEW.tenant_id,NEW.id,NEW.warehouse_id) IS DISTINCT FROM (OLD.tenant_id,OLD.id,OLD.warehouse_id)
            THEN RAISE EXCEPTION 'RULE_DICTIONARY_FRONTIER_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_location_rule_refs_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body,'UTF8')::jsonb;h cp6_location_rule_refs_v1.heads%ROWTYPE;BEGIN
          SELECT * INTO STRICT h FROM cp6_location_rule_refs_v1.heads WHERE tenant_id=NEW.tenant_id AND id=NEW.id;
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.id::text
            OR b->>'warehouseId' IS DISTINCT FROM h.warehouse_id::text OR b->>'version' IS DISTINCT FROM NEW.revision::text
            OR b->>'operationId' IS DISTINCT FROM NEW.operation_id::text OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR h.revision<>NEW.revision
            OR jsonb_typeof(b->'definition'->'enabled') IS DISTINCT FROM 'boolean' OR jsonb_typeof(b->'units') IS DISTINCT FROM 'array'
            OR jsonb_typeof(b->'definition'->'metrics') IS DISTINCT FROM 'array' OR jsonb_typeof(b->'definition'->'precisions') IS DISTINCT FROM 'array'
            OR jsonb_typeof(b->'definition'->'domains') IS DISTINCT FROM 'array'
            OR NEW.revision>1 AND NOT EXISTS(SELECT FROM cp6_location_rule_refs_v1.versions WHERE tenant_id=NEW.tenant_id AND id=NEW.id AND revision=NEW.revision-1)
            THEN RAISE EXCEPTION 'RULE_DICTIONARY_ORIGINAL_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_location_rule_refs_v1.provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE h cp6_location_rule_refs_v1.heads%ROWTYPE;BEGIN
          SELECT * INTO STRICT h FROM cp6_location_rule_refs_v1.heads WHERE tenant_id=NEW.tenant_id AND id=NEW.id;
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.operation_id=NEW.operation_id
            AND o.owner='Main.Wms.LocationRuleReferences.v1' AND o.contract='LOCATION-RULE-REFERENCES/1' AND o.actor_id=NEW.actor_id
            AND o.state='COMMITTED' AND o.xmin=pg_current_xact_id()::text::xid AND o.object_ids=ARRAY[h.warehouse_id]
            AND convert_from(o.result_bytes,'UTF8')::jsonb @> jsonb_build_object('WarehouseId',h.warehouse_id::text,
              'Reference',jsonb_build_object('Owner',o.owner,'Id',NEW.id::text,'Version',NEW.revision::text,'Digest',NEW.digest)))
            THEN RAISE EXCEPTION 'RULE_DICTIONARY_OPERATION_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_location_rule_refs_v1.native_identity() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (NEW."Id",NEW."TenantId") IS DISTINCT FROM (OLD."Id",OLD."TenantId") AND EXISTS(SELECT FROM cp6_location_rule_refs_v1.heads WHERE warehouse_id=OLD."Id")
            THEN RAISE EXCEPTION 'RULE_DICTIONARY_IDENTITY_RESERVED';END IF;RETURN NEW;
        END $$;
        CREATE TRIGGER rule_refs_advance BEFORE INSERT OR UPDATE ON cp6_location_rule_refs_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_refs_v1.advance();
        CREATE TRIGGER rule_refs_heads_permanent BEFORE DELETE OR TRUNCATE ON cp6_location_rule_refs_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER rule_refs_validate BEFORE INSERT ON cp6_location_rule_refs_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_refs_v1.validate();
        CREATE CONSTRAINT TRIGGER rule_refs_provenance AFTER INSERT ON cp6_location_rule_refs_v1.versions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_refs_v1.provenance();
        CREATE TRIGGER rule_refs_versions_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_location_rule_refs_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER rule_refs_native_identity BEFORE UPDATE ON public."T_Warehouse" FOR EACH ROW EXECUTE FUNCTION cp6_location_rule_refs_v1.native_identity();
        CREATE FUNCTION cp6_location_rule_refs_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('cp6_location_rule_refs_v1.heads','rule_refs_advance','cp6_location_rule_refs_v1.advance()',23,false),
            ('cp6_location_rule_refs_v1.heads','rule_refs_heads_permanent','cp6_scope_authorization_v1.immutable()',42,false),
            ('cp6_location_rule_refs_v1.versions','rule_refs_validate','cp6_location_rule_refs_v1.validate()',7,false),
            ('cp6_location_rule_refs_v1.versions','rule_refs_provenance','cp6_location_rule_refs_v1.provenance()',5,true),
            ('cp6_location_rule_refs_v1.versions','rule_refs_versions_immutable','cp6_scope_authorization_v1.immutable()',58,false),
            ('public."T_Warehouse"','rule_refs_native_identity','cp6_location_rule_refs_v1.native_identity()',19,false)
          ) a(tbl,name,fn,kind,deferred) WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass(a.tbl)
            AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.kind AND t.tgenabled IN('O','A')
            AND t.tgqual IS NULL AND t.tgnargs=0 AND (NOT a.deferred OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_location_rule_refs_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_location_rule_refs_v1 FROM PUBLIC;
        CREATE OR REPLACE FUNCTION cp6_location_rule_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body,'UTF8')::jsonb;d jsonb=b->'draft';c jsonb=d->'capacity';
          p cp6_location_rule_v1.proposals%ROWTYPE;s cp6_location_rule_v1.selectors%ROWTYPE;BEGIN
          SELECT * INTO STRICT p FROM cp6_location_rule_v1.proposals WHERE tenant_id=NEW.tenant_id AND id=NEW.id;
          SELECT * INTO STRICT s FROM cp6_location_rule_v1.selectors WHERE tenant_id=NEW.tenant_id AND id=p.selector_id;
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.id::text
            OR b->>'locationId' IS DISTINCT FROM p.location_id::text OR b->>'selectorId' IS DISTINCT FROM p.selector_id::text
            OR b->>'version' IS DISTINCT FROM NEW.version::text OR b->>'operationId' IS DISTINCT FROM NEW.operation_id::text
            OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR COALESCE(b->>'referenceResolution','') NOT IN('UNRESOLVED','RESOLVED')
            OR NEW.version<>p.version OR p.state<>'DRAFT' OR NEW.operation_id<>p.operation_id OR NEW.actor_id<>p.actor_id
            OR d->'selectorKey'->>'ruleFamily' IS DISTINCT FROM s.family OR d->'selectorKey'->>'metricId' IS DISTINCT FROM s.metric
            OR d->'selectorKey'->'applicabilityDomainRef'->>'id' IS DISTINCT FROM s.domain
            OR jsonb_typeof(d->'allowedProductTypes') IS DISTINCT FROM 'array' OR jsonb_typeof(d->'allowedOperations') IS DISTINCT FROM 'array'
            OR jsonb_typeof(d->'isPickable') IS DISTINCT FROM 'boolean' OR NOT(d ? 'validFrom' AND d ? 'validTo')
            OR c->>'mode' IS NULL OR c->>'mode' NOT IN('LIMITED','UNLIMITED','UNKNOWN')
            OR c->>'mode'='LIMITED' AND (COALESCE(c->>'value','') !~ '^[0-9]{1,13}\.[0-9]{8}$' OR c->'metricRef'->>'id' IS DISTINCT FROM s.metric
                OR jsonb_typeof(c->'uomRef') IS DISTINCT FROM 'object' OR jsonb_typeof(c->'precisionPolicyRef') IS DISTINCT FROM 'object' OR c ? 'reason')
            OR c->>'mode'='UNLIMITED' AND c IS DISTINCT FROM jsonb_build_object('mode','UNLIMITED')
            OR c->>'mode'='UNKNOWN' AND (jsonb_typeof(c->'reason') IS DISTINCT FROM 'string' OR c ?| ARRAY['value','metricRef','uomRef','precisionPolicyRef'])
            OR NEW.version>1 AND NOT EXISTS(SELECT FROM cp6_location_rule_v1.versions WHERE tenant_id=NEW.tenant_id AND id=NEW.id AND version=NEW.version-1)
            THEN RAISE EXCEPTION 'RULE_ORIGINAL_INVALID';END IF;
          IF b->>'referenceResolution'='UNRESOLVED' AND COALESCE(b->'referenceBinding','null'::jsonb)<>'null'::jsonb
            THEN RAISE EXCEPTION 'RULE_REFERENCE_BINDING_INVALID';END IF;
          IF b->>'referenceResolution'='RESOLVED' AND NOT EXISTS(
            SELECT FROM cp6_location_rule_refs_v1.heads h JOIN cp6_location_rule_refs_v1.versions v
              ON v.tenant_id=h.tenant_id AND v.id=h.id AND v.revision=h.revision
            JOIN public."T_Warehouse" w ON w."TenantId"=h.tenant_id AND w."Id"=h.warehouse_id
            JOIN public."T_Location" l ON l."TenantId"=w."TenantId" AND l."WarehouseCd"=w."WarehouseCd" AND l."Id"=p.location_id
            WHERE h.tenant_id=NEW.tenant_id AND b->'referenceBinding'->'policy'->>'owner'='Main.Wms.LocationRuleReferences.v1'
              AND b->'referenceBinding'->'policy'->>'id'=h.id::text AND b->'referenceBinding'->'policy'->>'version'=h.revision::text
              AND b->'referenceBinding'->'policy'->>'digest'=v.digest AND b->'referenceBinding'->>'registryVersion'=h.id::text||'/'||h.revision::text
              AND convert_from(v.body,'UTF8')::jsonb->'definition'->>'enabled'='true'
              AND jsonb_typeof(b->'referenceBinding'->'fields')='array' AND jsonb_typeof(b->'referenceBinding'->'units')='array')
            THEN RAISE EXCEPTION 'RULE_REFERENCE_BINDING_INVALID';END IF;RETURN NEW;
        END $$;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,10,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('wms-warehouse','rule-policy','维护库位规则字典'),('wms-location-rule','resolve','绑定候选准确来源')) a(menu,code,name)
        WHERE m."MenuKey"=a.menu AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Rule reference originals require forward recovery.");
}
