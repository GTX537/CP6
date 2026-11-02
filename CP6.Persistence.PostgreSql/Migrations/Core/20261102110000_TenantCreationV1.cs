using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261102110000_TenantCreationV1")]
public sealed class TenantCreationV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE SCHEMA cp6_tenant_creation_v1;
        REVOKE ALL ON SCHEMA cp6_tenant_creation_v1 FROM PUBLIC;
        CREATE TABLE cp6_tenant_creation_v1.commands (
          operation_hash bytea PRIMARY KEY CHECK(octet_length(operation_hash)=32),
          operation_bytes bytea NOT NULL, input_bytes bytea NOT NULL,
          actor_id uuid NOT NULL, actor_tenant uuid NOT NULL, result_bytes bytea NOT NULL
        );
        CREATE FUNCTION cp6_tenant_creation_v1.immutable() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog AS $$
        BEGIN RAISE EXCEPTION 'TENANT_CREATION_HISTORY_IMMUTABLE' USING ERRCODE='55000'; END $$;
        CREATE TRIGGER cp6_tenant_creation_immutable_v1 BEFORE UPDATE OR DELETE OR TRUNCATE
          ON cp6_tenant_creation_v1.commands FOR EACH STATEMENT EXECUTE FUNCTION cp6_tenant_creation_v1.immutable();
        REVOKE ALL ON ALL TABLES IN SCHEMA cp6_tenant_creation_v1 FROM PUBLIC;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_tenant_creation_v1 FROM PUBLIC;
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException("Tenant creation receipts require forward recovery.");
}
