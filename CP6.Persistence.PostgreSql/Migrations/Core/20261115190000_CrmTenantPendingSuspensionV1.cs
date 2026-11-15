using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261115190000_CrmTenantPendingSuspensionV1")]
public sealed class CrmTenantPendingSuspensionV1 : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE cp6_tenant_management_v1.pending_suspensions(
          tenant_id uuid PRIMARY KEY REFERENCES cp6_tenant_management_v1.versions,
          is_legacy boolean NOT NULL,
          operation_hash bytea NOT NULL CHECK(octet_length(operation_hash)=32),
          operation_bytes bytea NOT NULL, input_bytes bytea NOT NULL,
          actor_id uuid NOT NULL, actor_tenant uuid NOT NULL,
          expected_revision bigint NOT NULL CHECK(expected_revision>0),
          store_id uuid NOT NULL, source_version integer NOT NULL CHECK(source_version>0));
        REVOKE ALL ON TABLE cp6_tenant_management_v1.pending_suspensions FROM PUBLIC;

        CREATE FUNCTION cp6_tenant_management_v1.protect_pending_suspension() RETURNS trigger
        LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog AS $$
        BEGIN
          IF TG_OP='TRUNCATE' THEN
            IF NOT EXISTS(SELECT 1 FROM cp6_tenant_management_v1.pending_suspensions) THEN RETURN NULL; END IF;
          ELSIF NOT EXISTS(SELECT 1 FROM cp6_tenant_management_v1.pending_suspensions p WHERE p.tenant_id=OLD."Id") THEN
            RETURN NULL;
          END IF;
          RAISE EXCEPTION USING ERRCODE='23514', MESSAGE='TENANT_SUSPENSION_PENDING';
        END $$;
        REVOKE ALL ON FUNCTION cp6_tenant_management_v1.protect_pending_suspension() FROM PUBLIC;
        CREATE TRIGGER tenant_management_pending_suspension
          AFTER UPDATE OF "Id","TenantCode","TenantName","Enable","ExpireDate","Remark","TimeZoneId" OR DELETE
          ON public."Sys_Tenants" FOR EACH ROW EXECUTE FUNCTION cp6_tenant_management_v1.protect_pending_suspension();
        CREATE TRIGGER tenant_management_pending_suspension_truncate
          AFTER TRUNCATE ON public."Sys_Tenants" FOR EACH STATEMENT
          EXECUTE FUNCTION cp6_tenant_management_v1.protect_pending_suspension();
        """);

    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Pending CRM tenant suspensions require forward recovery.");
}
