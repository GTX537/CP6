using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261029020000_EngineeringManufacturingV1")]
public sealed class EngineeringManufacturingV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_engineering_manufacturing_v1;
        REVOKE ALL ON SCHEMA cp6_engineering_manufacturing_v1 FROM PUBLIC;
        CREATE TABLE cp6_engineering_manufacturing_v1.heads(
          tenant_id uuid NOT NULL,manufacturing_id uuid NOT NULL,creator_id uuid NOT NULL,product_id uuid NOT NULL,
          engineering_revision_id uuid NOT NULL,name text NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,manufacturing_id),
          CHECK(manufacturing_id<>'00000000-0000-0000-0000-000000000000'::uuid AND creator_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          FOREIGN KEY(tenant_id,product_id) REFERENCES cp6_engineering_product_v1.heads(tenant_id,product_id),
          FOREIGN KEY(tenant_id,engineering_revision_id) REFERENCES cp6_engineering_revision_v1.heads(tenant_id,revision_id));
        CREATE TABLE cp6_engineering_manufacturing_v1.versions(
          tenant_id uuid NOT NULL,manufacturing_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 8388608),
          body_digest text NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),actor_id uuid NOT NULL,
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(tenant_id,manufacturing_id,revision),
          FOREIGN KEY(tenant_id,manufacturing_id) REFERENCES cp6_engineering_manufacturing_v1.heads);
        ALTER TABLE cp6_engineering_manufacturing_v1.heads ADD CONSTRAINT manufacturing_original
          FOREIGN KEY(tenant_id,manufacturing_id,revision) REFERENCES cp6_engineering_manufacturing_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_engineering_manufacturing_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 THEN RAISE EXCEPTION 'MANUFACTURING_FRONTIER_INVALID'; END IF;
          ELSIF (to_jsonb(NEW)-'name'-'revision') IS DISTINCT FROM (to_jsonb(OLD)-'name'-'revision') OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'MANUFACTURING_FRONTIER_INVALID';
          END IF; RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_engineering_manufacturing_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb;p jsonb; BEGIN
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.manufacturing_id::text
            OR b->>'version' IS DISTINCT FROM NEW.revision::text OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text
            OR b->>'etag' IS NULL OR b->>'etag'='00000000-0000-0000-0000-000000000000'
            OR NOT EXISTS(SELECT FROM cp6_engineering_manufacturing_v1.heads h WHERE h.tenant_id=NEW.tenant_id AND h.manufacturing_id=NEW.manufacturing_id
              AND h.revision=NEW.revision AND h.creator_id::text=b->>'creatorId' AND h.product_id::text=b#>>'{body,productRef,id}'
              AND h.engineering_revision_id::text=b#>>'{body,revisionOrigin,id}' AND h.name=b#>>'{body,name}') THEN
            RAISE EXCEPTION 'MANUFACTURING_ORIGINAL_IDENTITY_INVALID'; END IF;
          IF NEW.revision=1 THEN
            IF b->>'parentVersion' IS NOT NULL OR b->>'action' IS DISTINCT FROM 'CREATE' THEN RAISE EXCEPTION 'MANUFACTURING_PARENT_INVALID'; END IF;
          ELSE
            SELECT convert_from(body_bytes,'UTF8')::jsonb INTO p FROM cp6_engineering_manufacturing_v1.versions
              WHERE tenant_id=NEW.tenant_id AND manufacturing_id=NEW.manufacturing_id AND revision=NEW.revision-1;
            IF p IS NULL OR b->>'parentVersion' IS DISTINCT FROM (NEW.revision-1)::text OR b->>'action' IS DISTINCT FROM 'REVISE'
              OR b->>'etag'=p->>'etag' THEN RAISE EXCEPTION 'MANUFACTURING_PARENT_INVALID'; END IF;
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER manufacturing_advance BEFORE INSERT OR UPDATE ON cp6_engineering_manufacturing_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_engineering_manufacturing_v1.advance();
        CREATE TRIGGER manufacturing_validate BEFORE INSERT ON cp6_engineering_manufacturing_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_engineering_manufacturing_v1.validate();
        CREATE TRIGGER manufacturing_original_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_engineering_manufacturing_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER manufacturing_head_permanent BEFORE DELETE OR TRUNCATE ON cp6_engineering_manufacturing_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_engineering_manufacturing_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('heads','manufacturing_advance','cp6_engineering_manufacturing_v1.advance()',23),
            ('versions','manufacturing_validate','cp6_engineering_manufacturing_v1.validate()',7),
            ('versions','manufacturing_original_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('heads','manufacturing_head_permanent','cp6_scope_authorization_v1.immutable()',42)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_engineering_manufacturing_v1.'||a.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_engineering_manufacturing_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_engineering_manufacturing_v1 FROM PUBLIC;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus" WHERE "MenuId">=1 AND "MenuId"<2147483647) available
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END FROM public."Sys_Menus"),
          'plm-engineering-manufacturing','制造映射与产出','/plm/engineering-manufacturing','Connection',NULL,155,true,LOCALTIMESTAMP
        WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='plm-engineering-manufacturing');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN (VALUES('query','读取制造关系',0),('add','创建制造关系',1),('edit','修订制造关系',2)) a(code,name,sort)
        WHERE m."MenuKey"='plm-engineering-manufacturing' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Manufacturing relation originals require forward recovery.");
}
