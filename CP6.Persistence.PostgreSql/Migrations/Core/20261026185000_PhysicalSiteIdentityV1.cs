using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261026185000_PhysicalSiteIdentityV1")]
public sealed class PhysicalSiteIdentityV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_physical_site_identity_v1;
        REVOKE ALL ON SCHEMA cp6_physical_site_identity_v1 FROM PUBLIC;
        CREATE TABLE cp6_physical_site_identity_v1.heads (
          tenant_id uuid NOT NULL,site_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          PRIMARY KEY(tenant_id,site_id));
        CREATE TABLE cp6_physical_site_identity_v1.versions (
          tenant_id uuid NOT NULL,site_id uuid NOT NULL,revision bigint NOT NULL CHECK(revision>0),
          source_present boolean NOT NULL,source_json text NOT NULL,
          source_digest text NOT NULL CHECK(source_digest=encode(sha256(convert_to(source_json,'UTF8')),'hex')),
          observed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(tenant_id,site_id,revision));
        ALTER TABLE cp6_physical_site_identity_v1.heads ADD CONSTRAINT physical_site_identity_head_version
          FOREIGN KEY(tenant_id,site_id,revision) REFERENCES cp6_physical_site_identity_v1.versions
          DEFERRABLE INITIALLY DEFERRED;
        -- This is a source witness, not a second editable Site master or legal entity declaration.
        CREATE FUNCTION cp6_physical_site_identity_v1.payload(p jsonb) RETURNS text
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog AS $$
          SELECT jsonb_build_object('siteId',p->'Id','tenantId',p->'TenantId',
            'siteCode',p->'SiteCode','siteName',p->'SiteName','enabled',p->'Enable','isDeleted',p->'IsDeleted')::text
        $$;
        CREATE FUNCTION cp6_physical_site_identity_v1.capture(p jsonb,present boolean) RETURNS void
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        DECLARE t uuid=(p->>'TenantId')::uuid; i uuid=(p->>'Id')::uuid; v bigint; body text;
        BEGIN
          INSERT INTO cp6_physical_site_identity_v1.heads(tenant_id,site_id,revision) VALUES(t,i,1)
          ON CONFLICT(tenant_id,site_id) DO UPDATE SET revision=cp6_physical_site_identity_v1.heads.revision+1
          RETURNING revision INTO v;
          body=cp6_physical_site_identity_v1.payload(p);
          INSERT INTO cp6_physical_site_identity_v1.versions(tenant_id,site_id,revision,source_present,source_json,source_digest)
          VALUES(t,i,v,present,body,encode(sha256(convert_to(body,'UTF8')),'hex'));
        END $$;
        CREATE FUNCTION cp6_physical_site_identity_v1.record_change() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='DELETE' THEN PERFORM cp6_physical_site_identity_v1.capture(to_jsonb(OLD),false); RETURN OLD; END IF;
          IF TG_OP='UPDATE' AND (OLD."TenantId",OLD."Id") IS DISTINCT FROM (NEW."TenantId",NEW."Id") THEN
            PERFORM cp6_physical_site_identity_v1.capture(to_jsonb(OLD),false);
          END IF;
          PERFORM cp6_physical_site_identity_v1.capture(to_jsonb(NEW),true); RETURN NEW;
        END $$;
        CREATE FUNCTION cp6_physical_site_identity_v1.immutable() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'PHYSICAL_SITE_IDENTITY_ORIGINAL_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE FUNCTION cp6_physical_site_identity_v1.advance_head() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF (OLD.tenant_id,OLD.site_id) IS DISTINCT FROM (NEW.tenant_id,NEW.site_id)
            OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'PHYSICAL_SITE_IDENTITY_FRONTIER_INVALID' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER physical_site_identity_head_monotonic BEFORE UPDATE ON cp6_physical_site_identity_v1.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_physical_site_identity_v1.advance_head();
        CREATE TRIGGER physical_site_identity_original_immutable BEFORE UPDATE OR DELETE ON cp6_physical_site_identity_v1.versions
          FOR EACH ROW EXECUTE FUNCTION cp6_physical_site_identity_v1.immutable();
        CREATE TRIGGER physical_site_identity_original_no_truncate BEFORE TRUNCATE ON cp6_physical_site_identity_v1.versions
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_physical_site_identity_v1.immutable();
        CREATE TRIGGER physical_site_identity_head_no_delete BEFORE DELETE ON cp6_physical_site_identity_v1.heads
          FOR EACH ROW EXECUTE FUNCTION cp6_physical_site_identity_v1.immutable();
        CREATE TRIGGER physical_site_identity_head_no_truncate BEFORE TRUNCATE ON cp6_physical_site_identity_v1.heads
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_physical_site_identity_v1.immutable();
        LOCK TABLE public."Space_Site" IN SHARE ROW EXCLUSIVE MODE;
        CREATE TRIGGER physical_site_identity_capture AFTER INSERT OR UPDATE OR DELETE ON public."Space_Site"
          FOR EACH ROW EXECUTE FUNCTION cp6_physical_site_identity_v1.record_change();
        CREATE TRIGGER physical_site_identity_no_truncate BEFORE TRUNCATE ON public."Space_Site"
          FOR EACH STATEMENT EXECUTE FUNCTION cp6_physical_site_identity_v1.immutable();
        -- Existing rows start at the actual migration observation; no earlier history is invented.
        SELECT cp6_physical_site_identity_v1.capture(to_jsonb(p),true) FROM public."Space_Site" p ORDER BY p."TenantId",p."Id";
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_physical_site_identity_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_physical_site_identity_v1 FROM PUBLIC;
        -- Read capability only; existing Space writes and role grants remain unchanged.
        INSERT INTO public."Sys_MenuAction"("Id","MenuId","ActionCode","ActionName","Sort","TenantId","CreateDate")
        SELECT gen_random_uuid(),m."MenuId",'query','读取物理站点准确来源',0,t."Id",LOCALTIMESTAMP
        FROM public."Sys_Menus" m CROSS JOIN public."Sys_Tenants" t
        WHERE m."MenuKey"='space-site' AND NOT EXISTS(SELECT FROM public."Sys_MenuAction" a
          WHERE a."TenantId"=t."Id" AND a."MenuId"=m."MenuId" AND a."ActionCode"='query');
        """);
    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Physical Site source history requires forward recovery.");
}
