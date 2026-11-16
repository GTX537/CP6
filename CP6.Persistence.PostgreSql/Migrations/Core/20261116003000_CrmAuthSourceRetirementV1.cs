using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql.Migrations.Core;

[DbContext(typeof(CP6Context)), Migration("20261116003000_CrmAuthSourceRetirementV1")]
public sealed class CrmAuthSourceRetirementV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE cp6_authentication_v1.crm_auth_source_retirements_v1 (
          operation_id uuid PRIMARY KEY,
          tenant_id uuid NOT NULL,
          subject_id uuid NOT NULL,
          store_id uuid NOT NULL,
          issuer text COLLATE "C" NOT NULL,
          source_jti text COLLATE "C",
          refresh_hash text COLLATE "C" NOT NULL CHECK (refresh_hash ~ '^[0-9A-F]{64}$'),
          replacement_hash text COLLATE "C" CHECK (replacement_hash ~ '^[0-9A-F]{64}$'),
          access_audit jsonb,
          grant_plan jsonb NOT NULL CHECK (jsonb_typeof(grant_plan)='array' AND jsonb_array_length(grant_plan)>0),
          completed_at_utc timestamptz,
          CHECK ((source_jti IS NULL AND replacement_hash IS NOT NULL AND access_audit IS NULL) OR
                 (source_jti IS NOT NULL AND replacement_hash IS NULL AND access_audit IS NOT NULL AND jsonb_typeof(access_audit)='object'))
        );
        CREATE UNIQUE INDEX crm_auth_source_retirements_access_v1
          ON cp6_authentication_v1.crm_auth_source_retirements_v1(tenant_id,subject_id,source_jti) WHERE source_jti IS NOT NULL;
        CREATE UNIQUE INDEX crm_auth_source_retirements_reuse_v1
          ON cp6_authentication_v1.crm_auth_source_retirements_v1(tenant_id,subject_id) WHERE source_jti IS NULL AND completed_at_utc IS NULL;
        CREATE INDEX crm_auth_source_retirements_pending_v1
          ON cp6_authentication_v1.crm_auth_source_retirements_v1(operation_id) WHERE completed_at_utc IS NULL;
        REVOKE ALL ON TABLE cp6_authentication_v1.crm_auth_source_retirements_v1 FROM PUBLIC;
        CREATE TRIGGER cp6_authentication_gate_v1 BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
          ON cp6_authentication_v1.crm_auth_source_retirements_v1 FOR EACH STATEMENT
          EXECUTE FUNCTION cp6_authentication_v1.before_change();
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("Accepted CRM authentication source retirements require forward recovery.");
}
