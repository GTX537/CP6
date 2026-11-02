using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102100000_TenantManagementV1")]
public sealed class TenantManagementV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        LOCK TABLE public."Sys_Tenants" IN SHARE ROW EXCLUSIVE MODE;
        CREATE SCHEMA cp6_tenant_management_v1;
        CREATE TABLE cp6_tenant_management_v1.versions(tenant_id uuid PRIMARY KEY,revision bigint NOT NULL CHECK(revision>0));
        INSERT INTO cp6_tenant_management_v1.versions SELECT "Id",1 FROM public."Sys_Tenants";
        CREATE TABLE cp6_tenant_management_v1.commands(
          tenant_id uuid NOT NULL,operation_hash bytea NOT NULL CHECK(octet_length(operation_hash)=32),
          operation_bytes bytea NOT NULL,input_bytes bytea NOT NULL,actor_id uuid NOT NULL,actor_tenant uuid NOT NULL,result_bytes bytea NOT NULL,
          PRIMARY KEY(tenant_id,operation_hash), FOREIGN KEY(tenant_id) REFERENCES cp6_tenant_management_v1.versions);
        CREATE FUNCTION cp6_tenant_management_v1.immutable() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'TENANT_MANAGEMENT_HISTORY_IMMUTABLE' USING ERRCODE='23514'; END $$;
        CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON cp6_tenant_management_v1.commands FOR EACH ROW EXECUTE FUNCTION cp6_tenant_management_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_tenant_management_v1.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_tenant_management_v1.immutable();
        CREATE TRIGGER no_delete BEFORE DELETE ON cp6_tenant_management_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_tenant_management_v1.immutable();
        CREATE TRIGGER no_truncate BEFORE TRUNCATE ON cp6_tenant_management_v1.versions FOR EACH STATEMENT EXECUTE FUNCTION cp6_tenant_management_v1.immutable();
        CREATE FUNCTION cp6_tenant_management_v1.advance() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW.revision<>1 THEN RAISE EXCEPTION 'TENANT_REVISION_INVALID' USING ERRCODE='23514'; END IF;
          ELSIF NEW.tenant_id<>OLD.tenant_id OR NEW.revision<>OLD.revision+1 THEN
            RAISE EXCEPTION 'TENANT_REVISION_INVALID' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER advance BEFORE INSERT OR UPDATE ON cp6_tenant_management_v1.versions FOR EACH ROW EXECUTE FUNCTION cp6_tenant_management_v1.advance();
        -- The pre-existing statement-level authentication gate runs before row writes.
        -- Keep tombstones so delete/recreate and all old writers remain visible to CAS.
        CREATE FUNCTION cp6_tenant_management_v1.changed() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            UPDATE cp6_tenant_management_v1.versions SET revision=revision+1; RETURN NULL;
          END IF;
          IF TG_OP='UPDATE' AND ROW(OLD."Id",bpcharsend(OLD."TenantCode"),bpcharsend(OLD."TenantName"),OLD."Enable",OLD."ExpireDate",bpcharsend(OLD."Remark"),bpcharsend(OLD."TimeZoneId"))
              IS NOT DISTINCT FROM ROW(NEW."Id",bpcharsend(NEW."TenantCode"),bpcharsend(NEW."TenantName"),NEW."Enable",NEW."ExpireDate",bpcharsend(NEW."Remark"),bpcharsend(NEW."TimeZoneId")) THEN
            RETURN NULL;
          END IF;
          IF TG_OP='DELETE' OR (TG_OP='UPDATE' AND OLD."Id"<>NEW."Id") THEN
            UPDATE cp6_tenant_management_v1.versions SET revision=revision+1 WHERE tenant_id=OLD."Id";
          END IF;
          IF TG_OP<>'DELETE' THEN
            INSERT INTO cp6_tenant_management_v1.versions VALUES(NEW."Id",1)
              ON CONFLICT(tenant_id) DO UPDATE SET revision=cp6_tenant_management_v1.versions.revision+1;
          END IF;
          RETURN NULL;
        END $$;
        CREATE TRIGGER tenant_management_changed AFTER INSERT OR UPDATE OR DELETE ON public."Sys_Tenants" FOR EACH ROW EXECUTE FUNCTION cp6_tenant_management_v1.changed();
        CREATE TRIGGER tenant_management_truncate AFTER TRUNCATE ON public."Sys_Tenants" FOR EACH STATEMENT EXECUTE FUNCTION cp6_tenant_management_v1.changed();
        REVOKE ALL ON SCHEMA cp6_tenant_management_v1 FROM PUBLIC;
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_tenant_management_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_tenant_management_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Tenant management revisions and original commands are forward-only.");
}
