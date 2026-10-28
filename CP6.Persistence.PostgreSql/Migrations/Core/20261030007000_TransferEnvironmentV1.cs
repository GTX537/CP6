using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030007000_TransferEnvironmentV1")]
public sealed class TransferEnvironmentV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_transfer_environment_v1;
        REVOKE ALL ON SCHEMA cp6_transfer_environment_v1 FROM PUBLIC;
        CREATE TABLE cp6_transfer_environment_v1.binding(
          singleton boolean PRIMARY KEY CHECK(singleton),environment_id uuid NOT NULL UNIQUE,
          mode text NOT NULL CHECK(mode IN('PRODUCTION','SIMULATION')),sandbox_scope_id uuid,
          CHECK(environment_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          CHECK((mode='PRODUCTION' AND sandbox_scope_id IS NULL) OR (mode='SIMULATION' AND sandbox_scope_id IS NOT NULL AND sandbox_scope_id<>'00000000-0000-0000-0000-000000000000'::uuid)));
        CREATE TABLE cp6_transfer_environment_v1.tenants(
          tenant_id uuid PRIMARY KEY REFERENCES public."Sys_Tenants"("Id"),id uuid NOT NULL UNIQUE,revision bigint NOT NULL CHECK(revision>0),
          UNIQUE(tenant_id,id));
        CREATE TABLE cp6_transfer_environment_v1.versions(
          tenant_id uuid NOT NULL,id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          body bytea NOT NULL,digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          journal text NOT NULL DEFAULT 'Main.Wms.TransferEnvironment.v1' CHECK(journal='Main.Wms.TransferEnvironment.v1'),
          PRIMARY KEY(tenant_id,id,revision),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(tenant_id,id) REFERENCES cp6_transfer_environment_v1.tenants(tenant_id,id),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_transfer_environment_v1.tenants ADD CONSTRAINT transfer_registry_original
          FOREIGN KEY(tenant_id,id,revision) REFERENCES cp6_transfer_environment_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE TABLE cp6_transfer_environment_v1.source_namespaces(
          tenant_id uuid NOT NULL,owner text NOT NULL,object_id uuid NOT NULL,revision text NOT NULL CHECK(revision~'^[1-9][0-9]*$'),
          digest text NOT NULL CHECK(digest~'^[0-9a-f]{64}$'),environment_id uuid NOT NULL REFERENCES cp6_transfer_environment_v1.binding(environment_id),
          mode text NOT NULL,sandbox_scope_id uuid,captured_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,owner,object_id,revision));
        CREATE FUNCTION cp6_transfer_environment_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN IF NEW.revision<>1 THEN RAISE EXCEPTION 'TRANSFER_REGISTRY_FRONTIER_INVALID';END IF;
          ELSIF (to_jsonb(NEW)-'revision') IS DISTINCT FROM (to_jsonb(OLD)-'revision') OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'TRANSFER_REGISTRY_FRONTIER_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_transfer_environment_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body,'UTF8')::jsonb;d cp6_transfer_environment_v1.binding%ROWTYPE;BEGIN
          SELECT * INTO STRICT d FROM cp6_transfer_environment_v1.binding WHERE singleton;
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.id::text
            OR b->>'version' IS DISTINCT FROM NEW.revision::text OR b->>'operationId' IS DISTINCT FROM NEW.operation_id::text
            OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text OR jsonb_typeof(b->'enabled') IS DISTINCT FROM 'boolean'
            OR b->'deployment' IS DISTINCT FROM jsonb_build_object('environmentId',d.environment_id,'mode',d.mode,'sandboxScopeId',d.sandbox_scope_id)
            OR b->>'profile' IS DISTINCT FROM 'MAIN_NATIVE_TRANSFER_SOURCES_V1'
            OR b->'owners' IS DISTINCT FROM to_jsonb(ARRAY['Main.Erp.ProductIdentity.v1','Main.Erp.BusinessPartnerIdentity.v1','Main.Erp.UomProfile.v1','Main.Erp.ProductUnit.v1','Main.Plm.EngineeringProduct.v1','Main.Plm.EngineeringMapping.v1','Main.Space.PhysicalSiteIdentity.v1','Main.Plm.EngineeringBaseline.v1','Main.Wms.InventoryIdentity.v1'])
            OR NOT EXISTS(SELECT FROM cp6_transfer_environment_v1.tenants h WHERE h.tenant_id=NEW.tenant_id AND h.id=NEW.id AND h.revision=NEW.revision)
            OR NEW.revision>1 AND NOT EXISTS(SELECT FROM cp6_transfer_environment_v1.versions v WHERE v.tenant_id=NEW.tenant_id AND v.id=NEW.id AND v.revision=NEW.revision-1)
            THEN RAISE EXCEPTION 'TRANSFER_REGISTRY_ORIGINAL_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_transfer_environment_v1.provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal
            AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id AND o.contract='Main.Wms.TransferEnvironment.commands.v1'
            AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.id] AND convert_from(o.result_bytes,'UTF8')::jsonb @>
              jsonb_build_object('Reference',jsonb_build_object('Owner',NEW.journal,'Id',NEW.id::text,'Version',NEW.revision::text,'Digest',NEW.digest),'Changed',true))
            THEN RAISE EXCEPTION 'TRANSFER_REGISTRY_OPERATION_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE FUNCTION cp6_transfer_environment_v1.namespace_insert() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF pg_trigger_depth()<2 OR NOT EXISTS(SELECT FROM cp6_transfer_environment_v1.binding d WHERE d.environment_id=NEW.environment_id
            AND d.mode=NEW.mode AND d.sandbox_scope_id IS NOT DISTINCT FROM NEW.sandbox_scope_id)
            THEN RAISE EXCEPTION 'TRANSFER_SOURCE_NAMESPACE_CAPTURE_REQUIRED';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_transfer_environment_v1.capture() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE d cp6_transfer_environment_v1.binding%ROWTYPE;r jsonb=to_jsonb(NEW);b jsonb;rev text;BEGIN
          SELECT * INTO d FROM cp6_transfer_environment_v1.binding WHERE singleton;
          IF NOT FOUND THEN RETURN NEW;END IF;
          IF TG_ARGV[5]='scoped' THEN
            b=convert_from((r->>TG_ARGV[4])::bytea,'UTF8')::jsonb;
            IF r->>'environment_id' IS DISTINCT FROM d.environment_id::text OR b->>'environmentId' IS DISTINCT FROM d.environment_id::text
              OR b->>'evidenceMode' IS DISTINCT FROM (CASE d.mode WHEN 'PRODUCTION' THEN 'REAL' ELSE 'DEMO' END)
              THEN RAISE EXCEPTION 'TRANSFER_SOURCE_ENVIRONMENT_MISMATCH';END IF;
          END IF;
          rev=CASE TG_ARGV[2] WHEN '@one' THEN '1' ELSE r->>TG_ARGV[2] END;
          INSERT INTO cp6_transfer_environment_v1.source_namespaces(tenant_id,owner,object_id,revision,digest,environment_id,mode,sandbox_scope_id)
            VALUES((r->>'tenant_id')::uuid,TG_ARGV[0],(r->>TG_ARGV[1])::uuid,rev,r->>TG_ARGV[3],d.environment_id,d.mode,d.sandbox_scope_id);
          RETURN NEW;
        END $$;
        CREATE TRIGGER transfer_binding_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_environment_v1.binding FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER transfer_registry_advance BEFORE INSERT OR UPDATE ON cp6_transfer_environment_v1.tenants FOR EACH ROW EXECUTE FUNCTION cp6_transfer_environment_v1.advance();
        CREATE TRIGGER transfer_registry_permanent BEFORE DELETE OR TRUNCATE ON cp6_transfer_environment_v1.tenants FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER transfer_registry_validate BEFORE INSERT ON cp6_transfer_environment_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_transfer_environment_v1.validate();
        CREATE TRIGGER transfer_registry_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_environment_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER transfer_registry_provenance AFTER INSERT ON cp6_transfer_environment_v1.versions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_transfer_environment_v1.provenance();
        CREATE TRIGGER transfer_namespace_insert BEFORE INSERT ON cp6_transfer_environment_v1.source_namespaces FOR EACH ROW EXECUTE FUNCTION cp6_transfer_environment_v1.namespace_insert();
        CREATE TRIGGER transfer_namespace_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_transfer_environment_v1.source_namespaces FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_transfer_environment_v1.source_profiles()
        RETURNS TABLE(tbl text,owner text,idcol text,revisioncol text,digestcol text,bodycol text,variant text)
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$ VALUES
          ('cp6_product_identity_v1.versions','Main.Erp.ProductIdentity.v1','product_id','revision','source_digest','source_json','native'),
          ('cp6_business_partner_identity_v1.versions','Main.Erp.BusinessPartnerIdentity.v1','partner_id','revision','source_digest','source_json','native'),
          ('cp6_uom_profile_v1.versions','Main.Erp.UomProfile.v1','profile_id','revision','body_digest','body_bytes','native'),
          ('cp6_uom_profile_v1.product_versions','Main.Erp.ProductUnit.v1','product_id','revision','body_digest','body_bytes','native'),
          ('cp6_engineering_product_v1.versions','Main.Plm.EngineeringProduct.v1','product_id','revision','body_digest','body_bytes','native'),
          ('cp6_engineering_product_v1.mapping_versions','Main.Plm.EngineeringMapping.v1','mapping_id','revision','body_digest','body_bytes','native'),
          ('cp6_physical_site_identity_v1.versions','Main.Space.PhysicalSiteIdentity.v1','site_id','revision','source_digest','source_json','native'),
          ('cp6_engineering_baseline_v1.baselines','Main.Plm.EngineeringBaseline.v1','baseline_id','@one','digest','body','scoped'),
          ('cp6_inventory_identity_v1.versions','Main.Wms.InventoryIdentity.v1','object_id','revision','digest','body','scoped') $$;
        DO $$ DECLARE p record;BEGIN
          FOR p IN SELECT * FROM cp6_transfer_environment_v1.source_profiles() LOOP
            EXECUTE format('CREATE TRIGGER transfer_source_namespace AFTER INSERT ON %s FOR EACH ROW EXECUTE FUNCTION cp6_transfer_environment_v1.capture(%L,%L,%L,%L,%L,%L)',
              p.tbl,p.owner,p.idcol,p.revisioncol,p.digestcol,p.bodycol,p.variant);
          END LOOP;
        END $$;
        CREATE FUNCTION cp6_transfer_environment_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT current_setting('session_replication_role')='origin' AND
          NOT EXISTS(SELECT FROM (VALUES
            ('binding','transfer_binding_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('tenants','transfer_registry_advance','cp6_transfer_environment_v1.advance()',23),
            ('tenants','transfer_registry_permanent','cp6_scope_authorization_v1.immutable()',42),
            ('versions','transfer_registry_validate','cp6_transfer_environment_v1.validate()',7),
            ('versions','transfer_registry_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('versions','transfer_registry_provenance','cp6_transfer_environment_v1.provenance()',5),
            ('source_namespaces','transfer_namespace_insert','cp6_transfer_environment_v1.namespace_insert()',7),
            ('source_namespaces','transfer_namespace_immutable','cp6_scope_authorization_v1.immutable()',58)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_transfer_environment_v1.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.name<>'transfer_registry_provenance' OR t.tgdeferrable AND t.tginitdeferred))) AND
          NOT EXISTS(SELECT FROM cp6_transfer_environment_v1.source_profiles() p WHERE NOT EXISTS(SELECT FROM pg_trigger t
            WHERE t.tgrelid=to_regclass(p.tbl) AND t.tgname='transfer_source_namespace' AND t.tgfoid='cp6_transfer_environment_v1.capture()'::regprocedure
              AND t.tgtype=5 AND t.tgenabled IN('O','A') AND t.tgqual IS NULL AND t.tgnargs=6 AND encode(t.tgargs,'hex')=
              encode(convert_to(p.owner,'UTF8'),'hex')||'00'||encode(convert_to(p.idcol,'UTF8'),'hex')||'00'||encode(convert_to(p.revisioncol,'UTF8'),'hex')||'00'||
              encode(convert_to(p.digestcol,'UTF8'),'hex')||'00'||encode(convert_to(p.bodycol,'UTF8'),'hex')||'00'||encode(convert_to(p.variant,'UTF8'),'hex')||'00'));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_transfer_environment_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_transfer_environment_v1 FROM PUBLIC;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        DO $$ DECLARE key text;id integer;BEGIN
          FOREACH key IN ARRAY ARRAY['wms-transfer-environment','wms-transfer-source'] LOOP
            IF NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"=key) THEN
              SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
                (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus" WHERE "MenuId">=1 AND "MenuId"<2147483647) a
                 WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" x WHERE x."MenuId"=a.candidate)) END INTO id FROM public."Sys_Menus";
              INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
              VALUES(id,key,CASE key WHEN 'wms-transfer-environment' THEN '调拨来源环境' ELSE '调拨来源读取' END,
                CASE key WHEN 'wms-transfer-environment' THEN '/wms/transfer-environment' ELSE NULL END,'Setting',NULL,160,true,LOCALTIMESTAMP);
            END IF;
          END LOOP;
        END $$;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('query','读取受信调拨来源',0),('edit','登记调拨来源环境',1)) a(code,name,sort)
        WHERE (m."MenuKey"='wms-transfer-environment' OR m."MenuKey"='wms-transfer-source' AND a.code='query')
          AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Transfer environment provenance requires forward recovery.");
}
