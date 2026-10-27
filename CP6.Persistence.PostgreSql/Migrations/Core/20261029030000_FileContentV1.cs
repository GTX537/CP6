using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;
[DbContext(typeof(CP6Context)), Migration("20261029030000_FileContentV1")]
public sealed class FileContentV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_file_content_v1;
        REVOKE ALL ON SCHEMA cp6_file_content_v1 FROM PUBLIC;
        CREATE TABLE cp6_file_content_v1.contents(
          tenant_id uuid NOT NULL,file_id uuid NOT NULL,creator_id uuid NOT NULL,parent_id uuid NOT NULL,
          parent_version text NOT NULL CHECK(parent_version~'^[1-9][0-9]*$'),parent_digest text NOT NULL CHECK(parent_digest~'^[0-9a-f]{64}$'),
          file_name text NOT NULL CHECK(length(file_name) BETWEEN 1 AND 255),digest text NOT NULL CHECK(digest~'^[0-9a-f]{64}$'),
          byte_length bigint NOT NULL CHECK(byte_length BETWEEN 1 AND 20971520),media_type text NOT NULL,store_path text NOT NULL,
          PRIMARY KEY(tenant_id,file_id),UNIQUE(store_path),
          CHECK(file_id<>'00000000-0000-0000-0000-000000000000'::uuid AND creator_id<>'00000000-0000-0000-0000-000000000000'::uuid),
          FOREIGN KEY(tenant_id,parent_id) REFERENCES cp6_engineering_revision_v1.heads(tenant_id,revision_id));
        CREATE TABLE cp6_file_content_v1.heads(tenant_id uuid NOT NULL,file_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,file_id),FOREIGN KEY(tenant_id,file_id) REFERENCES cp6_file_content_v1.contents);
        CREATE TABLE cp6_file_content_v1.events(
          tenant_id uuid NOT NULL,file_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          body_bytes bytea NOT NULL CHECK(octet_length(body_bytes) BETWEEN 2 AND 1048576),
          body_digest text NOT NULL CHECK(body_digest=encode(sha256(body_bytes),'hex')),actor_id uuid NOT NULL,
          recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),PRIMARY KEY(tenant_id,file_id,revision),
          FOREIGN KEY(tenant_id,file_id) REFERENCES cp6_file_content_v1.heads);
        ALTER TABLE cp6_file_content_v1.heads ADD CONSTRAINT file_original FOREIGN KEY(tenant_id,file_id,revision)
          REFERENCES cp6_file_content_v1.events DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_file_content_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN IF NEW.revision<>1 THEN RAISE EXCEPTION 'FILE_FRONTIER_INVALID'; END IF;
          ELSIF (to_jsonb(NEW)-'revision') IS DISTINCT FROM (to_jsonb(OLD)-'revision') OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'FILE_FRONTIER_INVALID'; END IF; RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_file_content_v1.validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE b jsonb=convert_from(NEW.body_bytes,'UTF8')::jsonb;p jsonb; BEGIN
          IF b->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR b->>'id' IS DISTINCT FROM NEW.file_id::text
            OR b->>'version' IS DISTINCT FROM NEW.revision::text OR b->>'actorId' IS DISTINCT FROM NEW.actor_id::text
            OR b->>'etag' IS NULL OR b->>'etag'='00000000-0000-0000-0000-000000000000'
            OR b#>>'{inspection,status}' IS NULL OR b#>>'{inspection,status}' NOT IN('CLEAN','PENDING','NOT_CLEAN')
            OR jsonb_typeof(b->'held') IS DISTINCT FROM 'boolean'
            OR NOT EXISTS(SELECT FROM cp6_file_content_v1.heads h WHERE h.tenant_id=NEW.tenant_id AND h.file_id=NEW.file_id AND h.revision=NEW.revision)
            THEN RAISE EXCEPTION 'FILE_EVENT_INVALID'; END IF;
          IF b#>>'{inspection,status}'='CLEAN' AND (COALESCE(b#>>'{inspection,engineVersion}','')='' OR COALESCE(b#>>'{inspection,signatureVersion}','')=''
            OR COALESCE(b#>>'{inspection,evidenceDigest}','') !~ '^[0-9a-f]{64}$') THEN RAISE EXCEPTION 'FILE_INSPECTION_INVALID'; END IF;
          IF NEW.revision=1 THEN
            IF b->>'action' IS DISTINCT FROM 'UPLOAD' OR b->>'held' IS DISTINCT FROM 'false' THEN RAISE EXCEPTION 'FILE_PARENT_INVALID'; END IF;
          ELSE
            SELECT convert_from(body_bytes,'UTF8')::jsonb INTO p FROM cp6_file_content_v1.events
              WHERE tenant_id=NEW.tenant_id AND file_id=NEW.file_id AND revision=NEW.revision-1;
            IF p IS NULL OR b->>'etag'=p->>'etag' OR b->>'action' NOT IN('HOLD','RELEASE','INSPECT','INTEGRITY_FAILURE')
              OR b->>'action' IN('HOLD','RELEASE') AND b->'inspection' IS DISTINCT FROM p->'inspection'
              OR b->>'action'='HOLD' AND b->>'held' IS DISTINCT FROM 'true'
              OR b->>'action'='RELEASE' AND b->>'held' IS DISTINCT FROM 'false'
              OR b->>'action' IN('INSPECT','INTEGRITY_FAILURE') AND b->'held' IS DISTINCT FROM p->'held'
              OR b->>'action'='INTEGRITY_FAILURE' AND b#>>'{inspection,status}' IS DISTINCT FROM 'NOT_CLEAN' THEN RAISE EXCEPTION 'FILE_PARENT_INVALID'; END IF;
          END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER file_advance BEFORE INSERT OR UPDATE ON cp6_file_content_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_file_content_v1.advance();
        CREATE TRIGGER file_validate BEFORE INSERT ON cp6_file_content_v1.events FOR EACH ROW EXECUTE FUNCTION cp6_file_content_v1.validate();
        CREATE TRIGGER file_contents_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_file_content_v1.contents FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER file_events_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_file_content_v1.events FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER file_head_permanent BEFORE DELETE OR TRUNCATE ON cp6_file_content_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE FUNCTION cp6_file_content_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('heads','file_advance','cp6_file_content_v1.advance()',23),('events','file_validate','cp6_file_content_v1.validate()',7),
            ('contents','file_contents_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('events','file_events_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('heads','file_head_permanent','cp6_scope_authorization_v1.immutable()',42)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_file_content_v1.'||a.tbl)
              AND t.tgname=a.name AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_file_content_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_file_content_v1 FROM PUBLIC;
        LOCK TABLE public."Sys_Menus" IN SHARE ROW EXCLUSIVE MODE;
        INSERT INTO public."Sys_Menus"("MenuId","MenuKey","MenuName","RoutePath","Icon","ParentId","OrderNo","Enable","CreateDate")
        SELECT (SELECT CASE WHEN COALESCE(MAX("MenuId"),0)<2147483647 THEN COALESCE(MAX("MenuId"),0)+1 ELSE
          (SELECT min(candidate) FROM (SELECT 1 AS candidate UNION SELECT "MenuId"+1 FROM public."Sys_Menus" WHERE "MenuId">=1 AND "MenuId"<2147483647) available
          WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" used WHERE used."MenuId"=available.candidate)) END FROM public."Sys_Menus"),
          'file-content','受控文件内容','/pub/file-content','Document',NULL,156,true,LOCALTIMESTAMP
        WHERE NOT EXISTS(SELECT FROM public."Sys_Menus" WHERE "MenuKey"='file-content');
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",a.code,a.name,a.sort,t."Id",LOCALTIMESTAMP FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
          CROSS JOIN (VALUES('query','读取文件内容',0),('add','上传文件内容',1),('edit','检查与保留控制',2)) a(code,name,sort)
        WHERE m."MenuKey"='file-content' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" x
          WHERE x."TenantId"=t."Id" AND x."MenuId"=m."MenuId" AND x."ActionCode"=a.code);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Original content and inspection history require forward recovery.");
}
