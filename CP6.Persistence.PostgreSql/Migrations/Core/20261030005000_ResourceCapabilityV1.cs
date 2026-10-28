using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261030005000_ResourceCapabilityV1")]
public sealed class ResourceCapabilityV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE SCHEMA cp6_resource_capability_v1;
        REVOKE ALL ON SCHEMA cp6_resource_capability_v1 FROM PUBLIC;
        CREATE TABLE cp6_resource_capability_v1.heads(
          tenant_id uuid NOT NULL,kind text NOT NULL CHECK(kind IN('MACHINE','WORK_CENTER')),resource_id uuid NOT NULL,
          revision bigint NOT NULL CHECK(revision>0),creator_id uuid NOT NULL,PRIMARY KEY(tenant_id,kind,resource_id));
        CREATE TABLE cp6_resource_capability_v1.versions(
          tenant_id uuid NOT NULL,kind text NOT NULL,resource_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          source_revision bigint NOT NULL,operation_id uuid NOT NULL,actor_id uuid NOT NULL,
          body bytea NOT NULL CHECK(octet_length(body) BETWEEN 2 AND 1048576),digest text NOT NULL CHECK(digest=encode(sha256(body),'hex')),
          journal text GENERATED ALWAYS AS ('Main.Mes.ResourceCapability.v1.'||kind) STORED,
          PRIMARY KEY(tenant_id,kind,resource_id,revision),UNIQUE(tenant_id,journal,operation_id),
          FOREIGN KEY(tenant_id,kind,resource_id) REFERENCES cp6_resource_capability_v1.heads,
          FOREIGN KEY(tenant_id,kind,resource_id,source_revision) REFERENCES cp6_resource_source_v1.versions(tenant_id,kind,resource_id,revision),
          FOREIGN KEY(tenant_id,journal,operation_id) REFERENCES cp6_main_owner_v1.operations(tenant_id,owner,operation_id) DEFERRABLE INITIALLY DEFERRED);
        ALTER TABLE cp6_resource_capability_v1.heads ADD CONSTRAINT resource_capability_head_original
          FOREIGN KEY(tenant_id,kind,resource_id,revision) REFERENCES cp6_resource_capability_v1.versions DEFERRABLE INITIALLY DEFERRED;
        CREATE FUNCTION cp6_resource_capability_v1.advance() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 THEN RAISE EXCEPTION 'RESOURCE_CAPABILITY_FRONTIER_INVALID'; END IF;
          ELSIF (NEW.tenant_id,NEW.kind,NEW.resource_id,NEW.creator_id) IS DISTINCT FROM (OLD.tenant_id,OLD.kind,OLD.resource_id,OLD.creator_id)
            OR NEW.revision<>OLD.revision+1 THEN RAISE EXCEPTION 'RESOURCE_CAPABILITY_FRONTIER_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_resource_capability_v1.complete() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE p jsonb=convert_from(NEW.body,'UTF8')::jsonb;s cp6_resource_source_v1.versions;j cp6_main_owner_v1.operations;
          h cp6_resource_capability_v1.heads; u jsonb; r jsonb;
        BEGIN
          SELECT * INTO h FROM cp6_resource_capability_v1.heads WHERE tenant_id=NEW.tenant_id AND kind=NEW.kind AND resource_id=NEW.resource_id;
          SELECT * INTO s FROM cp6_resource_source_v1.versions WHERE tenant_id=NEW.tenant_id AND kind=NEW.kind AND resource_id=NEW.resource_id AND revision=NEW.source_revision;
          IF p->>'tenantId' IS DISTINCT FROM NEW.tenant_id::text OR p->>'id' IS DISTINCT FROM NEW.resource_id::text OR p->>'kind' IS DISTINCT FROM NEW.kind
            OR p->>'version' IS DISTINCT FROM NEW.revision::text OR p->>'creatorId' IS DISTINCT FROM h.creator_id::text OR p->>'actorId' IS DISTINCT FROM NEW.actor_id::text
            OR p#>>'{body,source,owner}' IS DISTINCT FROM 'Main.Mes.ResourceSource.v1' OR p#>>'{body,source,kind}' IS DISTINCT FROM NEW.kind
            OR p#>>'{body,source,id}' IS DISTINCT FROM NEW.resource_id::text OR p#>>'{body,source,version}' IS DISTINCT FROM NEW.source_revision::text
            OR p#>>'{body,source,digest}' IS DISTINCT FROM s.source_digest OR p->>'resourceOriginal' IS DISTINCT FROM s.source_json
            OR NEW.revision>1 AND NOT EXISTS(SELECT FROM cp6_resource_capability_v1.versions v
              WHERE v.tenant_id=NEW.tenant_id AND v.kind=NEW.kind AND v.resource_id=NEW.resource_id AND v.revision=NEW.revision-1)
            THEN RAISE EXCEPTION 'RESOURCE_CAPABILITY_ORIGINAL_INVALID'; END IF;
          SELECT * INTO j FROM cp6_main_owner_v1.operations WHERE tenant_id=NEW.tenant_id AND owner=NEW.journal AND operation_id=NEW.operation_id;
          r=convert_from(j.result_bytes,'UTF8')::jsonb;
          IF j.actor_id IS DISTINCT FROM NEW.actor_id OR j.contract IS DISTINCT FROM 'Main.Mes.ResourceCapability.commands.v1' OR j.state IS DISTINCT FROM 'COMMITTED'
            OR j.object_ids IS DISTINCT FROM ARRAY[NEW.resource_id] OR r->>'OperationId' IS DISTINCT FROM NEW.operation_id::text
            OR r->>'Kind' IS DISTINCT FROM NEW.kind OR r->>'ObjectId' IS DISTINCT FROM NEW.resource_id::text
            OR r->>'Version' IS DISTINCT FROM NEW.revision::text OR r->>'Digest' IS DISTINCT FROM NEW.digest
            THEN RAISE EXCEPTION 'RESOURCE_CAPABILITY_OPERATION_REQUIRED'; END IF;
          IF jsonb_typeof(p->'units') IS DISTINCT FROM 'array' THEN RAISE EXCEPTION 'RESOURCE_CAPABILITY_UNITS_INVALID'; END IF;
          FOR u IN SELECT value FROM jsonb_array_elements(p->'units') LOOP
            IF NOT EXISTS(SELECT FROM cp6_uom_profile_v1.versions v WHERE v.tenant_id=NEW.tenant_id AND v.profile_id=(u#>>'{reference,id}')::uuid
              AND v.revision=(u#>>'{reference,version}')::bigint AND v.body_digest=u#>>'{reference,digest}'
              AND u#>>'{reference,owner}'='Main.Erp.UomProfile.v1' AND convert_from(v.body_bytes,'UTF8')=u->>'originalJson')
              THEN RAISE EXCEPTION 'RESOURCE_CAPABILITY_UNIT_ORIGINAL_INVALID'; END IF;
          END LOOP;
          IF p#>>'{body,timeBasis,id}' IS NOT NULL AND NOT EXISTS(SELECT FROM cp6_uom_time_v1.versions v
            WHERE v.tenant_id=NEW.tenant_id AND v.profile_id=(p#>>'{body,timeBasis,id}')::uuid AND v.revision=(p#>>'{body,timeBasis,version}')::bigint
              AND v.body_digest=p#>>'{body,timeBasis,digest}' AND convert_from(v.body_bytes,'UTF8')=p->>'timeBasisOriginal')
            THEN RAISE EXCEPTION 'RESOURCE_CAPABILITY_TIME_ORIGINAL_INVALID'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER resource_capability_advance BEFORE INSERT OR UPDATE ON cp6_resource_capability_v1.heads FOR EACH ROW EXECUTE FUNCTION cp6_resource_capability_v1.advance();
        CREATE TRIGGER resource_capability_keep BEFORE DELETE OR TRUNCATE ON cp6_resource_capability_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE TRIGGER resource_capability_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON cp6_resource_capability_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_scope_authorization_v1.immutable();
        CREATE CONSTRAINT TRIGGER resource_capability_complete AFTER INSERT ON cp6_resource_capability_v1.versions DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION cp6_resource_capability_v1.complete();
        CREATE TRIGGER resource_capability_root BEFORE INSERT OR UPDATE OR DELETE ON cp6_resource_capability_v1.heads FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE TRIGGER resource_capability_root BEFORE INSERT ON cp6_resource_capability_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_authentication_v1.before_change();
        CREATE FUNCTION cp6_resource_capability_v1.ready() RETURNS boolean LANGUAGE sql STABLE SET search_path=pg_catalog AS $$
          SELECT NOT EXISTS(SELECT FROM (VALUES
            ('heads','resource_capability_advance','cp6_resource_capability_v1.advance()',23),
            ('heads','resource_capability_keep','cp6_scope_authorization_v1.immutable()',42),
            ('heads','resource_capability_root','cp6_authentication_v1.before_change()',30),
            ('versions','resource_capability_immutable','cp6_scope_authorization_v1.immutable()',58),
            ('versions','resource_capability_complete','cp6_resource_capability_v1.complete()',5),
            ('versions','resource_capability_root','cp6_authentication_v1.before_change()',6)) a(tbl,name,fn,typ)
            WHERE NOT EXISTS(SELECT FROM pg_trigger t WHERE t.tgrelid=to_regclass('cp6_resource_capability_v1.'||a.tbl) AND t.tgname=a.name
              AND t.tgfoid=to_regprocedure(a.fn) AND t.tgtype=a.typ AND t.tgenabled IN('O','A') AND t.tgqual IS NULL
              AND (a.name<>'resource_capability_complete' OR t.tgdeferrable AND t.tginitdeferred)));
        $$;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_resource_capability_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_resource_capability_v1 FROM PUBLIC;
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'capability-edit','登记资源能力声明',10,t."Id",LOCALTIMESTAMP
          FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t WHERE m."MenuKey" IN('mes-machine','mes-work-center')
          AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='capability-edit');
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Resource capability originals require forward recovery.");
}
