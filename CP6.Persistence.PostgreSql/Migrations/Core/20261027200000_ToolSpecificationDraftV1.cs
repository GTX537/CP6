using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261027200000_ToolSpecificationDraftV1")]
public sealed class ToolSpecificationDraftV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_tool_specification_v1;
        REVOKE ALL ON SCHEMA cp6_tool_specification_v1 FROM PUBLIC;
        CREATE TABLE cp6_tool_specification_v1.heads (
          tenant_id uuid NOT NULL,tool_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,tool_id),
          FOREIGN KEY(tenant_id,tool_id) REFERENCES cp6_plate_mold_source_v1.heads(tenant_id,object_id));
        CREATE TABLE cp6_tool_specification_v1.versions (
          tenant_id uuid NOT NULL,tool_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 131072),
          body_digest text COLLATE "C" NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),
          actor_id uuid NOT NULL CHECK(actor_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          PRIMARY KEY(tenant_id,tool_id,revision),
          FOREIGN KEY(tenant_id,tool_id) REFERENCES cp6_tool_specification_v1.heads DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_tool_specification_v1.heads ADD CONSTRAINT tool_specification_original
          FOREIGN KEY(tenant_id,tool_id,revision) REFERENCES cp6_tool_specification_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_tool_specification_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'TOOL_SPECIFICATION_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE FUNCTION cp6_tool_specification_v1.advance_head() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 THEN RAISE EXCEPTION 'TOOL_SPECIFICATION_FRONTIER_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF (OLD.tenant_id,OLD.tool_id) IS DISTINCT FROM (NEW.tenant_id,NEW.tool_id) OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'TOOL_SPECIFICATION_FRONTIER_INVALID' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_tool_specification_v1.validate_original() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE body jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb;
        BEGIN
          IF body->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR body->>'id' IS DISTINCT FROM NEW.tool_id::text
             OR body->>'version' IS DISTINCT FROM NEW.revision::text OR body->>'actorId' IS DISTINCT FROM NEW.actor_id::text
             OR body#>>'{source,owner}' IS DISTINCT FROM 'Main.Erp.PlateMoldSource.v1'
             OR body#>>'{source,id}' IS DISTINCT FROM NEW.tool_id::text
             OR body#>>'{requirements,productRef,owner}' IS DISTINCT FROM 'Main.Plm.EngineeringProduct.v1'
             OR NOT EXISTS(SELECT FROM cp6_tool_specification_v1.heads h WHERE h.tenant_id=NEW.tenant_id AND h.tool_id=NEW.tool_id AND h.revision=NEW.revision)
             OR NOT EXISTS(SELECT FROM cp6_plate_mold_source_v1.versions s WHERE s.tenant_id=NEW.tenant_id AND s.object_id=NEW.tool_id
                AND s.revision::text=body#>>'{source,version}' AND s.source_digest=body#>>'{source,digest}')
             OR NOT EXISTS(SELECT FROM cp6_engineering_product_v1.versions p WHERE p.tenant_id=NEW.tenant_id
                AND p.product_id::text=body#>>'{requirements,productRef,id}' AND p.revision::text=body#>>'{requirements,productRef,version}'
                AND p.body_digest=body#>>'{requirements,productRef,digest}')
             OR NEW.revision>1 AND NOT EXISTS(SELECT FROM cp6_tool_specification_v1.versions p
                WHERE p.tenant_id=NEW.tenant_id AND p.tool_id=NEW.tool_id AND p.revision=NEW.revision-1) THEN
            RAISE EXCEPTION 'TOOL_SPECIFICATION_ORIGINAL_INVALID' USING ERRCODE='23514';
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER tool_specification_advance BEFORE INSERT OR UPDATE ON cp6_tool_specification_v1.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_tool_specification_v1.advance_head();
        CREATE TRIGGER tool_specification_validate BEFORE INSERT ON cp6_tool_specification_v1.versions
          FOR EACH ROW EXECUTE FUNCTION cp6_tool_specification_v1.validate_original();
        CREATE TRIGGER tool_specification_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_tool_specification_v1.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_specification_v1.immutable();
        CREATE TRIGGER tool_specification_no_delete BEFORE DELETE OR TRUNCATE ON cp6_tool_specification_v1.heads
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_tool_specification_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_tool_specification_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_tool_specification_v1 FROM PUBLIC;
        -- Independent capabilities only; existing role assignments remain unchanged.
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus"
            WHERE "MenuId">=1 AND "MenuId"<2147483647) available
            WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END
          FROM public."Sys_Menus"),'plm-tool-specification','工具技术规格草稿',
          '/plm/tool-specifications','Connection',NULL,152,true,LOCALTIMESTAMP
        WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='plm-tool-specification');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        CROSS JOIN (VALUES('query','读取工具技术规格草稿',0),('add','编制工具技术规格草稿',1),('edit','修订工具技术规格草稿',2)) a(code,name,sort)
        WHERE m."MenuKey"='plm-tool-specification' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Tool technical originals require forward recovery.");
}
