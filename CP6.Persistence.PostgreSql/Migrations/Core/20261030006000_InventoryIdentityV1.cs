using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030006000_InventoryIdentityV1")]
public sealed class InventoryIdentityV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_inventory_identity_v1;
        REVOKE ALL ON SCHEMA cp6_inventory_identity_v1 FROM PUBLIC;
        CREATE TABLE cp6_inventory_identity_v1.heads(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,object_id uuid NOT NULL,creator_id uuid NOT NULL,
          kind text NOT NULL CHECK(kind IN('PARTY','PURPOSE','PRODUCT')),code text NOT NULL,stable_key text NOT NULL,
          revision bigint NOT NULL CHECK(revision>0),PRIMARY KEY(environment_id,tenant_id,object_id),UNIQUE(tenant_id,object_id),
          UNIQUE(environment_id,tenant_id,kind,code),CHECK(object_id<>'00000000-0000-0000-0000-000000000000'::uuid));
        CREATE UNIQUE INDEX inventory_party_identity ON cp6_inventory_identity_v1.heads(environment_id,tenant_id,stable_key) WHERE kind='PARTY';
        CREATE TABLE cp6_inventory_identity_v1.versions(
          environment_id uuid NOT NULL,tenant_id uuid NOT NULL,object_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          actor_id uuid NOT NULL,operation_id uuid NOT NULL,body bytea NOT NULL CHECK(octet_length(body)>2),
          digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          journal text NOT NULL DEFAULT 'Main.Wms.InventoryIdentity.v1' CHECK(journal='Main.Wms.InventoryIdentity.v1'),
          PRIMARY KEY(environment_id,tenant_id,object_id,revision),UNIQUE(tenant_id,operation_id),
          FOREIGN KEY(environment_id,tenant_id,object_id) REFERENCES cp6_inventory_identity_v1.heads,
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_inventory_identity_v1.heads ADD CONSTRAINT inventory_identity_original
          FOREIGN KEY(environment_id,tenant_id,object_id,revision) REFERENCES cp6_inventory_identity_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_inventory_identity_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN IF NEW.revision<>1 THEN RAISE EXCEPTION 'INVENTORY_IDENTITY_FRONTIER_INVALID';END IF;
          ELSIF (to_jsonb(NEW)-'revision') IS DISTINCT FROM (to_jsonb(OLD)-'revision') OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'INVENTORY_IDENTITY_FRONTIER_INVALID';END IF;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_inventory_identity_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body,'UTF8')::jsonb;p jsonb;s jsonb;r jsonb;raw bytea;ok boolean;BEGIN
          IF b->>'environmentId' IS DISTINCT FROM NEW.environment_id::text OR b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text
            OR b->>'id' IS DISTINCT FROM NEW.object_id::text OR b->>'version' IS DISTINCT FROM NEW.revision::text
            OR b->>'operationId' IS DISTINCT FROM NEW.operation_id::text OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text
            OR COALESCE(b->>'evidenceMode','') NOT IN('REAL','DEMO') OR COALESCE(b->>'state','') NOT IN('DRAFT','APPROVED','DISABLED')
            OR NOT EXISTS(SELECT FROM cp6_inventory_identity_v1.heads h WHERE h.environment_id=NEW.environment_id AND h.tenant_id=NEW.tenant_id
              AND h.object_id=NEW.object_id AND h.revision=NEW.revision AND h.creator_id::text=b->>'creatorId'
              AND h.kind=b#>>'{definition,kind}' AND h.code=b#>>'{definition,code}') THEN RAISE EXCEPTION 'INVENTORY_IDENTITY_ORIGINAL_INVALID';END IF;
          IF NEW.revision>1 THEN SELECT convert_from(body,'UTF8')::jsonb INTO p FROM cp6_inventory_identity_v1.versions
            WHERE environment_id=NEW.environment_id AND tenant_id=NEW.tenant_id AND object_id=NEW.object_id AND revision=NEW.revision-1;
            IF p IS NULL THEN RAISE EXCEPTION 'INVENTORY_IDENTITY_PARENT_INVALID';END IF;
          END IF;
          IF b->>'action'='SAVE' THEN
            IF b->>'state'<>'DRAFT' OR b->>'authorId' IS DISTINCT FROM b->>'actorId' OR b->'confirmations'<>'[]'::jsonb THEN RAISE EXCEPTION 'INVENTORY_IDENTITY_DRAFT_INVALID';END IF;
          ELSIF b->>'action'='APPROVE' THEN
            IF p IS NULL OR p->>'state'<>'DRAFT' OR b->>'state'<>'APPROVED' OR b->'definition' IS DISTINCT FROM p->'definition'
              OR b->>'authorId' IS DISTINCT FROM p->>'authorId' OR b->>'actorId'=b->>'authorId'
              OR jsonb_array_length(b->'confirmations')<>(CASE WHEN b#>>'{definition,kind}'='PRODUCT' THEN 3 ELSE 1 END) THEN RAISE EXCEPTION 'INVENTORY_IDENTITY_APPROVAL_INVALID';END IF;
          ELSIF b->>'action'='DISABLE' THEN
            IF p IS NULL OR p->>'state'='DISABLED' OR b->>'state'<>'DISABLED' OR b->'definition' IS DISTINCT FROM p->'definition'
              OR b->>'authorId' IS DISTINCT FROM p->>'authorId' OR b->'confirmations'<>'[]'::jsonb THEN RAISE EXCEPTION 'INVENTORY_IDENTITY_DISABLE_INVALID';END IF;
          ELSE RAISE EXCEPTION 'INVENTORY_IDENTITY_ACTION_INVALID';END IF;
          FOR s IN SELECT value FROM jsonb_array_elements(b->'sources') LOOP
            r=s->'reference';raw=convert_to(s->>'originalJson','UTF8');
            IF r->>'digest' IS DISTINCT FROM encode(sha256(raw),'hex') THEN RAISE EXCEPTION 'INVENTORY_IDENTITY_SOURCE_INVALID';END IF;
            ok=CASE r->>'owner'
              WHEN 'Main.Erp.ProductIdentity.v1' THEN EXISTS(SELECT FROM cp6_product_identity_v1.versions v WHERE v.tenant_id=NEW.tenant_id AND v.product_id::text=r->>'id' AND v.revision::text=r->>'version' AND convert_to(v.source_json,'UTF8')=raw)
              WHEN 'Main.Erp.BusinessPartnerIdentity.v1' THEN EXISTS(SELECT FROM cp6_business_partner_identity_v1.versions v WHERE v.tenant_id=NEW.tenant_id AND v.partner_id::text=r->>'id' AND v.revision::text=r->>'version' AND convert_to(v.source_json,'UTF8')=raw)
              WHEN 'Main.Erp.ProductUnit.v1' THEN EXISTS(SELECT FROM cp6_uom_profile_v1.product_versions v WHERE v.tenant_id=NEW.tenant_id AND v.product_id::text=r->>'id' AND v.revision::text=r->>'version' AND v.body_bytes=raw)
              WHEN 'Main.Erp.UomProfile.v1' THEN EXISTS(SELECT FROM cp6_uom_profile_v1.versions v WHERE v.tenant_id=NEW.tenant_id AND v.profile_id::text=r->>'id' AND v.revision::text=r->>'version' AND v.body_bytes=raw)
              WHEN 'Main.Plm.EngineeringMapping.v1' THEN EXISTS(SELECT FROM cp6_engineering_product_v1.mapping_versions v WHERE v.tenant_id=NEW.tenant_id AND v.mapping_id::text=r->>'id' AND v.revision::text=r->>'version' AND v.body_bytes=raw)
              WHEN 'Main.Plm.EngineeringBaseline.v1' THEN EXISTS(SELECT FROM cp6_engineering_baseline_v1.baselines v WHERE v.environment_id=NEW.environment_id AND v.tenant_id=NEW.tenant_id AND v.baseline_id::text=r->>'id' AND r->>'version'='1' AND v.body=raw)
              WHEN 'Main.Wms.InventoryIdentity.v1' THEN EXISTS(SELECT FROM cp6_inventory_identity_v1.versions v WHERE v.environment_id=NEW.environment_id AND v.tenant_id=NEW.tenant_id AND v.object_id::text=r->>'id' AND v.revision::text=r->>'version' AND v.body=raw)
              ELSE false END;
            IF NOT ok THEN RAISE EXCEPTION 'INVENTORY_IDENTITY_NATIVE_SOURCE_REQUIRED';END IF;
          END LOOP;RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_inventory_identity_v1.provenance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF NOT EXISTS(SELECT FROM cp6_main_owner_v1.operations o WHERE o.tenant_id=NEW.tenant_id AND o.owner=NEW.journal
            AND o.operation_id=NEW.operation_id AND o.actor_id=NEW.actor_id AND o.contract='Main.Wms.InventoryIdentity.commands.v1'
            AND o.state='COMMITTED' AND o.object_ids=ARRAY[NEW.object_id]
            AND convert_from(o.result_bytes,'UTF8')::jsonb @> jsonb_build_object('ObjectId',NEW.object_id::text,'Version',NEW.revision::text,'Digest',NEW.digest,'Changed',true))
            THEN RAISE EXCEPTION 'INVENTORY_IDENTITY_OPERATION_REQUIRED';END IF;RETURN NULL;
        END $$;
        CREATE TRIGGER inventory_identity_advance BEFORE INSERT OR UPDATE ON cp6_inventory_identity_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_inventory_identity_v1.advance();
        CREATE TRIGGER inventory_identity_validate BEFORE INSERT ON cp6_inventory_identity_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_inventory_identity_v1.validate();
        CREATE TRIGGER inventory_identity_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_inventory_identity_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER inventory_identity_permanent BEFORE DELETE OR TRUNCATE ON cp6_inventory_identity_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER inventory_identity_provenance AFTER INSERT ON cp6_inventory_identity_v1.versions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_inventory_identity_v1.provenance();
        CREATE FUNCTION cp6_inventory_identity_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('heads','inventory_identity_advance','cp6_inventory_identity_v1.advance()',23),
            ('versions','inventory_identity_validate','cp6_inventory_identity_v1.validate()',7),
            ('versions','inventory_identity_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('heads','inventory_identity_permanent','cp6_scope_authorization_v1.immutable()',42),
            ('versions','inventory_identity_provenance','cp6_inventory_identity_v1.provenance()',5)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_inventory_identity_v1.'||a.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.name<>'inventory_identity_provenance' OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_inventory_identity_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_inventory_identity_v1 FROM PUBLIC;
        DO $$ DECLARE d text;f text;BEGIN
          SELECT pg_get_constraintdef(oid) INTO d FROM pg_constraint WHERE conrelid='cp6_operations_owner_v1.department_mappings'::regclass AND conname='department_mappings_object_kind_check';
          IF d IS NULL OR position('plm-engineering-iteration' in d)=0 THEN RAISE EXCEPTION 'INVENTORY_MAPPING_BASE_UNAVAILABLE';END IF;
          EXECUTE 'ALTER TABLE cp6_operations_owner_v1.department_mappings DROP CONSTRAINT department_mappings_object_kind_check';
          EXECUTE 'ALTER TABLE cp6_operations_owner_v1.department_mappings ADD CONSTRAINT department_mappings_object_kind_check CHECK ('||substring(d from 8 for length(d)-8)||' OR object_kind=''inventory-identity'')';
          SELECT pg_get_functiondef('cp6_operations_owner_v1.mapping_validate()'::regprocedure) INTO f;
          IF position('WHEN ''plm-requirement-set''' in f)=0 THEN RAISE EXCEPTION 'INVENTORY_MAPPING_BASE_UNAVAILABLE';END IF;
          EXECUTE replace(f,'WHEN ''plm-requirement-set''','WHEN ''inventory-identity'' THEN EXISTS(SELECT FROM cp6_inventory_identity_v1.heads o WHERE o.object_id=NEW.object_id AND o.tenant_id=NEW.tenant_id) WHEN ''plm-requirement-set''');
        END $$;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus" WHERE "MenuId">=1 AND "MenuId"<2147483647) available
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END FROM public."Sys_Menus"),
          'wms-inventory-identity','库存身份','/wms/inventory-identity','Goods',NULL,159,true,LOCALTIMESTAMP
        WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='wms-inventory-identity');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN (VALUES('query','读取库存身份',0),('add','创建库存身份',1),('edit','编辑身份草稿',2),('approve','独立确认身份',3),('disable','停用身份',4)) a(code,name,sort)
        WHERE m."MenuKey"='wms-inventory-identity' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'confirm-inventory','确认库存技术适用性',20,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey" IN('erp-product','plm-engineering-product') AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"='confirm-inventory');
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Inventory identity originals require forward recovery.");
}
